using GearFlow.Inventory.Domain.Stock;

namespace GearFlow.Inventory.Domain.Repositories;

public interface IConsumableRepository
{
    Task AddAsync(Consumable consumable, CancellationToken ct = default);
    Task<Consumable?> GetByIdAsync(ConsumableId id, CancellationToken ct = default);
    Task<IReadOnlyList<Consumable>> GetByIdsAsync(IReadOnlyCollection<ConsumableId> ids, CancellationToken ct = default);
    Task<IReadOnlyList<Consumable>> ListAsync(CancellationToken ct = default);
    void Remove(Consumable consumable);
}
