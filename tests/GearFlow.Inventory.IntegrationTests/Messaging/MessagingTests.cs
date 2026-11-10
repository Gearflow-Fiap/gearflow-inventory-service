using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using GearFlow.Contracts;
using GearFlow.Inventory.Domain.Reservations;
using GearFlow.Inventory.Domain.Stock;
using GearFlow.Inventory.Infrastructure.Messaging;
using GearFlow.Inventory.IntegrationTests.Fixtures;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace GearFlow.Inventory.IntegrationTests.Messaging;

/// <summary>
/// A Saga vista de fora: a API real (consumers, Outbox/Inbox, e-mail) sobre Postgres e RabbitMQ reais. O bus de
/// teste faz o papel do orquestrador: publica os comandos e espera os eventos de resposta.
/// </summary>
[Collection(InfrastructureCollection.Name)]
public sealed class MessagingTests(InfrastructureFixture fx) : IAsyncLifetime
{
    // Sobe a API uma vez: é ela que declara as filas e os bindings no RabbitMQ (WaitUntilStarted).
    public Task InitializeAsync()
    {
        fx.Api.CreateClient().Dispose();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static StockItemLine PartLine(Guid id, decimal quantity) => new(InventoryItemType.Part, id, quantity);
    private static StockItemLine ConsumableLine(Guid id, decimal quantity) => new(InventoryItemType.Consumable, id, quantity);

    private Task Reserve(Guid serviceOrderId, StockItemLine[] items, Guid? messageId = null) =>
        fx.Bus.Bus.Publish(new ReserveStock(serviceOrderId, Guid.NewGuid(), DateTime.UtcNow, items),
            context => { if (messageId is { } id) context.MessageId = id; });

    private Task Release(Guid serviceOrderId) =>
        fx.Bus.Bus.Publish(new ReleaseStock(serviceOrderId, Guid.NewGuid(), DateTime.UtcNow, "pagamento recusado"));

    private Task Consume(Guid serviceOrderId) =>
        fx.Bus.Bus.Publish(new ConsumeStock(serviceOrderId, Guid.NewGuid(), DateTime.UtcNow));

    private async Task<(int Available, int Reserved)> PartBalance(Guid partId)
    {
        await using var db = fx.NewDbContext();
        var part = await db.Parts.AsNoTracking().SingleAsync(p => p.Id == PartId.From(partId));
        return (part.Quantity, part.ReservedQuantity);
    }

    private async Task<ReservationStatus?> StatusOf(Guid serviceOrderId)
    {
        await using var db = fx.NewDbContext();
        var reservation = await db.StockReservations.AsNoTracking().SingleOrDefaultAsync(r => r.ServiceOrderId == serviceOrderId);
        return reservation?.Status;
    }

    // ---- Topologia: exchanges dos eventos --------------------------------------

    [Fact]
    public async Task Events_go_out_on_inventory_dot_fact_exchanges_and_never_on_the_clr_type_name()
    {
        var part = await fx.SeedPartAsync(10);
        var serviceOrderId = Guid.NewGuid();
        await Reserve(serviceOrderId, [PartLine(part, 1)]);
        await fx.Bus.WaitForAsync<StockReserved>(m => m.CorrelationId == serviceOrderId);

        // O consumidor do teste só recebeu porque o Inventory publicou em inventory.stock-reserved.
        (await fx.Management.ExchangeExistsAsync("inventory.stock-reserved")).Should().BeTrue();
        // Os consumers de e-mail do próprio Inventory ligam as filas a essas exchanges já no startup.
        (await fx.Management.ExchangeExistsAsync("inventory.stock-insufficient")).Should().BeTrue();
        (await fx.Management.ExchangeExistsAsync("inventory.low-stock-alert")).Should().BeTrue();

        foreach (var (type, exchange) in InventoryEventNames.All)
            (await fx.Management.ExchangeExistsAsync($"{type.Namespace}:{type.Name}")).Should()
                .BeFalse($"{type.Name} é evento e vai para {exchange}, não para a exchange do tipo CLR");
    }

    // ---- Reserva -------------------------------------------------------------

    [Fact]
    public async Task ReserveStock_reserves_replies_StockReserved_and_keeps_the_correlation_with_the_service_order()
    {
        var part = await fx.SeedPartAsync(10);
        var oil = await fx.SeedConsumableAsync(20m);
        var serviceOrderId = Guid.NewGuid();

        await Reserve(serviceOrderId, [PartLine(part, 3), ConsumableLine(oil, 4.5m)]);

        var reply = await fx.Bus.WaitForAsync<StockReserved>(m => m.CorrelationId == serviceOrderId);
        ((StockReserved)reply.Message).Lines.Should().BeEquivalentTo([PartLine(part, 3m), ConsumableLine(oil, 4.5m)]);
        reply.EnvelopeCorrelationId.Should().Be(serviceOrderId, "o envelope precisa carregar o CorrelationId (= OS)");
        (await PartBalance(part)).Should().Be((7, 3));
        (await StatusOf(serviceOrderId)).Should().Be(ReservationStatus.Reserved);
    }

    [Fact]
    public async Task ReserveStock_sent_straight_to_the_queue_works_like_a_published_one()
    {
        var part = await fx.SeedPartAsync(10);
        var serviceOrderId = Guid.NewGuid();

        var endpoint = await fx.Bus.Bus.GetSendEndpoint(new Uri($"queue:{QueueNames.ReserveStock}"));
        await endpoint.Send(new ReserveStock(serviceOrderId, Guid.NewGuid(), DateTime.UtcNow, [PartLine(part, 2)]));

        await fx.Bus.WaitForAsync<StockReserved>(m => m.CorrelationId == serviceOrderId);
        (await PartBalance(part)).Should().Be((8, 2));
    }

    [Fact]
    public async Task ReserveStock_without_enough_stock_reserves_nothing_replies_StockInsufficient_and_emails_the_stockist()
    {
        var plenty = await fx.SeedPartAsync(10);
        var scarce = await fx.SeedPartAsync(2, "Pastilha escassa");
        var serviceOrderId = Guid.NewGuid();

        await Reserve(serviceOrderId, [PartLine(plenty, 5), PartLine(scarce, 5)]);

        var reply = await fx.Bus.WaitForAsync<StockInsufficient>(m => m.CorrelationId == serviceOrderId);
        ((StockInsufficient)reply.Message).Missing.Should().ContainSingle()
            .Which.Should().Be(new MissingStockItem(InventoryItemType.Part, scarce, "Pastilha escassa", 5m, 2m));
        reply.EnvelopeCorrelationId.Should().Be(serviceOrderId);

        // Tudo ou nada: o item com saldo também ficou intacto, e nenhuma reserva foi gravada.
        (await PartBalance(plenty)).Should().Be((10, 0));
        (await PartBalance(scarce)).Should().Be((2, 0));
        (await StatusOf(serviceOrderId)).Should().BeNull();
        fx.Bus.Of<StockReserved>(m => m.CorrelationId == serviceOrderId).Should().BeEmpty();

        // O aviso por e-mail sai por consumer próprio, depois do commit.
        await Eventually.UntilAsync(
            () => Task.FromResult(fx.Api.Emails.Emails.Any(e => e.Subject.Contains(serviceOrderId.ToString()))),
            "e-mail de estoque insuficiente");
        var email = fx.Api.Emails.Emails.Single(e => e.Subject.Contains(serviceOrderId.ToString()));
        email.To.Should().Be("estoque@oficina.test");
        email.Body.Should().Contain("Pastilha escassa").And.Contain("pedido 5, disponível 2");
    }

    [Fact]
    public async Task The_same_message_delivered_twice_is_processed_once_by_the_inbox()
    {
        var part = await fx.SeedPartAsync(10);
        var serviceOrderId = Guid.NewGuid();
        var sameMessage = Guid.NewGuid();

        await Reserve(serviceOrderId, [PartLine(part, 3)], sameMessage);
        await Reserve(serviceOrderId, [PartLine(part, 3)], sameMessage);

        await fx.Bus.WaitForAsync<StockReserved>(m => m.CorrelationId == serviceOrderId);
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Se o Inbox não deduplicasse, o handler (idempotente) responderia de novo e teríamos 2 respostas.
        fx.Bus.Of<StockReserved>(m => m.CorrelationId == serviceOrderId).Should().HaveCount(1);
        (await PartBalance(part)).Should().Be((7, 3));
    }

    [Fact]
    public async Task A_new_ReserveStock_for_an_already_reserved_order_does_not_reserve_twice_and_answers_again()
    {
        var part = await fx.SeedPartAsync(10);
        var serviceOrderId = Guid.NewGuid();

        await Reserve(serviceOrderId, [PartLine(part, 3)]);
        await fx.Bus.WaitForAsync<StockReserved>(m => m.CorrelationId == serviceOrderId);

        await Reserve(serviceOrderId, [PartLine(part, 3)]); // outro MessageId: a idempotência agora é do domínio
        await Eventually.UntilAsync(
            () => Task.FromResult(fx.Bus.Of<StockReserved>(m => m.CorrelationId == serviceOrderId).Count == 2),
            "segunda resposta StockReserved");

        (await PartBalance(part)).Should().Be((7, 3));
    }

    [Fact]
    public async Task Two_service_orders_competing_for_the_last_unit_end_with_exactly_one_reservation()
    {
        var part = await fx.SeedPartAsync(1);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        await Task.WhenAll(Reserve(first, [PartLine(part, 1)]), Reserve(second, [PartLine(part, 1)]));

        bool Answered(Guid os) =>
            fx.Bus.Of<StockReserved>(m => m.CorrelationId == os).Count + fx.Bus.Of<StockInsufficient>(m => m.CorrelationId == os).Count > 0;
        await Eventually.UntilAsync(() => Task.FromResult(Answered(first) && Answered(second)), "as duas OS foram respondidas");

        var reserved = fx.Bus.Of<StockReserved>(m => m.CorrelationId == first || m.CorrelationId == second).Count;
        var insufficient = fx.Bus.Of<StockInsufficient>(m => m.CorrelationId == first || m.CorrelationId == second).Count;
        reserved.Should().Be(1, "só uma OS pode ficar com a última unidade");
        insufficient.Should().Be(1, "a outra recebe StockInsufficient, nunca um saldo negativo");
        (await PartBalance(part)).Should().Be((0, 1));
    }

    // ---- Compensação ---------------------------------------------------------

    [Fact]
    public async Task ReleaseStock_gives_the_stock_back_and_replies_StockReleased()
    {
        var part = await fx.SeedPartAsync(10);
        var serviceOrderId = Guid.NewGuid();
        await Reserve(serviceOrderId, [PartLine(part, 4)]);
        await fx.Bus.WaitForAsync<StockReserved>(m => m.CorrelationId == serviceOrderId);

        await Release(serviceOrderId);

        var reply = await fx.Bus.WaitForAsync<StockReleased>(m => m.CorrelationId == serviceOrderId);
        reply.EnvelopeCorrelationId.Should().Be(serviceOrderId);
        (await PartBalance(part)).Should().Be((10, 0));
        (await StatusOf(serviceOrderId)).Should().Be(ReservationStatus.Released);
    }

    [Fact]
    public async Task ReleaseStock_arriving_before_ReserveStock_makes_the_late_reservation_a_no_op()
    {
        var part = await fx.SeedPartAsync(10);
        var serviceOrderId = Guid.NewGuid();

        await Release(serviceOrderId);
        await fx.Bus.WaitForAsync<StockReleased>(m => m.CorrelationId == serviceOrderId);
        await Reserve(serviceOrderId, [PartLine(part, 4)]);

        await Eventually.NeverAsync(
            () => fx.Bus.Of<StockReserved>(m => m.CorrelationId == serviceOrderId).Any(),
            "o ReserveStock atrasado não pode reservar depois da compensação", TimeSpan.FromSeconds(3));
        (await PartBalance(part)).Should().Be((10, 0));
        (await StatusOf(serviceOrderId)).Should().Be(ReservationStatus.Released);
    }

    // ---- Consumo -------------------------------------------------------------

    [Fact]
    public async Task ConsumeStock_consumes_replies_StockConsumed_and_alerts_the_stockist_when_the_item_gets_low()
    {
        var name = $"Pastilha {Guid.NewGuid():N}";
        var part = await fx.SeedPartAsync(9, name);
        var serviceOrderId = Guid.NewGuid();
        await Reserve(serviceOrderId, [PartLine(part, 4)]); // sobram 5, que é o mínimo
        await fx.Bus.WaitForAsync<StockReserved>(m => m.CorrelationId == serviceOrderId);

        await Consume(serviceOrderId);

        await fx.Bus.WaitForAsync<StockConsumed>(m => m.CorrelationId == serviceOrderId);
        (await PartBalance(part)).Should().Be((5, 0));
        (await StatusOf(serviceOrderId)).Should().Be(ReservationStatus.Consumed);

        var alert = (LowStockAlert)(await fx.Bus.WaitForAsync<LowStockAlert>(m => m.ItemId == part)).Message;
        alert.ItemName.Should().Be(name);
        alert.Remaining.Should().Be(5m);
        alert.Minimum.Should().Be(5m);

        await Eventually.UntilAsync(
            () => Task.FromResult(fx.Api.Emails.Emails.Any(e => e.Subject == $"Estoque baixo: {name}")),
            "e-mail de estoque baixo");
    }

    // ---- Falhas permanentes vão direto para a DLQ ------------------------------

    [Fact]
    public async Task A_malformed_ReserveStock_goes_straight_to_the_error_queue_without_replies()
    {
        var errorQueue = $"{QueueNames.ReserveStock}_error";
        var before = await fx.Management.MessageCountAsync(errorQueue);
        var serviceOrderId = Guid.NewGuid();

        await Reserve(serviceOrderId, []); // sem itens: o validator recusa e a falha é permanente

        await Eventually.UntilAsync(async () => await fx.Management.MessageCountAsync(errorQueue) > before,
            "mensagem malformada na fila _error");

        var headers = await fx.Management.PeekHeadersAsync(errorQueue, 50);
        headers.Any(h => h.TryGetValue("MT-Fault-ExceptionType", out var type)
                         && type.GetString()!.Contains(nameof(PermanentMessageException)))
            .Should().BeTrue("a falha de uma mensagem malformada é permanente e não deve ser repetida");
        fx.Bus.Of<StockReserved>(m => m.CorrelationId == serviceOrderId).Should().BeEmpty();
        fx.Bus.Of<StockInsufficient>(m => m.CorrelationId == serviceOrderId).Should().BeEmpty();
    }

    [Fact]
    public async Task ConsumeStock_without_a_reservation_goes_to_the_error_queue()
    {
        var errorQueue = $"{QueueNames.ConsumeStock}_error";
        var before = await fx.Management.MessageCountAsync(errorQueue);

        await Consume(Guid.NewGuid());

        await Eventually.UntilAsync(async () => await fx.Management.MessageCountAsync(errorQueue) > before,
            "ConsumeStock sem reserva na fila _error");
    }

    // ---- REST: PATCH de estoque publica pelo Outbox do bus ---------------------

    [Fact]
    public async Task Patching_the_stock_over_http_publishes_StockReplenished_through_the_bus_outbox()
    {
        var part = await fx.SeedPartAsync(2);
        using var client = fx.Api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Staff());

        var response = await client.PatchAsJsonAsync($"/api/inventory/parts/{part}/stock", new { quantity = 5 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var replenished = (StockReplenished)(await fx.Bus.WaitForAsync<StockReplenished>(m => m.ItemId == part)).Message;
        replenished.ItemType.Should().Be(InventoryItemType.Part);
        (await PartBalance(part)).Should().Be((7, 0));
    }

    [Fact]
    public async Task The_api_rejects_requests_without_a_token()
    {
        using var client = fx.Api.CreateClient();

        (await client.GetAsync("/api/inventory/parts")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_ready_probe_reports_database_and_broker_healthy()
    {
        using var client = fx.Api.CreateClient();

        var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        body.Should().Contain("Postgres").And.Contain("masstransit");
    }
}
