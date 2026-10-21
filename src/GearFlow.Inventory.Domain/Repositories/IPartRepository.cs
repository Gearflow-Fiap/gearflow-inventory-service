using GearFlow.Inventory.Domain.Stock;

namespace GearFlow.Inventory.Domain.Repositories;

public interface IPartRepository
{
    Task AddAsync(Part part, CancellationToken ct = default);
    Task<Part?> GetByIdAsync(PartId id, CancellationToken ct = default);
    Task<IReadOnlyList<Part>> GetByIdsAsync(IReadOnlyCollection<PartId> ids, CancellationToken ct = default);
    Task<IReadOnlyList<Part>> ListAsync(CancellationToken ct = default);
    void Remove(Part part);
}
