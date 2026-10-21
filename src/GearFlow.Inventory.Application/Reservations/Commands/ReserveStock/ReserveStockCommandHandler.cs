using GearFlow.Contracts;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Domain.Repositories;
using GearFlow.Inventory.Domain.Reservations;
using Microsoft.Extensions.Logging;

namespace GearFlow.Inventory.Application.Reservations.Commands.ReserveStock;

/// <summary>
/// Passo 6 da Saga. Reserva todos os itens ou nenhum, numa transação local, e responde com
/// <c>StockReserved</c> ou <c>StockInsufficient</c> pelo Outbox.
///
/// Idempotente: a OS é a chave (<c>service_order_id</c> único). Mensagem repetida só reenvia a resposta.
/// </summary>
internal sealed class ReserveStockCommandHandler(
    IStockReservationRepository reservations,
    IPartRepository parts,
    IConsumableRepository consumables,
    IMessagePublisher publisher,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<ReserveStockCommandHandler> logger)
    : ICommandHandler<ReserveStockCommand, Result<ReserveStockOutcome>>
{
    public async Task<Result<ReserveStockOutcome>> HandleAsync(ReserveStockCommand command, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        var existing = await reservations.GetByServiceOrderAsync(command.ServiceOrderId, ct);
        if (existing is not null)
            return await AnswerExistingAsync(existing, now, ct);

        var catalog = await StockItemCatalog.LoadAsync(
            parts, consumables, command.Items.Select(i => (i.ItemType.ToDomain(), i.ItemId)), ct);

        // 1) Monta as linhas (isso valida a quantidade) e separa o que não dá para reservar.
        var lines = new List<StockReservationLine>();
        var missing = new List<MissingStockItem>();
        foreach (var item in command.Items)
        {
            var type = item.ItemType.ToDomain();

            if (!catalog.TryGetName(type, item.ItemId, out var name))
            {
                // Item inexistente aparece como faltante com disponível 0: a OS não consegue ser atendida.
                missing.Add(new MissingStockItem(item.ItemType, item.ItemId, "Item não encontrado", item.Quantity, 0m));
                continue;
            }

            var line = StockReservationLine.Create(type, item.ItemId, name, item.Quantity);
            if (line.IsFailure)
                return Result.Failure<ReserveStockOutcome>(line.Error);
            lines.Add(line.Value);

            if (!catalog.HasAvailability(type, item.ItemId, item.Quantity))
                missing.Add(new MissingStockItem(
                    item.ItemType, item.ItemId, name, item.Quantity, catalog.AvailableQuantity(type, item.ItemId)));
        }

        if (missing.Count > 0)
            return await RespondInsufficientAsync(command.ServiceOrderId, missing, now, ct);

        // 2) Valida a reserva inteira ANTES de mexer no saldo.
        var reservation = StockReservation.Create(command.ServiceOrderId, lines, now);
        if (reservation.IsFailure)
            return Result.Failure<ReserveStockOutcome>(reservation.Error);

        // 3) Reserva todas as linhas. Falha aqui seria inesperada (a disponibilidade foi checada acima).
        foreach (var line in lines)
        {
            var reserved = catalog.Reserve(line.ItemType, line.ItemId, line.Quantity);
            if (reserved.IsFailure)
                return Result.Failure<ReserveStockOutcome>(reserved.Error);
        }

        await reservations.AddAsync(reservation.Value, ct);
        await publisher.PublishAsync(StockReservedMessage(command.ServiceOrderId, lines, now), ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation("Estoque reservado para a OS {ServiceOrderId} ({LineCount} item(ns))",
            command.ServiceOrderId, lines.Count);
        return Result.Success(ReserveStockOutcome.Of(ReserveStockStatus.Reserved));
    }

    private async Task<Result<ReserveStockOutcome>> RespondInsufficientAsync(
        Guid serviceOrderId, List<MissingStockItem> missing, DateTime now, CancellationToken ct)
    {
        // Nada foi alterado. O SaveChanges só descarrega a mensagem do Outbox.
        await publisher.PublishAsync(new StockInsufficient(serviceOrderId, Guid.NewGuid(), now, missing), ct);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogWarning("Estoque insuficiente para a OS {ServiceOrderId}: {MissingCount} item(ns) faltando",
            serviceOrderId, missing.Count);
        return Result.Success(new ReserveStockOutcome(ReserveStockStatus.Insufficient, missing));
    }

    private async Task<Result<ReserveStockOutcome>> AnswerExistingAsync(
        StockReservation existing, DateTime now, CancellationToken ct)
    {
        if (existing.Status == ReservationStatus.Released)
        {
            // A compensação já rodou. Reservar de novo desfaria o rollback da Saga.
            logger.LogInformation(
                "ReserveStock da OS {ServiceOrderId} ignorado: a reserva já foi liberada", existing.ServiceOrderId);
            return Result.Success(ReserveStockOutcome.Of(ReserveStockStatus.IgnoredAfterRelease));
        }

        // Reserved (mensagem repetida) ou Consumed (repetida depois da execução): só responde de novo.
        await publisher.PublishAsync(StockReservedMessage(existing.ServiceOrderId, existing.Lines, now), ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(ReserveStockOutcome.Of(ReserveStockStatus.AlreadyReserved));
    }

    private static StockReserved StockReservedMessage(
        Guid serviceOrderId, IEnumerable<StockReservationLine> lines, DateTime now) =>
        new(serviceOrderId, Guid.NewGuid(), now,
            lines.Select(l => new StockItemLine(l.ItemType.ToContract(), l.ItemId, l.Quantity)).ToList());
}
