using FluentAssertions;
using GearFlow.Contracts;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Application.Notifications;
using GearFlow.Inventory.Application.Notifications.NotifyLowStock;
using GearFlow.Inventory.Application.Notifications.NotifyStockShortage;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GearFlow.Inventory.UnitTests.Application;

public sealed class NotificationHandlerTests
{
    private readonly IEmailSender _email = Substitute.For<IEmailSender>();
    private readonly StockAlertOptions _options = new() { AlertEmail = "estoque@oficina.test" };

    [Fact]
    public async Task Low_stock_email_goes_to_the_configured_address_with_the_monolith_text()
    {
        var handler = new NotifyLowStockCommandHandler(_email, _options, NullLogger<NotifyLowStockCommandHandler>.Instance);

        var result = await handler.HandleAsync(
            new NotifyLowStockCommand(InventoryItemType.Part, "Pastilha de freio", 4m, 5m), default);

        result.IsSuccess.Should().BeTrue();
        await _email.Received(1).SendAsync(
            "estoque@oficina.test",
            "Estoque baixo: Pastilha de freio",
            "O item 'Pastilha de freio' (Part) está em 4 (mínimo 5).",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Low_stock_command_is_built_from_the_alert_message()
    {
        var message = new LowStockAlert(Guid.NewGuid(), DateTime.UtcNow, InventoryItemType.Consumable, Guid.NewGuid(), "Óleo", 3.5m, 5m);

        var command = NotifyLowStockCommand.From(message);

        command.Should().Be(new NotifyLowStockCommand(InventoryItemType.Consumable, "Óleo", 3.5m, 5m));
    }

    [Fact]
    public async Task Stock_shortage_email_lists_every_missing_item()
    {
        var serviceOrderId = Guid.NewGuid();
        var handler = new NotifyStockShortageCommandHandler(_email, _options, NullLogger<NotifyStockShortageCommandHandler>.Instance);
        var missing = new List<MissingStockItem>
        {
            new(InventoryItemType.Part, Guid.NewGuid(), "Jogo de palhetas", 5m, 4m),
            new(InventoryItemType.Consumable, Guid.NewGuid(), "Água desmineralizada", 10m, 0m),
        };

        var result = await handler.HandleAsync(new NotifyStockShortageCommand(serviceOrderId, missing), default);

        result.IsSuccess.Should().BeTrue();
        await _email.Received(1).SendAsync(
            "estoque@oficina.test",
            $"Estoque insuficiente para a OS {serviceOrderId}",
            Arg.Is<string>(body =>
                body.Contains($"ordem de serviço {serviceOrderId}") &&
                body.Contains("Jogo de palhetas (Part): pedido 5, disponível 4") &&
                body.Contains("Água desmineralizada (Consumable): pedido 10, disponível 0")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stock_shortage_command_uses_the_correlation_id_as_service_order()
    {
        var serviceOrderId = Guid.NewGuid();
        var message = new StockInsufficient(serviceOrderId, Guid.NewGuid(), DateTime.UtcNow,
            [new MissingStockItem(InventoryItemType.Part, Guid.NewGuid(), "Filtro", 2m, 1m)]);

        var command = NotifyStockShortageCommand.From(message);

        command.ServiceOrderId.Should().Be(serviceOrderId);
        command.Missing.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_failing_smtp_propagates_so_the_consumer_retries()
    {
        _email.SendAsync(default!, default!, default!, default).ReturnsForAnyArgs<Task>(_ => throw new InvalidOperationException("smtp fora do ar"));
        var handler = new NotifyLowStockCommandHandler(_email, _options, NullLogger<NotifyLowStockCommandHandler>.Instance);

        var act = () => handler.HandleAsync(new NotifyLowStockCommand(InventoryItemType.Part, "x", 1m, 5m), default);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("smtp fora do ar");
    }
}
