using GearFlow.Inventory.Domain.Reservations;

namespace GearFlow.Inventory.Domain.Repositories;

public interface IStockReservationRepository
{
    Task AddAsync(StockReservation reservation, CancellationToken ct = default);

    /// <summary>Reserva (rastreada) de uma OS, ou <c>null</c> se não existir.</summary>
    Task<StockReservation?> GetByServiceOrderAsync(Guid serviceOrderId, CancellationToken ct = default);
}
