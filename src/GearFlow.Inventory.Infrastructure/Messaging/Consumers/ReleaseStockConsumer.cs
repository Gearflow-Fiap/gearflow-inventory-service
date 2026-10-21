using GearFlow.Contracts;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Application.Reservations.Commands.ReleaseStock;
using MassTransit;

namespace GearFlow.Inventory.Infrastructure.Messaging.Consumers;

/// <summary>Compensação do passo 6. Liberar uma reserva já consumida é conflito permanente: vai para a DLQ.</summary>
internal sealed class ReleaseStockConsumer(IDispatcher dispatcher) : IConsumer<ReleaseStock>
{
    public async Task Consume(ConsumeContext<ReleaseStock> context)
    {
        var result = await dispatcher.Send(ReleaseStockCommand.From(context.Message), context.CancellationToken);
        result.ThrowIfFailure();
    }
}
