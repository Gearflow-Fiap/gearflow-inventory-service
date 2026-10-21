using FluentAssertions;
using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Domain.Stock;

namespace GearFlow.Inventory.UnitTests.Domain;

public sealed class PartStockTests
{
    private static Part APart(int quantity) =>
        Part.Create("Filtro", "óleo", "PN-1", "Bosch", 5000, quantity).Value;

    // ---- Criação -------------------------------------------------------------

    [Fact]
    public void Create_trims_and_starts_without_reservation()
    {
        var part = Part.Create("  Filtro  ", " óleo ", "PN-1", "Bosch", 5000, 10).Value;

        part.Name.Should().Be("Filtro");
        part.Description.Should().Be("óleo");
        part.Quantity.Should().Be(10);
        part.ReservedQuantity.Should().Be(0);
    }

    [Fact]
    public void Create_rejects_blank_name_negative_price_and_negative_quantity()
    {
        Part.Create(" ", "d", "PN", "M", 100, 1).Error.Code.Should().Be("Part.NameRequired");
        Part.Create("x", "d", "PN", "M", -1, 1).Error.Code.Should().Be("Part.PriceNegative");
        Part.Create("x", "d", "PN", "M", 100, -1).Error.Code.Should().Be("Part.QuantityNegative");
    }

    [Fact]
    public void Create_with_known_id_keeps_the_id()
    {
        var id = PartId.From(Guid.NewGuid());

        var part = Part.Create(id, "Filtro", "d", "PN", "M", 100, 1).Value;

        part.Id.Should().Be(id);
    }

    // ---- Reserve / Release / Consume -----------------------------------------

    [Fact]
    public void Reserve_moves_from_available_to_reserved()
    {
        var part = APart(10);

        var result = part.Reserve(4);

        result.IsSuccess.Should().BeTrue();
        part.Quantity.Should().Be(6);
        part.ReservedQuantity.Should().Be(4);
    }

    [Fact]
    public void Reserve_more_than_available_fails_with_conflict()
    {
        var part = APart(3);

        var result = part.Reserve(4);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Part.InsufficientStock");
        result.Error.Type.Should().Be(ErrorType.Conflict);
        part.Quantity.Should().Be(3);
        part.ReservedQuantity.Should().Be(0);
    }

    [Fact]
    public void Reserve_non_positive_quantity_is_invalid()
    {
        APart(10).Reserve(0).Error.Code.Should().Be("Part.ReserveQuantityInvalid");
    }

    [Fact]
    public void Release_moves_from_reserved_back_to_available()
    {
        var part = APart(10);
        part.Reserve(4);

        var result = part.Release(3);

        result.IsSuccess.Should().BeTrue();
        part.Quantity.Should().Be(9);
        part.ReservedQuantity.Should().Be(1);
    }

    [Fact]
    public void Release_reverts_a_full_reservation_to_the_original_balance()
    {
        var part = APart(10);
        part.Reserve(7);

        part.Release(7).IsSuccess.Should().BeTrue();

        part.Quantity.Should().Be(10);
        part.ReservedQuantity.Should().Be(0);
    }

    [Fact]
    public void Release_more_than_reserved_fails_and_changes_nothing()
    {
        var part = APart(10);
        part.Reserve(2);

        var result = part.Release(3);

        result.Error.Code.Should().Be("Part.InsufficientReserved");
        part.Quantity.Should().Be(8);
        part.ReservedQuantity.Should().Be(2);
    }

    [Fact]
    public void Release_non_positive_quantity_is_invalid()
    {
        APart(10).Release(0).Error.Code.Should().Be("Part.ReleaseQuantityInvalid");
    }

    [Fact]
    public void Consume_reduces_only_the_reservation()
    {
        var part = APart(10);
        part.Reserve(4);

        var result = part.Consume(4);

        result.IsSuccess.Should().BeTrue();
        part.Quantity.Should().Be(6);         // consumo não mexe no disponível: já saiu na reserva
        part.ReservedQuantity.Should().Be(0);
    }

    [Fact]
    public void Consume_more_than_reserved_fails()
    {
        var part = APart(10);
        part.Reserve(2);

        var result = part.Consume(3);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Part.InsufficientReserved");
    }

    [Fact]
    public void Consume_non_positive_quantity_is_invalid()
    {
        APart(10).Consume(0).Error.Code.Should().Be("Part.ConsumeQuantityInvalid");
    }

    // ---- Mínimo, reposição, remoção ------------------------------------------

    [Fact]
    public void IsStockBelowMinimum_true_at_or_below_five()
    {
        APart(5).IsStockBelowMinimum().Should().BeTrue();
        APart(6).IsStockBelowMinimum().Should().BeFalse();
    }

    [Fact]
    public void HasAvailability_compares_against_available_quantity()
    {
        var part = APart(3);

        part.HasAvailability(3).Should().BeTrue();
        part.HasAvailability(4).Should().BeFalse();
    }

    [Fact]
    public void AddQuantity_replenishes_available()
    {
        var part = APart(2);

        part.AddQuantity(8).IsSuccess.Should().BeTrue();

        part.Quantity.Should().Be(10);
        part.AddQuantity(0).Error.Code.Should().Be("Part.AddQuantityInvalid");
    }

    [Fact]
    public void EnsureCanBeRemoved_fails_while_units_are_reserved()
    {
        var part = APart(10);
        part.EnsureCanBeRemoved().IsSuccess.Should().BeTrue();

        part.Reserve(1);

        var result = part.EnsureCanBeRemoved();
        result.Error.Code.Should().Be("Part.HasActiveReservations");
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public void UpdateDetails_overwrites_the_fields()
    {
        var part = APart(10);

        part.UpdateDetails("Correia", "dentada", "PN-2", "Gates", 7000, 20);

        part.Name.Should().Be("Correia");
        part.PartNumber.Should().Be("PN-2");
        part.PriceCents.Should().Be(7000);
        part.Quantity.Should().Be(20);
        part.UpdatedOn.Should().NotBeNull();
    }
}
