using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Domain.Reservations;

public sealed class StockReservationId : ValueObject
{
    public Guid Value { get; }
    private StockReservationId(Guid value) => Value = value;
    public static StockReservationId New() => new(Guid.NewGuid());
    public static StockReservationId From(Guid value) => new(value);
    protected override IEnumerable<object> GetEqualityComponents() { yield return Value; }
    public override string ToString() => Value.ToString();
}
