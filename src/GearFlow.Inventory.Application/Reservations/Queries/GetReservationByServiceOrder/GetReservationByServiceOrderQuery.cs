using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Application.Reservations.Queries.GetReservationByServiceOrder;

public sealed record GetReservationByServiceOrderQuery(Guid ServiceOrderId) : IQuery<Result<ReservationDto>>;
