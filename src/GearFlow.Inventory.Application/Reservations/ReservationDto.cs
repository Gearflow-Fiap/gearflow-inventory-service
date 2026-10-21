using GearFlow.Inventory.Domain.Reservations;

namespace GearFlow.Inventory.Application.Reservations;

public sealed record ReservationLineDto(string ItemType, Guid ItemId, string ItemName, decimal Quantity);

/// <summary>Enums saem como string no JSON, igual aos contratos de mensagem.</summary>
public sealed record ReservationDto(
    Guid Id, Guid ServiceOrderId, string Status, IReadOnlyList<ReservationLineDto> Lines,
    DateTime CreatedOn, DateTime UpdatedOn)
{
    public static ReservationDto From(StockReservation r) =>
        new(r.Id.Value, r.ServiceOrderId, r.Status.ToString(),
            r.Lines.Select(l => new ReservationLineDto(l.ItemType.ToString(), l.ItemId, l.ItemName, l.Quantity)).ToList(),
            r.CreatedOn, r.UpdatedOn);
}
