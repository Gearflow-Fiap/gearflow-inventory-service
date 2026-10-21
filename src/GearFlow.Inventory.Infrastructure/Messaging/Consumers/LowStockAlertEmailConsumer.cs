using GearFlow.Contracts;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Application.Notifications.NotifyLowStock;
using MassTransit;

namespace GearFlow.Inventory.Infrastructure.Messaging.Consumers;

/// <summary>
/// Envia o e-mail de estoque baixo a partir do <c>LowStockAlert</c> que o próprio serviço publicou. Fica numa fila à
/// parte: SMTP fora do ar é repetido (e acaba na DLQ) sem nunca atrasar nem desfazer a baixa de estoque.
/// </summary>
internal sealed class LowStockAlertEmailConsumer(IDispatcher dispatcher) : IConsumer<LowStockAlert>
{
    public async Task Consume(ConsumeContext<LowStockAlert> context) =>
        (await dispatcher.Send(NotifyLowStockCommand.From(context.Message), context.CancellationToken)).ThrowIfFailure();
}
