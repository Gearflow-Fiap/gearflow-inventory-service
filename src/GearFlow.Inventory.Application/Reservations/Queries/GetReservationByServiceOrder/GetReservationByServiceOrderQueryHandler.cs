using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Domain.Repositories;

namespace GearFlow.Inventory.Application.Reservations.Queries.GetReservationByServiceOrder;

internal sealed class GetReservationByServiceOrderQueryHandler(IStockReservationRepository reservations)
    : IQueryHandler<GetReservationByServiceOrderQuery, Result<ReservationDto>>
{
    public async Task<Result<ReservationDto>> HandleAsync(GetReservationByServiceOrderQuery query, CancellationToken ct)
    {
        var reservation = await reservations.GetByServiceOrderAsync(query.ServiceOrderId, ct);
        if (reservation is null)
            return Result.Failure<ReservationDto>(Error.NotFound("Reservation.NotFound",
                $"Não há reserva de estoque para a OS {query.ServiceOrderId}."));

        return Result.Success(ReservationDto.From(reservation));
    }
}
