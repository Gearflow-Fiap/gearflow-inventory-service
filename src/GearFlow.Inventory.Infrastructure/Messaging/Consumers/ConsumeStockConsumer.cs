using GearFlow.Contracts;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Application.Reservations.Commands.ConsumeStock;
using MassTransit;

namespace GearFlow.Inventory.Infrastructure.Messaging.Consumers;

/// <summary>
/// Passo 7b da Saga (ponto sem volta). Reserva inexistente ou liberada não se resolve repetindo: DLQ + alerta.
/// Falha transitória (banco fora, concorrência) sobe como exceção comum e é repetida.
/// </summary>
internal sealed class ConsumeStockConsumer(IDispatcher dispatcher) : IConsumer<ConsumeStock>
{
    public async Task Consume(ConsumeContext<ConsumeStock> context)
    {
        var result = await dispatcher.Send(ConsumeStockCommand.From(context.Message), context.CancellationToken);
        result.ThrowIfFailure();
    }
}
