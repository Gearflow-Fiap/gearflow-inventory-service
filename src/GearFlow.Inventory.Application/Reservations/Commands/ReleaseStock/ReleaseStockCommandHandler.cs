using GearFlow.Contracts;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Domain.Repositories;
using GearFlow.Inventory.Domain.Reservations;
using Microsoft.Extensions.Logging;

namespace GearFlow.Inventory.Application.Reservations.Commands.ReleaseStock;

/// <summary>
/// Compensação do passo 6. Devolve o reservado ao disponível e marca a reserva como <c>Released</c>.
///
/// Idempotente e tolerante a mensagem fora de ordem:
/// <list type="bullet">
/// <item>Sem reserva (nunca reservou, ou o <c>ReserveStock</c> ainda não chegou): grava a reserva já como
/// <c>Released</c> (tombstone) e responde sucesso. Um <c>ReserveStock</c> atrasado será ignorado.</item>
/// <item>Já <c>Released</c>: só responde de novo.</item>
/// <item><c>Consumed</c>: conflito. Depois da execução não há compensação (ponto sem volta).</item>
/// </list>
/// </summary>
internal sealed class ReleaseStockCommandHandler(
    IStockReservationRepository reservations,
    IPartRepository parts,
    IConsumableRepository consumables,
    IMessagePublisher publisher,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<ReleaseStockCommandHandler> logger)
    : ICommandHandler<ReleaseStockCommand, Result>
{
    public async Task<Result> HandleAsync(ReleaseStockCommand command, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        var reservation = await reservations.GetByServiceOrderAsync(command.ServiceOrderId, ct);
        if (reservation is null)
        {
            await reservations.AddAsync(StockReservation.CreateReleased(command.ServiceOrderId, now), ct);
            logger.LogInformation(
                "ReleaseStock da OS {ServiceOrderId} sem reserva prévia: tombstone gravado. Motivo: {Reason}",
                command.ServiceOrderId, command.Reason);
        }
        else if (reservation.Status == ReservationStatus.Reserved)
        {
            var released = await ReleaseLinesAsync(reservation, ct);
            if (released.IsFailure)
                return released;
        }

        if (reservation is not null)
        {
            // Released vira no-op de sucesso; Consumed devolve o conflito.
            var transition = reservation.Release(now);
            if (transition.IsFailure)
                return transition;
        }

        await publisher.PublishAsync(new StockReleased(command.ServiceOrderId, Guid.NewGuid(), now), ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Estoque da OS {ServiceOrderId} liberado. Motivo: {Reason}",
            command.ServiceOrderId, command.Reason);
        return Result.Success();
    }

    private async Task<Result> ReleaseLinesAsync(StockReservation reservation, CancellationToken ct)
    {
        var catalog = await StockItemCatalog.LoadAsync(
            parts, consumables, reservation.Lines.Select(l => (l.ItemType, l.ItemId)), ct);

        foreach (var line in reservation.Lines)
        {
            if (!catalog.Contains(line.ItemType, line.ItemId))
            {
                // Não deveria acontecer (item com reserva ativa não pode ser removido). Não trava a compensação.
                logger.LogWarning(
                    "Item {ItemType} {ItemId} da reserva da OS {ServiceOrderId} não existe mais: nada a devolver",
                    line.ItemType, line.ItemId, reservation.ServiceOrderId);
                continue;
            }

            var released = catalog.Release(line.ItemType, line.ItemId, line.Quantity);
            if (released.IsFailure)
                return released;
        }

        return Result.Success();
    }
}
