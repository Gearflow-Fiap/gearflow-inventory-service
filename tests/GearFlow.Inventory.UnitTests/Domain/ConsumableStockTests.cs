using FluentAssertions;
using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Domain.Stock;

namespace GearFlow.Inventory.UnitTests.Domain;

/// <summary>Estoque bifásico do insumo: mesma regra da peça, mas com quantidade fracionária (litros).</summary>
public sealed class ConsumableStockTests
{
    private static Consumable AConsumable(decimal quantity) =>
        Consumable.Create("Óleo 5W30", 4500, quantity).Value;

    [Fact]
    public void Create_rejects_negative_price_and_quantity_and_blank_name()
    {
        Consumable.Create("Óleo", -1, 10m).IsFailure.Should().BeTrue();
        Consumable.Create("Óleo", 100, -1m).IsFailure.Should().BeTrue();
        Consumable.Create(" ", 100, 10m).Error.Code.Should().Be("Consumable.NameRequired");
    }

    [Fact]
    public void Create_with_known_id_keeps_the_id()
    {
        var id = ConsumableId.From(Guid.NewGuid());

        Consumable.Create(id, "Óleo", 100, 10m).Value.Id.Should().Be(id);
    }

    [Fact]
    public void Reserve_moves_fractional_quantity_from_available_to_reserved()
    {
        var consumable = AConsumable(10.5m);

        var result = consumable.Reserve(2.5m);

        result.IsSuccess.Should().BeTrue();
        consumable.Quantity.Should().Be(8.0m);
        consumable.ReservedQuantity.Should().Be(2.5m);
    }

    [Fact]
    public void Reserve_more_than_available_fails_with_conflict()
    {
        var consumable = AConsumable(1m);

        var result = consumable.Reserve(2m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Consumable.InsufficientStock");
        consumable.Quantity.Should().Be(1m);
    }

    [Fact]
    public void Reserve_non_positive_quantity_is_invalid()
    {
        AConsumable(10m).Reserve(0m).Error.Code.Should().Be("Consumable.ReserveQuantityInvalid");
    }

    [Fact]
    public void Release_moves_fractional_quantity_back_to_available()
    {
        var consumable = AConsumable(10m);
        consumable.Reserve(4.5m);

        var result = consumable.Release(4.5m);

        result.IsSuccess.Should().BeTrue();
        consumable.Quantity.Should().Be(10m);
        consumable.ReservedQuantity.Should().Be(0m);
    }

    [Fact]
    public void Release_more_than_reserved_fails_and_changes_nothing()
    {
        var consumable = AConsumable(10m);
        consumable.Reserve(2m);

        var result = consumable.Release(3m);

        result.Error.Code.Should().Be("Consumable.InsufficientReserved");
        consumable.Quantity.Should().Be(8m);
        consumable.ReservedQuantity.Should().Be(2m);
    }

    [Fact]
    public void Release_non_positive_quantity_is_invalid()
    {
        AConsumable(10m).Release(0m).Error.Code.Should().Be("Consumable.ReleaseQuantityInvalid");
    }

    [Fact]
    public void Consume_reduces_only_the_reservation()
    {
        var consumable = AConsumable(10m);
        consumable.Reserve(4m); // disponível 6, reservado 4

        var result = consumable.Consume(4m);

        result.IsSuccess.Should().BeTrue();
        consumable.Quantity.Should().Be(6m);           // disponível intacto
        consumable.ReservedQuantity.Should().Be(0m);   // baixou só a reserva
    }

    [Fact]
    public void Consume_more_than_reserved_fails()
    {
        var consumable = AConsumable(10m);
        consumable.Reserve(2m);

        var result = consumable.Consume(3m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Consumable.InsufficientReserved");
    }

    [Fact]
    public void Consume_non_positive_quantity_is_invalid()
    {
        // Guard que o monólito só tinha na peça.
        var consumable = AConsumable(10m);
        consumable.Reserve(2m);

        consumable.Consume(0m).Error.Code.Should().Be("Consumable.ConsumeQuantityInvalid");
        consumable.Consume(-1m).Error.Code.Should().Be("Consumable.ConsumeQuantityInvalid");
        consumable.ReservedQuantity.Should().Be(2m);
    }

    [Theory]
    [InlineData(5.0, true)]     // no mínimo
    [InlineData(4.9, true)]     // abaixo do mínimo
    [InlineData(5.1, false)]    // acima
    public void IsStockBelowMinimum_at_or_below_five(decimal quantity, bool expected)
    {
        AConsumable(quantity).IsStockBelowMinimum().Should().Be(expected);
    }

    [Fact]
    public void HasAvailability_compares_against_available_quantity()
    {
        var consumable = AConsumable(2.5m);

        consumable.HasAvailability(2.5m).Should().BeTrue();
        consumable.HasAvailability(2.6m).Should().BeFalse();
    }

    [Fact]
    public void AddQuantity_replenishes_available()
    {
        var consumable = AConsumable(2m);

        consumable.AddQuantity(8.5m).IsSuccess.Should().BeTrue();

        consumable.Quantity.Should().Be(10.5m);
        consumable.AddQuantity(0m).Error.Code.Should().Be("Consumable.AddQuantityInvalid");
    }

    [Fact]
    public void EnsureCanBeRemoved_fails_while_quantity_is_reserved()
    {
        var consumable = AConsumable(10m);
        consumable.EnsureCanBeRemoved().IsSuccess.Should().BeTrue();

        consumable.Reserve(0.5m);

        var result = consumable.EnsureCanBeRemoved();
        result.Error.Code.Should().Be("Consumable.HasActiveReservations");
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public void UpdateDetails_overwrites_the_fields()
    {
        var consumable = AConsumable(10m);

        consumable.UpdateDetails("Aditivo", 4200, 25m);

        consumable.Name.Should().Be("Aditivo");
        consumable.UnitPriceCents.Should().Be(4200);
        consumable.Quantity.Should().Be(25m);
    }
}
