using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Domain.Stock;

public sealed class PartId : ValueObject
{
    public Guid Value { get; }
    private PartId(Guid value) => Value = value;
    public static PartId New() => new(Guid.NewGuid());
    public static PartId From(Guid value) => new(value);
    protected override IEnumerable<object> GetEqualityComponents() { yield return Value; }
    public override string ToString() => Value.ToString();
}
