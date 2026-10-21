using GearFlow.Inventory.Domain.Repositories;
using GearFlow.Inventory.Domain.Reservations;
using Microsoft.EntityFrameworkCore;

namespace GearFlow.Inventory.Infrastructure.Persistence.Repositories;

internal sealed class StockReservationRepository(InventoryDbContext db) : IStockReservationRepository
{
    public async Task AddAsync(StockReservation reservation, CancellationToken ct = default) =>
        await db.StockReservations.AddAsync(reservation, ct);

    // Rastreada (sem AsNoTracking): Release/Consume alteram a reserva e o handler persiste tudo junto.
    // As linhas (owned) vêm junto automaticamente.
    public async Task<StockReservation?> GetByServiceOrderAsync(Guid serviceOrderId, CancellationToken ct = default) =>
        await db.StockReservations.FirstOrDefaultAsync(r => r.ServiceOrderId == serviceOrderId, ct);
}
