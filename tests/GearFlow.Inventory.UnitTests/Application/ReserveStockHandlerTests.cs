using FluentAssertions;
using GearFlow.Contracts;
using GearFlow.Inventory.Application.Reservations.Commands.ReserveStock;
using GearFlow.Inventory.Domain.Reservations;

namespace GearFlow.Inventory.UnitTests.Application;

public sealed class ReserveStockHandlerTests
{
    private readonly StockTestBed _bed = new();
    private readonly Guid _serviceOrderId = Guid.NewGuid();

    private ReserveStockCommand Command(params StockItemLine[] items) => new(_serviceOrderId, items);

    private static StockItemLine PartLine(Guid id, decimal quantity) => new(InventoryItemType.Part, id, quantity);
    private static StockItemLine ConsumableLine(Guid id, decimal quantity) => new(InventoryItemType.Consumable, id, quantity);

    [Fact]
    public async Task Reserves_all_items_moving_available_to_reserved()
    {
        var part = _bed.AddPart(10);
        var oil = _bed.AddConsumable(20m);

        var result = await _bed.Reserve.HandleAsync(
            Command(PartLine(part.Id.Value, 3), ConsumableLine(oil.Id.Value, 4.5m)), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ReserveStockStatus.Reserved);
        part.Quantity.Should().Be(7);
        part.ReservedQuantity.Should().Be(3);
        oil.Quantity.Should().Be(15.5m);
        oil.ReservedQuantity.Should().Be(4.5m);
    }

    [Fact]
    public async Task Persists_the_reservation_with_a_snapshot_of_the_item_names()
    {
        var part = _bed.AddPart(10, name: "Filtro de óleo");

        await _bed.Reserve.HandleAsync(Command(PartLine(part.Id.Value, 2)), default);

        var reservation = _bed.Reservations.Items.Should().ContainSingle().Subject;
        reservation.ServiceOrderId.Should().Be(_serviceOrderId);
        reservation.Status.Should().Be(ReservationStatus.Reserved);
        reservation.Lines.Should().ContainSingle()
            .Which.Should().Match<StockReservationLine>(l => l.ItemName == "Filtro de óleo" && l.Quantity == 2m);
    }

    [Fact]
    public async Task Publishes_StockReserved_with_the_reserved_lines_and_commits_once()
    {
        var part = _bed.AddPart(10);

        await _bed.Reserve.HandleAsync(Command(PartLine(part.Id.Value, 2)), default);

        var message = _bed.Publisher.Of<StockReserved>().Should().ContainSingle().Subject;
        message.CorrelationId.Should().Be(_serviceOrderId);
        message.OccurredOn.Should().Be(StockTestBed.Now);
        message.Lines.Should().ContainSingle()
            .Which.Should().Be(new StockItemLine(InventoryItemType.Part, part.Id.Value, 2m));
        _bed.UnitOfWork.SaveCount.Should().Be(1);
    }

    // ---- Tudo ou nada ----------------------------------------------------------

    [Fact]
    public async Task Reserves_nothing_when_one_item_lacks_stock()
    {
        var plenty = _bed.AddPart(10, "PN-A");
        var scarce = _bed.AddPart(2, "PN-B", "Pastilha");

        var result = await _bed.Reserve.HandleAsync(
            Command(PartLine(plenty.Id.Value, 5), PartLine(scarce.Id.Value, 3)), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ReserveStockStatus.Insufficient);
        plenty.Quantity.Should().Be(10);          // o item com saldo também ficou intacto
        plenty.ReservedQuantity.Should().Be(0);
        scarce.Quantity.Should().Be(2);
        _bed.Reservations.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Publishes_StockInsufficient_listing_only_the_missing_items()
    {
        var plenty = _bed.AddPart(10, "PN-A");
        var scarce = _bed.AddPart(2, "PN-B", "Pastilha");

        await _bed.Reserve.HandleAsync(Command(PartLine(plenty.Id.Value, 5), PartLine(scarce.Id.Value, 3)), default);

        var message = _bed.Publisher.Of<StockInsufficient>().Should().ContainSingle().Subject;
        message.CorrelationId.Should().Be(_serviceOrderId);
        message.Missing.Should().ContainSingle()
            .Which.Should().Be(new MissingStockItem(InventoryItemType.Part, scarce.Id.Value, "Pastilha", 3m, 2m));
        _bed.Publisher.Of<StockReserved>().Should().BeEmpty();
        _bed.UnitOfWork.SaveCount.Should().Be(1); // só descarrega o Outbox
    }

    [Fact]
    public async Task Lists_every_missing_item_not_just_the_first()
    {
        var a = _bed.AddPart(1, "PN-A");
        var oil = _bed.AddConsumable(1m);

        var result = await _bed.Reserve.HandleAsync(
            Command(PartLine(a.Id.Value, 5), ConsumableLine(oil.Id.Value, 3m)), default);

        result.Value.Missing.Should().HaveCount(2);
    }

    [Fact]
    public async Task Unknown_item_counts_as_missing_with_zero_available()
    {
        var unknown = Guid.NewGuid();

        var result = await _bed.Reserve.HandleAsync(Command(PartLine(unknown, 1)), default);

        result.Value.Status.Should().Be(ReserveStockStatus.Insufficient);
        result.Value.Missing.Should().ContainSingle()
            .Which.Should().Be(new MissingStockItem(InventoryItemType.Part, unknown, "Item não encontrado", 1m, 0m));
    }

    [Fact]
    public async Task Reserving_exactly_the_available_quantity_succeeds()
    {
        var part = _bed.AddPart(3);

        var result = await _bed.Reserve.HandleAsync(Command(PartLine(part.Id.Value, 3)), default);

        result.Value.Status.Should().Be(ReserveStockStatus.Reserved);
        part.Quantity.Should().Be(0);
    }

    // ---- Idempotência ----------------------------------------------------------

    [Fact]
    public async Task Repeated_ReserveStock_does_not_reserve_twice_and_answers_again()
    {
        var part = _bed.AddPart(10);
        var command = Command(PartLine(part.Id.Value, 4));

        await _bed.Reserve.HandleAsync(command, default);
        var second = await _bed.Reserve.HandleAsync(command, default);

        second.Value.Status.Should().Be(ReserveStockStatus.AlreadyReserved);
        part.Quantity.Should().Be(6);              // reservou 4 uma vez só
        part.ReservedQuantity.Should().Be(4);
        _bed.Reservations.Items.Should().ContainSingle();
        _bed.Publisher.Of<StockReserved>().Should().HaveCount(2); // a resposta é reenviada
    }

    [Fact]
    public async Task Repeated_ReserveStock_after_consume_still_does_not_touch_the_balance()
    {
        var part = _bed.AddPart(10);
        var command = Command(PartLine(part.Id.Value, 4));
        await _bed.Reserve.HandleAsync(command, default);
        await _bed.Consume.HandleAsync(new GearFlow.Inventory.Application.Reservations.Commands.ConsumeStock.ConsumeStockCommand(_serviceOrderId), default);

        var late = await _bed.Reserve.HandleAsync(command, default);

        late.Value.Status.Should().Be(ReserveStockStatus.AlreadyReserved);
        part.Quantity.Should().Be(6);
        part.ReservedQuantity.Should().Be(0);
    }

    // ---- Mensagem fora de ordem ------------------------------------------------

    [Fact]
    public async Task ReserveStock_after_the_reservation_was_released_is_ignored()
    {
        var part = _bed.AddPart(10);
        _bed.Reservations.Items.Add(StockReservation.CreateReleased(_serviceOrderId, StockTestBed.Now));

        var result = await _bed.Reserve.HandleAsync(Command(PartLine(part.Id.Value, 4)), default);

        result.Value.Status.Should().Be(ReserveStockStatus.IgnoredAfterRelease);
        part.Quantity.Should().Be(10);
        part.ReservedQuantity.Should().Be(0);
        _bed.Publisher.Messages.Should().BeEmpty();
        _bed.UnitOfWork.SaveCount.Should().Be(0);
    }

    // ---- Quantidades inválidas -------------------------------------------------

    [Fact]
    public async Task Fractional_part_quantity_is_rejected_without_touching_the_balance()
    {
        var part = _bed.AddPart(10);

        var result = await _bed.Reserve.HandleAsync(Command(PartLine(part.Id.Value, 1.5m)), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Reservation.PartQuantityMustBeInteger");
        part.Quantity.Should().Be(10);
        _bed.UnitOfWork.SaveCount.Should().Be(0);
        _bed.Publisher.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Fractional_consumable_quantity_is_accepted()
    {
        var oil = _bed.AddConsumable(10m);

        var result = await _bed.Reserve.HandleAsync(Command(ConsumableLine(oil.Id.Value, 2.75m)), default);

        result.Value.Status.Should().Be(ReserveStockStatus.Reserved);
        oil.ReservedQuantity.Should().Be(2.75m);
    }

    [Fact]
    public async Task Duplicated_item_is_rejected_before_touching_the_balance()
    {
        var part = _bed.AddPart(10);

        var result = await _bed.Reserve.HandleAsync(
            Command(PartLine(part.Id.Value, 2), PartLine(part.Id.Value, 3)), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Reservation.DuplicateItem");
        part.Quantity.Should().Be(10);
        part.ReservedQuantity.Should().Be(0);
        _bed.UnitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Different_service_orders_reserve_independently()
    {
        var part = _bed.AddPart(10);

        await _bed.Reserve.HandleAsync(new ReserveStockCommand(Guid.NewGuid(), [PartLine(part.Id.Value, 4)]), default);
        await _bed.Reserve.HandleAsync(new ReserveStockCommand(Guid.NewGuid(), [PartLine(part.Id.Value, 5)]), default);

        part.Quantity.Should().Be(1);
        part.ReservedQuantity.Should().Be(9);
        _bed.Reservations.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Second_order_competing_for_the_last_units_gets_StockInsufficient()
    {
        var part = _bed.AddPart(5);
        await _bed.Reserve.HandleAsync(new ReserveStockCommand(Guid.NewGuid(), [PartLine(part.Id.Value, 4)]), default);

        var result = await _bed.Reserve.HandleAsync(
            new ReserveStockCommand(Guid.NewGuid(), [PartLine(part.Id.Value, 3)]), default);

        result.Value.Status.Should().Be(ReserveStockStatus.Insufficient);
        result.Value.Missing.Single().Available.Should().Be(1m);
        part.Quantity.Should().Be(1);
    }
}
