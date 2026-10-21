using FluentAssertions;
using GearFlow.Contracts;
using GearFlow.Inventory.Application.Reservations.Commands.ConsumeStock;
using GearFlow.Inventory.Application.Reservations.Commands.ReleaseStock;
using GearFlow.Inventory.Application.Reservations.Commands.ReserveStock;
using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Domain.Reservations;
using GearFlow.Inventory.Domain.Stock;

namespace GearFlow.Inventory.UnitTests.Domain;

public sealed class DomainCoreTests
{
    // ---- Ids (value objects) -------------------------------------------------

    [Fact]
    public void Ids_are_equal_by_value()
    {
        var guid = Guid.NewGuid();

        PartId.From(guid).Should().Be(PartId.From(guid));
        (PartId.From(guid) == PartId.From(guid)).Should().BeTrue();
        (PartId.From(guid) != PartId.New()).Should().BeTrue();
        PartId.From(guid).GetHashCode().Should().Be(PartId.From(guid).GetHashCode());
        PartId.From(guid).ToString().Should().Be(guid.ToString());

        ConsumableId.From(guid).Should().Be(ConsumableId.From(guid));
        ConsumableId.From(guid).ToString().Should().Be(guid.ToString());
        StockReservationId.From(guid).Should().Be(StockReservationId.From(guid));
        StockReservationId.From(guid).ToString().Should().Be(guid.ToString());
    }

    [Fact]
    public void New_ids_are_distinct()
    {
        PartId.New().Should().NotBe(PartId.New());
        ConsumableId.New().Should().NotBe(ConsumableId.New());
        StockReservationId.New().Should().NotBe(StockReservationId.New());
    }

    [Fact]
    public void Ids_do_not_equal_null_or_other_types()
    {
        var id = PartId.New();

        PartId? none = null;

        id.Equals((object)"x").Should().BeFalse();
        (id == none).Should().BeFalse();
        id.Equals(none).Should().BeFalse();
    }

    // ---- Entidades -----------------------------------------------------------

    [Fact]
    public void Entities_are_equal_by_id()
    {
        var part = Part.Create("A", "d", "PN", "M", 1, 1).Value;

        Part? none = null;

        part.Equals(part).Should().BeTrue();
        part.Equals((object)"x").Should().BeFalse();
        part.Equals(none).Should().BeFalse();
        (part == Part.Create("A", "d", "PN", "M", 1, 1).Value).Should().BeFalse();
        part.GetHashCode().Should().Be(part.Id.GetHashCode());
    }

    // ---- Error ---------------------------------------------------------------

    [Fact]
    public void Error_factories_set_the_matching_type()
    {
        Error.Failure("C", "d").Type.Should().Be(ErrorType.Failure);
        Error.NotFound("C", "d").Type.Should().Be(ErrorType.NotFound);
        Error.Conflict("C", "d").Type.Should().Be(ErrorType.Conflict);
        Error.Forbidden("C", "d").Type.Should().Be(ErrorType.Forbidden);
        Error.Unauthorized("C", "d").Type.Should().Be(ErrorType.Unauthorized);
        Error.Validation("C", "d", "field").Should().Match<Error>(e => e.Type == ErrorType.Validation && e.Field == "field");
    }

    [Fact]
    public void Result_invariants_are_enforced()
    {
        Result.Success().IsSuccess.Should().BeTrue();
        Result.Success().Error.Should().Be(Error.None);

        var failure = Result.Failure(Error.NotFound("C", "d"));
        failure.IsFailure.Should().BeTrue();

        var value = () => Result.Failure<int>(Error.NotFound("C", "d")).Value;
        value.Should().Throw<InvalidOperationException>();
    }

    // ---- Mapeamento mensagem → command (consumers da fase 5) -----------------

    [Fact]
    public void Commands_are_built_from_the_saga_messages_using_the_correlation_id_as_service_order()
    {
        var serviceOrderId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var items = new List<StockItemLine> { new(InventoryItemType.Part, Guid.NewGuid(), 2) };

        var reserve = ReserveStockCommand.From(new GearFlow.Contracts.ReserveStock(serviceOrderId, Guid.NewGuid(), now, items));
        var release = ReleaseStockCommand.From(new GearFlow.Contracts.ReleaseStock(serviceOrderId, Guid.NewGuid(), now, "pagamento recusado"));
        var consume = ConsumeStockCommand.From(new GearFlow.Contracts.ConsumeStock(serviceOrderId, Guid.NewGuid(), now));

        reserve.ServiceOrderId.Should().Be(serviceOrderId);
        reserve.Items.Should().BeEquivalentTo(items);
        release.ServiceOrderId.Should().Be(serviceOrderId);
        release.Reason.Should().Be("pagamento recusado");
        consume.ServiceOrderId.Should().Be(serviceOrderId);
    }
}
