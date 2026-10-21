using GearFlow.Inventory.Domain.Repositories;
using GearFlow.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace GearFlow.Inventory.Infrastructure.Persistence.Repositories;

internal sealed class ConsumableRepository(InventoryDbContext db) : IConsumableRepository
{
    public async Task AddAsync(Consumable consumable, CancellationToken ct = default) =>
        await db.Consumables.AddAsync(consumable, ct);

    public async Task<Consumable?> GetByIdAsync(ConsumableId id, CancellationToken ct = default) =>
        await db.Consumables.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<Consumable>> GetByIdsAsync(
        IReadOnlyCollection<ConsumableId> ids, CancellationToken ct = default)
    {
        var wanted = ids.ToList();
        return await db.Consumables.Where(c => wanted.Contains(c.Id)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Consumable>> ListAsync(CancellationToken ct = default) =>
        await db.Consumables.AsNoTracking().OrderBy(c => c.Name).ToListAsync(ct);

    public void Remove(Consumable consumable) => db.Consumables.Remove(consumable);
}
