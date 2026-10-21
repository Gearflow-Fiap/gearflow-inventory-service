using FluentAssertions;
using GearFlow.Inventory.Domain.Reservations;
using GearFlow.Inventory.Domain.Stock;
using GearFlow.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GearFlow.Inventory.UnitTests.Infrastructure;

/// <summary>
/// Verifica o mapeamento do EF/Npgsql SEM banco: o modelo é construído e as queries são traduzidas para SQL
/// (<c>ToQueryString</c> não abre conexão). O comportamento contra um Postgres real fica nos testes de integração.
/// </summary>
public sealed class EfModelTests
{
    private static InventoryDbContext NewContext() =>
        new(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=u;Password=p")
            .Options);

    [Theory]
    [InlineData(typeof(Part))]
    [InlineData(typeof(Consumable))]
    [InlineData(typeof(StockReservation))]
    public void Stock_entities_use_xmin_as_the_concurrency_token(Type entityType)
    {
        using var db = NewContext();

        var xmin = db.Model.FindEntityType(entityType)!.FindProperty("xmin");

        xmin.Should().NotBeNull();
        xmin!.IsConcurrencyToken.Should().BeTrue();
        xmin.GetColumnName().Should().Be("xmin");
    }

    [Fact]
    public void Reservation_is_unique_per_service_order()
    {
        using var db = NewContext();
        var reservation = db.Model.FindEntityType(typeof(StockReservation))!;

        var index = reservation.GetIndexes().Single(i => i.Properties.Any(p => p.Name == nameof(StockReservation.ServiceOrderId)));

        index.IsUnique.Should().BeTrue();
        index.Properties.Should().ContainSingle();
    }

    [Fact]
    public void Reservation_lines_live_in_their_own_table_with_a_composite_key()
    {
        using var db = NewContext();

        var lines = db.Model.FindEntityType(typeof(StockReservationLine))!;

        lines.GetTableName().Should().Be("stock_reservation_lines");
        lines.GetSchema().Should().Be("inventory");
        lines.FindPrimaryKey()!.Properties.Select(p => p.Name)
            .Should().BeEquivalentTo("reservation_id", nameof(StockReservationLine.ItemType), nameof(StockReservationLine.ItemId));
    }

    [Fact]
    public void Status_and_item_type_are_persisted_as_text()
    {
        using var db = NewContext();

        var status = db.Model.FindEntityType(typeof(StockReservation))!.FindProperty(nameof(StockReservation.Status))!;
        var itemType = db.Model.FindEntityType(typeof(StockReservationLine))!.FindProperty(nameof(StockReservationLine.ItemType))!;

        status.GetProviderClrType().Should().Be(typeof(string));
        itemType.GetProviderClrType().Should().Be(typeof(string));
    }

    [Fact]
    public void Tables_use_snake_case_names_in_the_inventory_schema()
    {
        using var db = NewContext();

        db.Model.FindEntityType(typeof(Part))!.GetTableName().Should().Be("parts");
        db.Model.FindEntityType(typeof(Consumable))!.GetTableName().Should().Be("consumables");
        db.Model.FindEntityType(typeof(StockReservation))!.GetTableName().Should().Be("stock_reservations");
        db.Model.FindEntityType(typeof(Part))!.GetSchema().Should().Be("inventory");
    }

    [Fact]
    public void Quantity_columns_keep_three_decimal_places_for_fractional_items()
    {
        using var db = NewContext();

        var consumable = db.Model.FindEntityType(typeof(Consumable))!;

        consumable.FindProperty(nameof(Consumable.Quantity))!.GetPrecision().Should().Be(18);
        consumable.FindProperty(nameof(Consumable.Quantity))!.GetScale().Should().Be(3);
        consumable.FindProperty(nameof(Consumable.ReservedQuantity))!.GetScale().Should().Be(3);
    }

    // As duas expressões abaixo são as mesmas dos repositórios.

    [Fact]
    public void Lookup_of_several_parts_by_id_translates_to_a_single_membership_query()
    {
        using var db = NewContext();
        var wanted = new List<PartId> { PartId.From(Guid.NewGuid()), PartId.From(Guid.NewGuid()) };

        var sql = db.Parts.Where(p => wanted.Contains(p.Id)).ToQueryString();

        sql.Should().Contain("FROM inventory.parts");
        sql.Should().MatchRegex("(?i)(= ANY|IN \\()");
    }

    [Fact]
    public void Lookup_of_several_consumables_by_id_translates_to_a_single_membership_query()
    {
        using var db = NewContext();
        var wanted = new List<ConsumableId> { ConsumableId.From(Guid.NewGuid()) };

        var sql = db.Consumables.Where(c => wanted.Contains(c.Id)).ToQueryString();

        sql.Should().Contain("FROM inventory.consumables");
    }

    [Fact]
    public void Reservation_lookup_by_service_order_translates_to_an_equality_filter()
    {
        using var db = NewContext();
        var serviceOrderId = Guid.NewGuid();

        var sql = db.StockReservations.Where(r => r.ServiceOrderId == serviceOrderId).ToQueryString();

        sql.Should().Contain("service_order_id");
        sql.Should().Contain("stock_reservation_lines"); // as linhas (owned) vêm no mesmo carregamento
    }
}
