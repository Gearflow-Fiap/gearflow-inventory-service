using GearFlow.Inventory.Domain.Stock;

namespace GearFlow.Inventory.Application.Parts;

public sealed record PartDto(
    Guid Id, string Name, string Description, string PartNumber, string Manufacturer,
    int PriceCents, int Quantity, int ReservedQuantity, bool BelowMinimum)
{
    public static PartDto From(Part p) =>
        new(p.Id.Value, p.Name, p.Description, p.PartNumber, p.Manufacturer,
            p.PriceCents, p.Quantity, p.ReservedQuantity, p.IsStockBelowMinimum());
}
