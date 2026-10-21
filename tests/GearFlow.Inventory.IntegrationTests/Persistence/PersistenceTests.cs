using FluentAssertions;
using GearFlow.Contracts;
using GearFlow.Inventory.Application;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Application.Reservations.Commands.ReserveStock;
using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Domain.Reservations;
using GearFlow.Inventory.Domain.Stock;
using GearFlow.Inventory.Infrastructure;
using GearFlow.Inventory.Infrastructure.Persistence;
using GearFlow.Inventory.Infrastructure.Persistence.Repositories;
using GearFlow.Inventory.Infrastructure.Seed;
using GearFlow.Inventory.IntegrationTests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace GearFlow.Inventory.IntegrationTests.Persistence;

/// <summary>Repositórios, migrations, seed e concorrência contra um PostgreSQL real.</summary>
[Collection(InfrastructureCollection.Name)]
public sealed class PersistenceTests(InfrastructureFixture fx)
{
    private static readonly DateTime Now = new(2026, 11, 3, 13, 0, 0, DateTimeKind.Utc);

    // ---- Migrations e seed ---------------------------------------------------

    [Fact]
    public async Task Migrations_create_every_expected_table_in_the_inventory_schema()
    {
        var connectionString = await fx.CreateIsolatedDatabaseAsync();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'inventory'", connection);
        var tables = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                tables.Add(reader.GetString(0));

        tables.Should().Contain(
            ["parts", "consumables", "stock_reservations", "stock_reservation_lines", "OutboxMessage", "OutboxState", "InboxState"]);
    }

    [Fact]
    public async Task Seeder_creates_the_demo_catalog_once_with_the_fixed_ids()
    {
        var connectionString = await fx.CreateIsolatedDatabaseAsync();
        await using var provider = new ServiceCollection()
            .AddDbContext<InventoryDbContext>(o => o.UseNpgsql(connectionString))
            .BuildServiceProvider();

        await InventorySeeder.SeedAsync(provider);
        await InventorySeeder.SeedAsync(provider); // idempotente

        await using var db = fx.NewDbContext(connectionString);
        (await db.Parts.CountAsync()).Should().Be(10);
        (await db.Consumables.CountAsync()).Should().Be(7);

        var palhetas = await db.Parts.SingleAsync(p => p.Id == PartId.From(DemoCatalogIds.JogoDePalhetas));
        palhetas.Quantity.Should().Be(4);
        palhetas.IsStockBelowMinimum().Should().BeTrue();
        var agua = await db.Consumables.SingleAsync(c => c.Id == ConsumableId.From(DemoCatalogIds.AguaDesmineralizada));
        agua.Quantity.Should().Be(4m);
    }

    // ---- Repositórios --------------------------------------------------------

    [Fact]
    public async Task Parts_round_trip_through_the_repositories()
    {
        var part = Part.Create("Vela de ignição", "Iridium", "VI-6", "NGK", 4500, 60).Value;

        await using (var db = fx.NewDbContext())
        {
            await new PartRepository(db).AddAsync(part);
            await new EfUnitOfWork(db).SaveChangesAsync();
        }

        await using (var db = fx.NewDbContext())
        {
            var repository = new PartRepository(db);
            var loaded = await repository.GetByIdAsync(part.Id);

            loaded.Should().NotBeNull();
            loaded!.Name.Should().Be("Vela de ignição");
            loaded.PartNumber.Should().Be("VI-6");
            loaded.PriceCents.Should().Be(4500);
            loaded.Quantity.Should().Be(60);

            loaded.UpdateDetails("Vela dupla", "Iridium", "VI-7", "NGK", 5000, 55);
            await new EfUnitOfWork(db).SaveChangesAsync();
        }

        await using (var db = fx.NewDbContext())
        {
            var repository = new PartRepository(db);
            var updated = await repository.GetByIdAsync(part.Id);
            updated!.Name.Should().Be("Vela dupla");
            updated.Quantity.Should().Be(55);

            repository.Remove(updated);
            await new EfUnitOfWork(db).SaveChangesAsync();
        }

        await using (var db = fx.NewDbContext())
            (await new PartRepository(db).GetByIdAsync(part.Id)).Should().BeNull();
    }

    [Fact]
    public async Task GetByIds_returns_only_the_requested_parts_and_consumables()
    {
        var partA = await fx.SeedPartAsync(5);
        var partB = await fx.SeedPartAsync(5);
        await fx.SeedPartAsync(5); // fora do pedido
        var oil = await fx.SeedConsumableAsync(10m);

        await using var db = fx.NewDbContext();

        var parts = await new PartRepository(db).GetByIdsAsync([PartId.From(partA), PartId.From(partB)]);
        var consumables = await new ConsumableRepository(db).GetByIdsAsync([ConsumableId.From(oil)]);

        parts.Select(p => p.Id.Value).Should().BeEquivalentTo([partA, partB]);
        consumables.Should().ContainSingle().Which.Id.Value.Should().Be(oil);
    }

    [Fact]
    public async Task Fractional_consumable_quantities_survive_the_round_trip()
    {
        var oil = await fx.SeedConsumableAsync(10.5m);

        await using (var db = fx.NewDbContext())
        {
            var consumable = await new ConsumableRepository(db).GetByIdAsync(ConsumableId.From(oil));
            consumable!.Reserve(2.375m).IsSuccess.Should().BeTrue();
            await new EfUnitOfWork(db).SaveChangesAsync();
        }

        await using var check = fx.NewDbContext();
        var saved = await new ConsumableRepository(check).GetByIdAsync(ConsumableId.From(oil));
        saved!.Quantity.Should().Be(8.125m);
        saved.ReservedQuantity.Should().Be(2.375m);
    }

    [Fact]
    public async Task Stock_reservation_round_trips_with_its_lines_and_status_is_stored_as_text()
    {
        var serviceOrderId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var line = StockReservationLine.Create(StockItemType.Part, itemId, "Filtro", 2m).Value;
        var reservation = StockReservation.Create(serviceOrderId, [line], Now).Value;

        await using (var db = fx.NewDbContext())
        {
            await new StockReservationRepository(db).AddAsync(reservation);
            await new EfUnitOfWork(db).SaveChangesAsync();
        }

        await using (var db = fx.NewDbContext())
        {
            var loaded = await new StockReservationRepository(db).GetByServiceOrderAsync(serviceOrderId);

            loaded.Should().NotBeNull();
            loaded!.Status.Should().Be(ReservationStatus.Reserved);
            loaded.CreatedOn.Should().Be(Now);
            loaded.Lines.Should().ContainSingle()
                .Which.Should().Match<StockReservationLine>(l =>
                    l.ItemId == itemId && l.ItemType == StockItemType.Part && l.ItemName == "Filtro" && l.Quantity == 2m);

            loaded.Release(Now.AddMinutes(1));
            await new EfUnitOfWork(db).SaveChangesAsync();
        }

        (await ScalarAsync<string>("SELECT status FROM inventory.stock_reservations WHERE service_order_id = @id", ("id", serviceOrderId)))
            .Should().Be("Released");
        (await ScalarAsync<string>(
                "SELECT item_type FROM inventory.stock_reservation_lines l JOIN inventory.stock_reservations r ON r.id = l.reservation_id WHERE r.service_order_id = @id",
                ("id", serviceOrderId)))
            .Should().Be("Part");
    }

    [Fact]
    public async Task Missing_reservation_returns_null()
    {
        await using var db = fx.NewDbContext();

        (await new StockReservationRepository(db).GetByServiceOrderAsync(Guid.NewGuid())).Should().BeNull();
    }

    // ---- Concorrência --------------------------------------------------------

    [Fact]
    public async Task Two_reservations_of_the_last_unit_cannot_both_commit()
    {
        var partId = await fx.SeedPartAsync(1);

        await using var contextA = fx.NewDbContext();
        await using var contextB = fx.NewDbContext();
        var partA = await new PartRepository(contextA).GetByIdAsync(PartId.From(partId));
        var partB = await new PartRepository(contextB).GetByIdAsync(PartId.From(partId));

        // Os dois enxergam 1 unidade disponível e reservam a mesma.
        partA!.Reserve(1).IsSuccess.Should().BeTrue();
        partB!.Reserve(1).IsSuccess.Should().BeTrue();

        await new EfUnitOfWork(contextA).SaveChangesAsync();
        var second = () => new EfUnitOfWork(contextB).SaveChangesAsync();

        // O token xmin barra o segundo commit: sem ele a unidade seria vendida duas vezes.
        await second.Should().ThrowAsync<ConcurrencyConflictException>();

        await using var check = fx.NewDbContext();
        var final = await new PartRepository(check).GetByIdAsync(PartId.From(partId));
        final!.Quantity.Should().Be(0);
        final.ReservedQuantity.Should().Be(1);
    }

    [Fact]
    public async Task Serializable_transactions_competing_for_the_same_part_surface_as_a_concurrency_conflict()
    {
        // É assim que o consumer roda: o Outbox do MassTransit abre a transação em isolamento Serializable,
        // então a disputa chega como 40001 (falha de serialização), não como DbUpdateConcurrencyException.
        var partId = await fx.SeedPartAsync(1);

        await using var contextA = fx.NewDbContext();
        await using var contextB = fx.NewDbContext();
        await using var transactionA = await contextA.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
        await using var transactionB = await contextB.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);

        var partA = await new PartRepository(contextA).GetByIdAsync(PartId.From(partId));
        var partB = await new PartRepository(contextB).GetByIdAsync(PartId.From(partId));
        partA!.Reserve(1).IsSuccess.Should().BeTrue();
        partB!.Reserve(1).IsSuccess.Should().BeTrue();

        await new EfUnitOfWork(contextA).SaveChangesAsync();
        await transactionA.CommitAsync();
        var second = () => new EfUnitOfWork(contextB).SaveChangesAsync();

        await second.Should().ThrowAsync<ConcurrencyConflictException>();

        await using var check = fx.NewDbContext();
        var final = await new PartRepository(check).GetByIdAsync(PartId.From(partId));
        final!.Quantity.Should().Be(0);
        final.ReservedQuantity.Should().Be(1);
    }

    [Fact]
    public async Task Two_reservations_for_the_same_service_order_conflict_on_the_unique_index()
    {
        var serviceOrderId = Guid.NewGuid();
        StockReservation New() => StockReservation.Create(
            serviceOrderId, [StockReservationLine.Create(StockItemType.Part, Guid.NewGuid(), "x", 1m).Value], Now).Value;

        await using var contextA = fx.NewDbContext();
        await using var contextB = fx.NewDbContext();
        await new StockReservationRepository(contextA).AddAsync(New());
        await new StockReservationRepository(contextB).AddAsync(New());

        await new EfUnitOfWork(contextA).SaveChangesAsync();
        var second = () => new EfUnitOfWork(contextB).SaveChangesAsync();

        await second.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task Concurrent_updates_of_the_same_reservation_conflict()
    {
        var serviceOrderId = Guid.NewGuid();
        await using (var seed = fx.NewDbContext())
        {
            await new StockReservationRepository(seed).AddAsync(StockReservation.Create(
                serviceOrderId, [StockReservationLine.Create(StockItemType.Part, Guid.NewGuid(), "x", 1m).Value], Now).Value);
            await new EfUnitOfWork(seed).SaveChangesAsync();
        }

        await using var contextA = fx.NewDbContext();
        await using var contextB = fx.NewDbContext();
        var a = await new StockReservationRepository(contextA).GetByServiceOrderAsync(serviceOrderId);
        var b = await new StockReservationRepository(contextB).GetByServiceOrderAsync(serviceOrderId);

        a!.Release(Now);
        b!.Consume(Now);
        await new EfUnitOfWork(contextA).SaveChangesAsync();
        var second = () => new EfUnitOfWork(contextB).SaveChangesAsync();

        await second.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    // ---- Handler + Outbox na mesma transação ----------------------------------

    [Fact]
    public async Task ReserveStock_writes_stock_reservation_and_outbox_message_in_the_same_commit()
    {
        var partId = await fx.SeedPartAsync(10);
        var serviceOrderId = Guid.NewGuid();

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = fx.PostgresConnectionString,
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInventoryApplication();
        services.AddInventoryInfrastructure(configuration);

        // Sem host: o bus não sobe, então a mensagem só pode estar na tabela do Outbox.
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<ReserveStockCommand, Result<ReserveStockOutcome>>>();

        var result = await handler.HandleAsync(
            new ReserveStockCommand(serviceOrderId, [new StockItemLine(InventoryItemType.Part, partId, 4)]), default);

        result.Value.Status.Should().Be(ReserveStockStatus.Reserved);

        await using var db = fx.NewDbContext();
        var part = await db.Parts.AsNoTracking().SingleAsync(p => p.Id == PartId.From(partId));
        part.Quantity.Should().Be(6);
        part.ReservedQuantity.Should().Be(4);

        (await ScalarAsync<long>("SELECT count(*) FROM inventory.stock_reservations WHERE service_order_id = @id", ("id", serviceOrderId)))
            .Should().Be(1);
        (await ScalarAsync<long>(
                "SELECT count(*) FROM inventory.\"OutboxMessage\" WHERE \"MessageType\" LIKE '%StockReserved%' AND \"Body\" LIKE '%' || @id || '%'",
                ("id", serviceOrderId.ToString())))
            .Should().Be(1);
    }

    private async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(fx.PostgresConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
