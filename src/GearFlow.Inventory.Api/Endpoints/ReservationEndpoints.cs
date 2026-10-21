using GearFlow.Inventory.Api.Http;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Application.Reservations;
using GearFlow.Inventory.Application.Reservations.Queries.GetReservationByServiceOrder;

namespace GearFlow.Inventory.Api.Endpoints;

/// <summary>
/// Consulta da reserva de estoque de uma OS. Só leitura: reservar, liberar e consumir são comandos
/// da Saga (RabbitMQ), nunca REST.
/// </summary>
public sealed class ReservationEndpoints : IEndpoint
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/inventory/reservations").WithTags("Inventory");

        group.MapGet("/service-order/{serviceOrderId:guid}", async (Guid serviceOrderId, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.Query(new GetReservationByServiceOrderQuery(serviceOrderId), ct)).ToOk())
            .WithSummary("Consulta a reserva de estoque de uma OS.")
            .WithDescription("Retorna o status da reserva (Reserved, Released ou Consumed) e as linhas reservadas; 404 se a OS não tem reserva.")
            .Produces<ReservationDto>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();
    }
}
