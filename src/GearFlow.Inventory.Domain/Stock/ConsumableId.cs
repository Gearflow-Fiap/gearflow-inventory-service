using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Domain.Stock;

public sealed class ConsumableId : ValueObject
{
    public Guid Value { get; }
    private ConsumableId(Guid value) => Value = value;
    public static ConsumableId New() => new(Guid.NewGuid());
    public static ConsumableId From(Guid value) => new(value);
    protected override IEnumerable<object> GetEqualityComponents() { yield return Value; }
    public override string ToString() => Value.ToString();
}
