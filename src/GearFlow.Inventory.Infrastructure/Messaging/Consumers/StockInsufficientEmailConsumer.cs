using GearFlow.Contracts;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Application.Notifications.NotifyStockShortage;
using MassTransit;

namespace GearFlow.Inventory.Infrastructure.Messaging.Consumers;

/// <summary>
/// Envia o e-mail de estoque insuficiente a partir do <c>StockInsufficient</c> que o próprio serviço publicou. O
/// orquestrador consome o mesmo evento na fila dele: cada consumidor recebe a sua cópia.
/// </summary>
internal sealed class StockInsufficientEmailConsumer(IDispatcher dispatcher) : IConsumer<StockInsufficient>
{
    public async Task Consume(ConsumeContext<StockInsufficient> context) =>
        (await dispatcher.Send(NotifyStockShortageCommand.From(context.Message), context.CancellationToken)).ThrowIfFailure();
}
