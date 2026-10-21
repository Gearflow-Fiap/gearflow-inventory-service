using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Application.Consumables.Commands.AddConsumableStock;
using GearFlow.Inventory.Application.Consumables.Commands.CreateConsumable;
using GearFlow.Inventory.Application.Consumables.Commands.DeleteConsumable;
using GearFlow.Inventory.Application.Consumables.Commands.UpdateConsumable;
using GearFlow.Inventory.Application.Consumables.Queries.GetConsumableById;
using GearFlow.Inventory.Application.Consumables.Queries.GetConsumables;
using GearFlow.Inventory.Application.Parts.Commands.AddPartStock;
using GearFlow.Inventory.Application.Parts.Commands.CreatePart;
using GearFlow.Inventory.Application.Parts.Commands.DeletePart;
using GearFlow.Inventory.Application.Parts.Commands.UpdatePart;
using GearFlow.Inventory.Application.Parts.Queries.GetPartById;
using GearFlow.Inventory.Application.Parts.Queries.GetParts;
using GearFlow.Inventory.Application.Reservations.Commands.ConsumeStock;
using GearFlow.Inventory.Application.Reservations.Commands.ReleaseStock;
using GearFlow.Inventory.Application.Reservations.Commands.ReserveStock;
using GearFlow.Inventory.Application.Reservations.Queries.GetReservationByServiceOrder;
using GearFlow.Inventory.Domain.Repositories;
using GearFlow.Inventory.Domain.Reservations;
using GearFlow.Inventory.Domain.Stock;
using Microsoft.Extensions.Logging.Abstractions;

namespace GearFlow.Inventory.UnitTests.Application;

/// <summary>
/// Bancada dos testes de handler: repositórios em memória + publisher que grava as mensagens + unit of
/// work que conta os commits. Os handlers rodam sobre o domínio REAL (sem mocks no domínio), então os
/// testes verificam saldo, estado da reserva e mensagens publicadas.
/// </summary>
internal sealed class StockTestBed
{
    public static readonly DateTime Now = new(2026, 11, 3, 13, 0, 0, DateTimeKind.Utc);

    public InMemoryPartRepository Parts { get; } = new();
    public InMemoryConsumableRepository Consumables { get; } = new();
    public InMemoryReservationRepository Reservations { get; } = new();
    public RecordingPublisher Publisher { get; } = new();
    public CountingUnitOfWork UnitOfWork { get; } = new();
    public FixedTimeProvider Clock { get; } = new(Now);

    public Part AddPart(int quantity = 10, string partNumber = "PN-1", string name = "Filtro de óleo")
    {
        var part = Part.Create(name, "desc", partNumber, "Bosch", 5000, quantity).Value;
        Parts.Items.Add(part);
        return part;
    }

    public Consumable AddConsumable(decimal quantity = 10m, string name = "Óleo 5W30")
    {
        var consumable = Consumable.Create(name, 3000, quantity).Value;
        Consumables.Items.Add(consumable);
        return consumable;
    }

    public ReserveStockCommandHandler Reserve => new(
        Reservations, Parts, Consumables, Publisher, UnitOfWork, Clock, NullLogger<ReserveStockCommandHandler>.Instance);

    public ReleaseStockCommandHandler Release => new(
        Reservations, Parts, Consumables, Publisher, UnitOfWork, Clock, NullLogger<ReleaseStockCommandHandler>.Instance);

    public ConsumeStockCommandHandler Consume => new(
        Reservations, Parts, Consumables, Publisher, UnitOfWork, Clock, NullLogger<ConsumeStockCommandHandler>.Instance);

    public GetReservationByServiceOrderQueryHandler GetReservation => new(Reservations);

    public CreatePartCommandHandler CreatePart => new(Parts, UnitOfWork);
    public UpdatePartCommandHandler UpdatePart => new(Parts, UnitOfWork);
    public DeletePartCommandHandler DeletePart => new(Parts, UnitOfWork);
    public AddPartStockCommandHandler AddPartStock => new(Parts, Publisher, UnitOfWork, Clock);
    public GetPartsQueryHandler GetParts => new(Parts);
    public GetPartByIdQueryHandler GetPartById => new(Parts);

    public CreateConsumableCommandHandler CreateConsumable => new(Consumables, UnitOfWork);
    public UpdateConsumableCommandHandler UpdateConsumable => new(Consumables, UnitOfWork);
    public DeleteConsumableCommandHandler DeleteConsumable => new(Consumables, UnitOfWork);
    public AddConsumableStockCommandHandler AddConsumableStock => new(Consumables, Publisher, UnitOfWork, Clock);
    public GetConsumablesQueryHandler GetConsumables => new(Consumables);
    public GetConsumableByIdQueryHandler GetConsumableById => new(Consumables);
}

internal sealed class InMemoryPartRepository : IPartRepository
{
    public List<Part> Items { get; } = [];

    public Task AddAsync(Part part, CancellationToken ct = default)
    {
        Items.Add(part);
        return Task.CompletedTask;
    }

    public Task<Part?> GetByIdAsync(PartId id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<Part>> GetByIdsAsync(IReadOnlyCollection<PartId> ids, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Part>>(Items.Where(p => ids.Contains(p.Id)).ToList());

    public Task<IReadOnlyList<Part>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Part>>(Items.OrderBy(p => p.Name).ToList());

    public void Remove(Part part) => Items.Remove(part);
}

internal sealed class InMemoryConsumableRepository : IConsumableRepository
{
    public List<Consumable> Items { get; } = [];

    public Task AddAsync(Consumable consumable, CancellationToken ct = default)
    {
        Items.Add(consumable);
        return Task.CompletedTask;
    }

    public Task<Consumable?> GetByIdAsync(ConsumableId id, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(c => c.Id == id));

    public Task<IReadOnlyList<Consumable>> GetByIdsAsync(IReadOnlyCollection<ConsumableId> ids, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Consumable>>(Items.Where(c => ids.Contains(c.Id)).ToList());

    public Task<IReadOnlyList<Consumable>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Consumable>>(Items.OrderBy(c => c.Name).ToList());

    public void Remove(Consumable consumable) => Items.Remove(consumable);
}

internal sealed class InMemoryReservationRepository : IStockReservationRepository
{
    public List<StockReservation> Items { get; } = [];

    public Task AddAsync(StockReservation reservation, CancellationToken ct = default)
    {
        Items.Add(reservation);
        return Task.CompletedTask;
    }

    public Task<StockReservation?> GetByServiceOrderAsync(Guid serviceOrderId, CancellationToken ct = default) =>
        Task.FromResult(Items.FirstOrDefault(r => r.ServiceOrderId == serviceOrderId));
}

internal sealed class RecordingPublisher : IMessagePublisher
{
    public List<object> Messages { get; } = [];

    public Task PublishAsync<TMessage>(TMessage message, CancellationToken ct = default) where TMessage : class
    {
        Messages.Add(message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<T> Of<T>() => Messages.OfType<T>().ToList();
}

internal sealed class CountingUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}

internal sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
}
