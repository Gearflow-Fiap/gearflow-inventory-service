using GearFlow.Contracts;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;
using ReserveStockMessage = GearFlow.Contracts.ReserveStock; // o namespace desta pasta tem o mesmo nome da mensagem

namespace GearFlow.Inventory.Application.Reservations.Commands.ReserveStock;

/// <summary>Reserva TODOS os itens da OS ou nenhum (regra tudo-ou-nada do monólito).</summary>
public sealed record ReserveStockCommand(Guid ServiceOrderId, IReadOnlyList<StockItemLine> Items)
    : ICommand<Result<ReserveStockOutcome>>
{
    public static ReserveStockCommand From(ReserveStockMessage message) => new(message.CorrelationId, message.Items);
}

public enum ReserveStockStatus
{
    /// <summary>Reservou agora. Publicou <c>StockReserved</c>.</summary>
    Reserved,

    /// <summary>A OS já tinha reserva (mensagem repetida). Reenviou <c>StockReserved</c>, sem reservar de novo.</summary>
    AlreadyReserved,

    /// <summary>Faltou saldo. Nada foi reservado; publicou <c>StockInsufficient</c>.</summary>
    Insufficient,

    /// <summary>A reserva da OS já foi liberada (compensação): o <c>ReserveStock</c> é atrasado e foi ignorado.</summary>
    IgnoredAfterRelease,
}

/// <summary>Resultado de negócio do <c>ReserveStock</c>. "Faltou estoque" não é erro: é um desfecho previsto da Saga.</summary>
public sealed record ReserveStockOutcome(ReserveStockStatus Status, IReadOnlyList<MissingStockItem> Missing)
{
    public static ReserveStockOutcome Of(ReserveStockStatus status) => new(status, []);
}
