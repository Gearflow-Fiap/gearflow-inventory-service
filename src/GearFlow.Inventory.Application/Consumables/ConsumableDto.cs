using GearFlow.Inventory.Domain.Stock;

namespace GearFlow.Inventory.Application.Consumables;

public sealed record ConsumableDto(
    Guid Id, string Name, int UnitPriceCents, decimal Quantity, decimal ReservedQuantity, bool BelowMinimum)
{
    public static ConsumableDto From(Consumable c) =>
        new(c.Id.Value, c.Name, c.UnitPriceCents, c.Quantity, c.ReservedQuantity, c.IsStockBelowMinimum());
}
