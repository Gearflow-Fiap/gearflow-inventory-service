using FluentAssertions;
using GearFlow.Contracts;
using GearFlow.Inventory.Application.Reservations.Commands.ConsumeStock;
using GearFlow.Inventory.Application.Reservations.Commands.ReleaseStock;
using GearFlow.Inventory.Application.Reservations.Commands.ReserveStock;
using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Domain.Reservations;

namespace GearFlow.Inventory.UnitTests.Application;

public sealed class ConsumeStockHandlerTests
{
    private readonly StockTestBed _bed = new();
    private readonly Guid _serviceOrderId = Guid.NewGuid();

    private Task Reserve(params StockItemLine[] items) =>
        _bed.Reserve.HandleAsync(new ReserveStockCommand(_serviceOrderId, items), default);

    private ConsumeStockCommand Consume() => new(_serviceOrderId);

    [Fact]
    public async Task Consumes_only_the_reservation_leaving_the_available_balance_untouched()
    {
        var part = _bed.AddPart(10);
        var oil = _bed.AddConsumable(20m);
        await Reserve(
            new StockItemLine(InventoryItemType.Part, part.Id.Value, 4),
            new StockItemLine(InventoryItemType.Consumable, oil.Id.Value, 7.5m));

        var result = await _bed.Consume.HandleAsync(Consume(), default);

        result.IsSuccess.Should().BeTrue();
        part.Quantity.Should().Be(6);              // já tinha saído na reserva
        part.ReservedQuantity.Should().Be(0);
        oil.Quantity.Should().Be(12.5m);
        oil.ReservedQuantity.Should().Be(0m);
        _bed.Reservations.Items.Single().Status.Should().Be(ReservationStatus.Consumed);
    }

    [Fact]
    public async Task Publishes_StockConsumed()
    {
        var part = _bed.AddPart(10);
        await Reserve(new StockItemLine(InventoryItemType.Part, part.Id.Value, 4));

        await _bed.Consume.HandleAsync(Consume(), default);

        var message = _bed.Publisher.Of<StockConsumed>().Should().ContainSingle().Subject;
        message.CorrelationId.Should().Be(_serviceOrderId);
    }

    // ---- LowStockAlert ---------------------------------------------------------

    [Fact]
    public async Task Publishes_LowStockAlert_for_an_item_left_at_or_below_the_minimum()
    {
        var part = _bed.AddPart(9, name: "Pastilha");
        await Reserve(new StockItemLine(InventoryItemType.Part, part.Id.Value, 4)); // sobra 5 disponíveis

        await _bed.Consume.HandleAsync(Consume(), default);

        var alert = _bed.Publisher.Of<LowStockAlert>().Should().ContainSingle().Subject;
        alert.ItemType.Should().Be(InventoryItemType.Part);
        alert.ItemId.Should().Be(part.Id.Value);
        alert.ItemName.Should().Be("Pastilha");
        alert.Remaining.Should().Be(5m);
        alert.Minimum.Should().Be(5m);
    }

    [Fact]
    public async Task Publishes_LowStockAlert_for_a_consumable_below_the_minimum()
    {
        var oil = _bed.AddConsumable(8m, "Óleo 5W30");
        await Reserve(new StockItemLine(InventoryItemType.Consumable, oil.Id.Value, 3.5m)); // sobra 4,5

        await _bed.Consume.HandleAsync(Consume(), default);

        var alert = _bed.Publisher.Of<LowStockAlert>().Should().ContainSingle().Subject;
        alert.ItemType.Should().Be(InventoryItemType.Consumable);
        alert.Remaining.Should().Be(4.5m);
    }

    [Fact]
    public async Task Does_not_alert_while_the_stock_stays_above_the_minimum()
    {
        var part = _bed.AddPart(20);
        await Reserve(new StockItemLine(InventoryItemType.Part, part.Id.Value, 4)); // sobra 16

        await _bed.Consume.HandleAsync(Consume(), default);

        _bed.Publisher.Of<LowStockAlert>().Should().BeEmpty();
    }

    [Fact]
    public async Task Alerts_only_for_the_items_that_are_low()
    {
        var low = _bed.AddPart(6, "PN-LOW");
        var fine = _bed.AddPart(50, "PN-FINE", "Vela");
        await Reserve(
            new StockItemLine(InventoryItemType.Part, low.Id.Value, 2),
            new StockItemLine(InventoryItemType.Part, fine.Id.Value, 2));

        await _bed.Consume.HandleAsync(Consume(), default);

        _bed.Publisher.Of<LowStockAlert>().Should().ContainSingle()
            .Which.ItemId.Should().Be(low.Id.Value);
    }

    // ---- Idempotência ----------------------------------------------------------

    [Fact]
    public async Task Is_idempotent_a_second_consume_does_not_consume_again_nor_repeat_the_alert()
    {
        var part = _bed.AddPart(9);
        await Reserve(new StockItemLine(InventoryItemType.Part, part.Id.Value, 4));
        await _bed.Consume.HandleAsync(Consume(), default);

        var second = await _bed.Consume.HandleAsync(Consume(), default);

        second.IsSuccess.Should().BeTrue();
        part.Quantity.Should().Be(5);
        part.ReservedQuantity.Should().Be(0);
        _bed.Publisher.Of<StockConsumed>().Should().HaveCount(2);   // a resposta é reenviada
        _bed.Publisher.Of<LowStockAlert>().Should().HaveCount(1);   // o alerta não
    }

    // ---- Falhas (ponto sem volta: sobem para retry/DLQ) ------------------------

    [Fact]
    public async Task Consume_without_a_reservation_fails_with_not_found()
    {
        var result = await _bed.Consume.HandleAsync(Consume(), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Reservation.NotFound");
        result.Error.Type.Should().Be(ErrorType.NotFound);
        _bed.Publisher.Messages.Should().BeEmpty();
        _bed.UnitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Consume_after_release_is_a_conflict()
    {
        var part = _bed.AddPart(10);
        await Reserve(new StockItemLine(InventoryItemType.Part, part.Id.Value, 4));
        await _bed.Release.HandleAsync(new ReleaseStockCommand(_serviceOrderId, "cancelada"), default);
        var publishedBefore = _bed.Publisher.Messages.Count;

        var result = await _bed.Consume.HandleAsync(Consume(), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Reservation.AlreadyReleased");
        part.Quantity.Should().Be(10);
        _bed.Publisher.Messages.Should().HaveCount(publishedBefore);
    }

    [Fact]
    public async Task Consume_fails_loudly_when_a_reserved_item_disappeared()
    {
        var part = _bed.AddPart(10);
        await Reserve(new StockItemLine(InventoryItemType.Part, part.Id.Value, 4));
        _bed.Parts.Items.Remove(part);

        var result = await _bed.Consume.HandleAsync(Consume(), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Part.NotFound");
        _bed.Reservations.Items.Single().Status.Should().Be(ReservationStatus.Reserved);
    }
}
