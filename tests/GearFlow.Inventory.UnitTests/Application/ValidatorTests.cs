using FluentAssertions;
using FluentValidation.TestHelper;
using GearFlow.Contracts;
using GearFlow.Inventory.Application.Reservations.Commands.ConsumeStock;
using GearFlow.Inventory.Application.Reservations.Commands.ReleaseStock;
using GearFlow.Inventory.Application.Reservations.Commands.ReserveStock;

namespace GearFlow.Inventory.UnitTests.Application;

public sealed class ValidatorTests
{
    private readonly ReserveStockCommandValidator _reserve = new();

    private static ReserveStockCommand Valid() =>
        new(Guid.NewGuid(), [new StockItemLine(InventoryItemType.Part, Guid.NewGuid(), 2)]);

    [Fact]
    public void ReserveStock_valid_command_passes()
    {
        _reserve.TestValidate(Valid()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void ReserveStock_requires_the_service_order()
    {
        _reserve.TestValidate(Valid() with { ServiceOrderId = Guid.Empty })
            .ShouldHaveValidationErrorFor(x => x.ServiceOrderId);
    }

    [Fact]
    public void ReserveStock_requires_at_least_one_item()
    {
        _reserve.TestValidate(Valid() with { Items = [] }).ShouldHaveValidationErrorFor(x => x.Items);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ReserveStock_rejects_non_positive_quantity(double quantity)
    {
        var command = new ReserveStockCommand(
            Guid.NewGuid(), [new StockItemLine(InventoryItemType.Consumable, Guid.NewGuid(), (decimal)quantity)]);

        _reserve.TestValidate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void ReserveStock_rejects_fractional_part_quantity()
    {
        var command = new ReserveStockCommand(
            Guid.NewGuid(), [new StockItemLine(InventoryItemType.Part, Guid.NewGuid(), 1.5m)]);

        var result = _reserve.TestValidate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("inteiro"));
    }

    [Fact]
    public void ReserveStock_accepts_fractional_consumable_quantity()
    {
        var command = new ReserveStockCommand(
            Guid.NewGuid(), [new StockItemLine(InventoryItemType.Consumable, Guid.NewGuid(), 1.5m)]);

        _reserve.TestValidate(command).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void ReserveStock_rejects_empty_item_id_and_unknown_type()
    {
        var emptyId = new ReserveStockCommand(Guid.NewGuid(), [new StockItemLine(InventoryItemType.Part, Guid.Empty, 1)]);
        var badType = new ReserveStockCommand(Guid.NewGuid(), [new StockItemLine((InventoryItemType)99, Guid.NewGuid(), 1)]);

        _reserve.TestValidate(emptyId).IsValid.Should().BeFalse();
        _reserve.TestValidate(badType).IsValid.Should().BeFalse();
    }

    [Fact]
    public void ReserveStock_rejects_repeated_items()
    {
        var id = Guid.NewGuid();
        var command = new ReserveStockCommand(Guid.NewGuid(),
        [
            new StockItemLine(InventoryItemType.Part, id, 1),
            new StockItemLine(InventoryItemType.Part, id, 2),
        ]);

        var result = _reserve.TestValidate(command);

        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("repetidos"));
    }

    [Fact]
    public void ReserveStock_allows_the_same_id_as_part_and_consumable()
    {
        var id = Guid.NewGuid();
        var command = new ReserveStockCommand(Guid.NewGuid(),
        [
            new StockItemLine(InventoryItemType.Part, id, 1),
            new StockItemLine(InventoryItemType.Consumable, id, 2),
        ]);

        _reserve.TestValidate(command).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void ReleaseStock_and_ConsumeStock_require_the_service_order()
    {
        new ReleaseStockCommandValidator().TestValidate(new ReleaseStockCommand(Guid.Empty, "x"))
            .ShouldHaveValidationErrorFor(x => x.ServiceOrderId);
        new ConsumeStockCommandValidator().TestValidate(new ConsumeStockCommand(Guid.Empty))
            .ShouldHaveValidationErrorFor(x => x.ServiceOrderId);

        new ReleaseStockCommandValidator().TestValidate(new ReleaseStockCommand(Guid.NewGuid(), "x"))
            .ShouldNotHaveAnyValidationErrors();
        new ConsumeStockCommandValidator().TestValidate(new ConsumeStockCommand(Guid.NewGuid()))
            .ShouldNotHaveAnyValidationErrors();
    }
}
