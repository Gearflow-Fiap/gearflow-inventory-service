using FluentAssertions;
using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Domain.Reservations;
using GearFlow.Inventory.Domain.Stock;

namespace GearFlow.Inventory.UnitTests.Domain;

public sealed class StockReservationTests
{
    private static readonly DateTime Now = new(2026, 11, 3, 13, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = Now.AddMinutes(5);

    private static StockReservationLine APartLine(decimal quantity = 2, Guid? id = null) =>
        StockReservationLine.Create(StockItemType.Part, id ?? Guid.NewGuid(), "Filtro", quantity).Value;

    private static StockReservation AReservation(Guid? serviceOrderId = null) =>
        StockReservation.Create(serviceOrderId ?? Guid.NewGuid(), [APartLine()], Now).Value;

    // ---- Linha ---------------------------------------------------------------

    [Fact]
    public void Line_accepts_whole_part_quantity_and_fractional_consumable_quantity()
    {
        StockReservationLine.Create(StockItemType.Part, Guid.NewGuid(), "Filtro", 3m).IsSuccess.Should().BeTrue();
        StockReservationLine.Create(StockItemType.Consumable, Guid.NewGuid(), "Óleo", 2.5m).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Line_rejects_fractional_part_quantity()
    {
        var result = StockReservationLine.Create(StockItemType.Part, Guid.NewGuid(), "Filtro", 1.5m);

        result.Error.Code.Should().Be("Reservation.PartQuantityMustBeInteger");
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Line_rejects_non_positive_quantity(double quantity)
    {
        StockReservationLine.Create(StockItemType.Consumable, Guid.NewGuid(), "Óleo", (decimal)quantity)
            .Error.Code.Should().Be("Reservation.QuantityInvalid");
    }

    [Fact]
    public void Line_rejects_empty_item_id_and_unknown_type()
    {
        StockReservationLine.Create(StockItemType.Part, Guid.Empty, "Filtro", 1m)
            .Error.Code.Should().Be("Reservation.ItemIdRequired");
        StockReservationLine.Create((StockItemType)99, Guid.NewGuid(), "Filtro", 1m)
            .Error.Code.Should().Be("Reservation.ItemTypeInvalid");
    }

    [Fact]
    public void Line_exposes_whole_quantity_for_parts()
    {
        APartLine(4m).WholeQuantity.Should().Be(4);
    }

    // ---- Criação -------------------------------------------------------------

    [Fact]
    public void Create_starts_reserved_with_the_lines()
    {
        var serviceOrderId = Guid.NewGuid();

        var reservation = StockReservation.Create(serviceOrderId, [APartLine(), APartLine(1)], Now).Value;

        reservation.ServiceOrderId.Should().Be(serviceOrderId);
        reservation.Status.Should().Be(ReservationStatus.Reserved);
        reservation.Lines.Should().HaveCount(2);
        reservation.CreatedOn.Should().Be(Now);
        reservation.UpdatedOn.Should().Be(Now);
    }

    [Fact]
    public void Create_rejects_empty_service_order_and_empty_lines()
    {
        StockReservation.Create(Guid.Empty, [APartLine()], Now)
            .Error.Code.Should().Be("Reservation.ServiceOrderRequired");
        StockReservation.Create(Guid.NewGuid(), [], Now)
            .Error.Code.Should().Be("Reservation.ItemsRequired");
    }

    [Fact]
    public void Create_rejects_the_same_item_twice()
    {
        var itemId = Guid.NewGuid();

        var result = StockReservation.Create(Guid.NewGuid(), [APartLine(1, itemId), APartLine(2, itemId)], Now);

        result.Error.Code.Should().Be("Reservation.DuplicateItem");
    }

    [Fact]
    public void Create_allows_same_id_for_a_part_and_a_consumable()
    {
        var itemId = Guid.NewGuid();
        var part = StockReservationLine.Create(StockItemType.Part, itemId, "Filtro", 1m).Value;
        var consumable = StockReservationLine.Create(StockItemType.Consumable, itemId, "Óleo", 1m).Value;

        StockReservation.Create(Guid.NewGuid(), [part, consumable], Now).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void CreateReleased_is_a_tombstone_without_lines()
    {
        var tombstone = StockReservation.CreateReleased(Guid.NewGuid(), Now);

        tombstone.Status.Should().Be(ReservationStatus.Released);
        tombstone.Lines.Should().BeEmpty();
    }

    // ---- Release -------------------------------------------------------------

    [Fact]
    public void Release_moves_reserved_to_released()
    {
        var reservation = AReservation();

        reservation.Release(Later).IsSuccess.Should().BeTrue();

        reservation.Status.Should().Be(ReservationStatus.Released);
        reservation.UpdatedOn.Should().Be(Later);
    }

    [Fact]
    public void Release_is_idempotent()
    {
        var reservation = AReservation();
        reservation.Release(Now);

        var again = reservation.Release(Later);

        again.IsSuccess.Should().BeTrue();
        reservation.Status.Should().Be(ReservationStatus.Released);
        reservation.UpdatedOn.Should().Be(Now); // a repetição não mexe em nada
    }

    [Fact]
    public void Release_after_consume_is_a_conflict()
    {
        var reservation = AReservation();
        reservation.Consume(Now);

        var result = reservation.Release(Later);

        result.Error.Code.Should().Be("Reservation.AlreadyConsumed");
        result.Error.Type.Should().Be(ErrorType.Conflict);
        reservation.Status.Should().Be(ReservationStatus.Consumed);
    }

    // ---- Consume -------------------------------------------------------------

    [Fact]
    public void Consume_moves_reserved_to_consumed()
    {
        var reservation = AReservation();

        reservation.Consume(Later).IsSuccess.Should().BeTrue();

        reservation.Status.Should().Be(ReservationStatus.Consumed);
        reservation.UpdatedOn.Should().Be(Later);
    }

    [Fact]
    public void Consume_is_idempotent()
    {
        var reservation = AReservation();
        reservation.Consume(Now);

        reservation.Consume(Later).IsSuccess.Should().BeTrue();

        reservation.UpdatedOn.Should().Be(Now);
    }

    [Fact]
    public void Consume_after_release_is_a_conflict()
    {
        var reservation = AReservation();
        reservation.Release(Now);

        var result = reservation.Consume(Later);

        result.Error.Code.Should().Be("Reservation.AlreadyReleased");
        reservation.Status.Should().Be(ReservationStatus.Released);
    }
}
