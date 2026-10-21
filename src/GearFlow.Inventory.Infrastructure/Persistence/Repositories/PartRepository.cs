using GearFlow.Inventory.Domain.Repositories;
using GearFlow.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;

namespace GearFlow.Inventory.Infrastructure.Persistence.Repositories;

internal sealed class PartRepository(InventoryDbContext db) : IPartRepository
{
    public async Task AddAsync(Part part, CancellationToken ct = default) => await db.Parts.AddAsync(part, ct);

    public async Task<Part?> GetByIdAsync(PartId id, CancellationToken ct = default) =>
        await db.Parts.FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<IReadOnlyList<Part>> GetByIdsAsync(IReadOnlyCollection<PartId> ids, CancellationToken ct = default)
    {
        var wanted = ids.ToList();
        return await db.Parts.Where(p => wanted.Contains(p.Id)).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Part>> ListAsync(CancellationToken ct = default) =>
        await db.Parts.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);

    public void Remove(Part part) => db.Parts.Remove(part);
}
