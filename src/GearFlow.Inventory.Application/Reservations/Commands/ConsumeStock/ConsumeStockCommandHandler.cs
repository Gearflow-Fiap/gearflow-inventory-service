using GearFlow.Contracts;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Domain.Repositories;
using GearFlow.Inventory.Domain.Reservations;
using Microsoft.Extensions.Logging;

namespace GearFlow.Inventory.Application.Reservations.Commands.ConsumeStock;

/// <summary>
/// Passo 7b da Saga: baixa definitiva do reservado (a regra "consumo na finalização" do monólito) e
/// <c>LowStockAlert</c> para os itens que ficaram no/abaixo do mínimo. Usa as linhas da própria reserva.
///
/// Falhas aqui (reserva inexistente ou liberada, item sumiu) são inconsistências graves: voltam como
/// <c>Result</c> de erro e o consumer deixa subir para retry/DLQ. Repetição é sucesso: reserva já
/// <c>Consumed</c> só reenvia <c>StockConsumed</c>, sem baixar de novo nem repetir o alerta.
/// </summary>
internal sealed class ConsumeStockCommandHandler(
    IStockReservationRepository reservations,
    IPartRepository parts,
    IConsumableRepository consumables,
    IMessagePublisher publisher,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<ConsumeStockCommandHandler> logger)
    : ICommandHandler<ConsumeStockCommand, Result>
{
    public async Task<Result> HandleAsync(ConsumeStockCommand command, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        var reservation = await reservations.GetByServiceOrderAsync(command.ServiceOrderId, ct);
        if (reservation is null)
            return Result.Failure(Error.NotFound("Reservation.NotFound",
                $"Não há reserva de estoque para a OS {command.ServiceOrderId}."));

        if (reservation.Status == ReservationStatus.Reserved)
        {
            var consumed = await ConsumeLinesAsync(reservation, now, ct);
            if (consumed.IsFailure)
                return consumed;
        }

        // Consumed vira no-op de sucesso; Released devolve o conflito.
        var transition = reservation.Consume(now);
        if (transition.IsFailure)
            return transition;

        await publisher.PublishAsync(new StockConsumed(command.ServiceOrderId, Guid.NewGuid(), now), ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Estoque da OS {ServiceOrderId} consumido", command.ServiceOrderId);
        return Result.Success();
    }

    private async Task<Result> ConsumeLinesAsync(StockReservation reservation, DateTime now, CancellationToken ct)
    {
        var catalog = await StockItemCatalog.LoadAsync(
            parts, consumables, reservation.Lines.Select(l => (l.ItemType, l.ItemId)), ct);

        foreach (var line in reservation.Lines)
        {
            var consumed = catalog.Consume(line.ItemType, line.ItemId, line.Quantity);
            if (consumed.IsFailure)
                return consumed;

            // Alerta só no consumo (como no monólito): é quando o saldo "de verdade" cai.
            if (catalog.LowStock(line.ItemType, line.ItemId) is { } low)
                await publisher.PublishAsync(
                    new LowStockAlert(Guid.NewGuid(), now, line.ItemType.ToContract(), line.ItemId,
                        line.ItemName, low.Remaining, low.Minimum),
                    ct);
        }

        return Result.Success();
    }
}
