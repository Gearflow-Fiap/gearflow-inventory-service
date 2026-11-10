using GearFlow.Inventory.Domain.Stock;
using GearFlow.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace GearFlow.Inventory.IntegrationTests.Fixtures;

/// <summary>
/// Sobe UM Postgres e UM RabbitMQ reais (Testcontainers) e os compartilha entre todos os testes da coleção.
/// Cada teste usa dados próprios (GUIDs novos), então não precisam limpar nada. Requer o Docker rodando.
/// </summary>
public sealed class InfrastructureFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17")
        .WithDatabase("gearflow_inventory")
        .WithUsername("gearflow")
        .WithPassword("gearflow_test")
        .Build();

    // Imagem -management: a API HTTP de gerenciamento permite inspecionar as filas de erro (DLQ).
    // Usuário próprio: o `guest` do RabbitMQ só aceita conexões de dentro do container (loopback).
    private readonly RabbitMqContainer _rabbit = new RabbitMqBuilder("rabbitmq:4-management")
        .WithUsername(RabbitUsername)
        .WithPassword(RabbitPassword)
        .WithPortBinding(15672, true)
        .Build();

    private Lazy<InventoryApiFactory>? _api;

    internal const string RabbitUsername = "inventory";
    internal const string RabbitPassword = "inventory_test";

    internal string PostgresConnectionString => _postgres.GetConnectionString();
    internal string RabbitHost => _rabbit.Hostname;
    internal int RabbitPort => _rabbit.GetMappedPublicPort(5672);

    internal InventoryApiFactory Api => _api!.Value;
    internal TestBusClient Bus { get; private set; } = null!;
    internal RabbitMqManagementClient Management { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbit.StartAsync());

        await using (var db = NewDbContext())
            await db.Database.MigrateAsync();

        Bus = await TestBusClient.StartAsync(RabbitHost, RabbitPort, RabbitUsername, RabbitPassword);
        Management = new RabbitMqManagementClient(RabbitHost, _rabbit.GetMappedPublicPort(15672), RabbitUsername, RabbitPassword);
        _api = new Lazy<InventoryApiFactory>(() => new InventoryApiFactory(this));
    }

    public async Task DisposeAsync()
    {
        if (_api is { IsValueCreated: true })
            await _api.Value.DisposeAsync();

        Management?.Dispose();
        if (Bus is not null)
            await Bus.DisposeAsync();

        await _rabbit.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    internal InventoryDbContext NewDbContext(string? connectionString = null) =>
        new(new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(connectionString ?? PostgresConnectionString)
            .Options);

    /// <summary>Cria um database novo e vazio (com as migrations aplicadas) no mesmo servidor.</summary>
    internal async Task<string> CreateIsolatedDatabaseAsync()
    {
        var connectionString = await CreateEmptyDatabaseAsync();
        await using var db = NewDbContext(connectionString);
        await db.Database.MigrateAsync();
        return connectionString;
    }

    /// <summary>Cria um database novo, SEM nenhuma migration aplicada (o estado de um cluster recém-criado).</summary>
    internal async Task<string> CreateEmptyDatabaseAsync()
    {
        var name = $"isolated_{Guid.NewGuid():N}";

        await using var admin = new NpgsqlConnection(PostgresConnectionString);
        await admin.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", admin);
        await create.ExecuteNonQueryAsync();

        return new NpgsqlConnectionStringBuilder(PostgresConnectionString) { Database = name }.ConnectionString;
    }

    internal async Task<Guid> SeedPartAsync(int quantity, string? name = null)
    {
        var part = Part.Create(name ?? $"Peça {Guid.NewGuid():N}", "desc", $"PN-{Guid.NewGuid():N}"[..12], "Bosch", 5000, quantity).Value;
        await using var db = NewDbContext();
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        return part.Id.Value;
    }

    internal async Task<Guid> SeedConsumableAsync(decimal quantity, string? name = null)
    {
        var consumable = Consumable.Create(name ?? $"Insumo {Guid.NewGuid():N}", 3000, quantity).Value;
        await using var db = NewDbContext();
        db.Consumables.Add(consumable);
        await db.SaveChangesAsync();
        return consumable.Id.Value;
    }
}

[CollectionDefinition(Name)]
public sealed class InfrastructureCollection : ICollectionFixture<InfrastructureFixture>
{
    public const string Name = "infrastructure";
}
