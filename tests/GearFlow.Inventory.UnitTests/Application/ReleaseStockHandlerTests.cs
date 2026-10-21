using FluentAssertions;
using GearFlow.Contracts;
using GearFlow.Inventory.Application.Reservations.Commands.ConsumeStock;
using GearFlow.Inventory.Application.Reservations.Commands.ReleaseStock;
using GearFlow.Inventory.Application.Reservations.Commands.ReserveStock;
using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Domain.Reservations;
using GearFlow.Inventory.Domain.Stock;

namespace GearFlow.Inventory.UnitTests.Application;

public sealed class ReleaseStockHandlerTests
{
    private readonly StockTestBed _bed = new();
    private readonly Guid _serviceOrderId = Guid.NewGuid();

    private Task Reserve(params StockItemLine[] items) =>
        _bed.Reserve.HandleAsync(new ReserveStockCommand(_serviceOrderId, items), default);

    private ReleaseStockCommand Release(string reason = "pagamento recusado") => new(_serviceOrderId, reason);

    [Fact]
    public async Task Releases_the_reservation_restoring_the_original_balance()
    {
        var part = _bed.AddPart(10);
        var oil = _bed.AddConsumable(20m);
        await Reserve(
            new StockItemLine(InventoryItemType.Part, part.Id.Value, 4),
            new StockItemLine(InventoryItemType.Consumable, oil.Id.Value, 7.5m));

        var result = await _bed.Release.HandleAsync(Release(), default);

        result.IsSuccess.Should().BeTrue();
        part.Quantity.Should().Be(10);
        part.ReservedQuantity.Should().Be(0);
        oil.Quantity.Should().Be(20m);
        oil.ReservedQuantity.Should().Be(0m);
        _bed.Reservations.Items.Single().Status.Should().Be(ReservationStatus.Released);
    }

    [Fact]
    public async Task Publishes_StockReleased()
    {
        var part = _bed.AddPart(10);
        await Reserve(new StockItemLine(InventoryItemType.Part, part.Id.Value, 4));

        await _bed.Release.HandleAsync(Release(), default);

        var message = _bed.Publisher.Of<StockReleased>().Should().ContainSingle().Subject;
        message.CorrelationId.Should().Be(_serviceOrderId);
        message.OccurredOn.Should().Be(StockTestBed.Now);
    }

    [Fact]
    public async Task Is_idempotent_a_second_release_changes_nothing_and_answers_again()
    {
        var part = _bed.AddPart(10);
        await Reserve(new StockItemLine(InventoryItemType.Part, part.Id.Value, 4));
        await _bed.Release.HandleAsync(Release(), default);

        var second = await _bed.Release.HandleAsync(Release(), default);

        second.IsSuccess.Should().BeTrue();
        part.Quantity.Should().Be(10);            // não devolveu 4 de novo
        part.ReservedQuantity.Should().Be(0);
        _bed.Publisher.Of<StockReleased>().Should().HaveCount(2);
    }

    [Fact]
    public async Task Release_with_no_prior_reservation_stores_a_tombstone_and_succeeds()
    {
        var result = await _bed.Release.HandleAsync(Release(), default);

        result.IsSuccess.Should().BeTrue();
        var tombstone = _bed.Reservations.Items.Should().ContainSingle().Subject;
        tombstone.Status.Should().Be(ReservationStatus.Released);
        tombstone.Lines.Should().BeEmpty();
        _bed.Publisher.Of<StockReleased>().Should().ContainSingle();
    }

    [Fact]
    public async Task A_late_ReserveStock_after_that_tombstone_is_ignored()
    {
        // Mensagens fora de ordem: o ReleaseStock chegou antes do ReserveStock.
        var part = _bed.AddPart(10);
        await _bed.Release.HandleAsync(Release(), default);

        var late = await _bed.Reserve.HandleAsync(
            new ReserveStockCommand(_serviceOrderId, [new StockItemLine(InventoryItemType.Part, part.Id.Value, 4)]), default);

        late.Value.Status.Should().Be(ReserveStockStatus.IgnoredAfterRelease);
        part.Quantity.Should().Be(10);
        part.ReservedQuantity.Should().Be(0);
    }

    [Fact]
    public async Task Release_after_consume_is_a_conflict_and_changes_nothing()
    {
        var part = _bed.AddPart(10);
        await Reserve(new StockItemLine(InventoryItemType.Part, part.Id.Value, 4));
        await _bed.Consume.HandleAsync(new ConsumeStockCommand(_serviceOrderId), default);
        var publishedBefore = _bed.Publisher.Messages.Count;

        var result = await _bed.Release.HandleAsync(Release(), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Reservation.AlreadyConsumed");
        result.Error.Type.Should().Be(ErrorType.Conflict);
        part.Quantity.Should().Be(6);
        part.ReservedQuantity.Should().Be(0);
        _bed.Reservations.Items.Single().Status.Should().Be(ReservationStatus.Consumed);
        _bed.Publisher.Messages.Should().HaveCount(publishedBefore);
    }

    [Fact]
    public async Task Item_that_no_longer_exists_does_not_block_the_compensation()
    {
        var kept = _bed.AddPart(10, "PN-A");
        var removed = _bed.AddPart(10, "PN-B", "Pastilha");
        await Reserve(
            new StockItemLine(InventoryItemType.Part, kept.Id.Value, 2),
            new StockItemLine(InventoryItemType.Part, removed.Id.Value, 3));
        _bed.Parts.Items.Remove(removed);

        var result = await _bed.Release.HandleAsync(Release(), default);

        result.IsSuccess.Should().BeTrue();
        kept.Quantity.Should().Be(10);
        kept.ReservedQuantity.Should().Be(0);
        _bed.Reservations.Items.Single().Status.Should().Be(ReservationStatus.Released);
    }

    [Fact]
    public async Task Releasing_one_order_does_not_affect_another_orders_reservation()
    {
        var part = _bed.AddPart(10);
        var otherOrder = Guid.NewGuid();
        await Reserve(new StockItemLine(InventoryItemType.Part, part.Id.Value, 3));
        await _bed.Reserve.HandleAsync(
            new ReserveStockCommand(otherOrder, [new StockItemLine(InventoryItemType.Part, part.Id.Value, 2)]), default);

        await _bed.Release.HandleAsync(Release(), default);

        part.Quantity.Should().Be(8);
        part.ReservedQuantity.Should().Be(2); // sobrou só a reserva da outra OS
    }

    [Fact]
    public async Task Release_makes_the_item_removable_again()
    {
        var part = _bed.AddPart(10);
        await Reserve(new StockItemLine(InventoryItemType.Part, part.Id.Value, 3));
        part.EnsureCanBeRemoved().IsFailure.Should().BeTrue();

        await _bed.Release.HandleAsync(Release(), default);

        part.EnsureCanBeRemoved().IsSuccess.Should().BeTrue();
    }
}
