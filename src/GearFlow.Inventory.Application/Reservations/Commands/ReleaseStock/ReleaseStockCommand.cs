using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;
using ReleaseStockMessage = GearFlow.Contracts.ReleaseStock; // o namespace desta pasta tem o mesmo nome da mensagem

namespace GearFlow.Inventory.Application.Reservations.Commands.ReleaseStock;

/// <summary>Compensação da Saga: devolve a reserva da OS ao disponível. Idempotente.</summary>
public sealed record ReleaseStockCommand(Guid ServiceOrderId, string Reason) : ICommand<Result>
{
    public static ReleaseStockCommand From(ReleaseStockMessage message) => new(message.CorrelationId, message.Reason);
}
