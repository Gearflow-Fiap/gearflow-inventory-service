using FluentAssertions;
using GearFlow.Inventory.Infrastructure.Seed;

namespace GearFlow.Inventory.UnitTests.Infrastructure;

public sealed class SeedTests
{
    private static readonly Guid[] AllIds = typeof(DemoCatalogIds)
        .GetFields()
        .Where(f => f.FieldType == typeof(Guid))
        .Select(f => (Guid)f.GetValue(null)!)
        .ToArray();

    [Fact]
    public void Demo_catalog_ids_are_fixed_unique_and_non_empty()
    {
        AllIds.Should().HaveCount(17);                 // 10 peças + 7 insumos
        AllIds.Should().OnlyHaveUniqueItems();
        AllIds.Should().NotContain(Guid.Empty);
    }

    [Fact]
    public void Parts_and_consumables_use_separate_id_ranges()
    {
        AllIds.Count(id => id.ToString().StartsWith("a1", StringComparison.Ordinal)).Should().Be(10);
        AllIds.Count(id => id.ToString().StartsWith("b2", StringComparison.Ordinal)).Should().Be(7);
    }

    [Fact]
    public void The_two_items_used_to_force_a_shortage_are_exposed_by_name()
    {
        DemoCatalogIds.JogoDePalhetas.Should().NotBe(Guid.Empty);
        DemoCatalogIds.AguaDesmineralizada.Should().NotBe(Guid.Empty);
    }
}
