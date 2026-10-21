using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;
using ConsumeStockMessage = GearFlow.Contracts.ConsumeStock; // o namespace desta pasta tem o mesmo nome da mensagem

namespace GearFlow.Inventory.Application.Reservations.Commands.ConsumeStock;

/// <summary>
/// Baixa definitiva do reservado, depois que a OS foi executada. Ponto sem volta da Saga:
/// só tem retry, não tem compensação.
/// </summary>
public sealed record ConsumeStockCommand(Guid ServiceOrderId) : ICommand<Result>
{
    public static ConsumeStockCommand From(ConsumeStockMessage message) => new(message.CorrelationId);
}
