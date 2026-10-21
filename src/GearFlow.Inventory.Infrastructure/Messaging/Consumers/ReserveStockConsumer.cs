using GearFlow.Contracts;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Application.Reservations.Commands.ReserveStock;
using MassTransit;

namespace GearFlow.Inventory.Infrastructure.Messaging.Consumers;

/// <summary>Passo 6 da Saga. Consumer fino: monta o command e despacha. Quem responde é o handler (Outbox).</summary>
internal sealed class ReserveStockConsumer(IDispatcher dispatcher) : IConsumer<ReserveStock>
{
    public async Task Consume(ConsumeContext<ReserveStock> context)
    {
        // "Faltou estoque" NÃO é falha: volta como Result de sucesso (StockInsufficient já foi publicado).
        // Falha aqui é mensagem malformada: permanente, vai direto para a DLQ.
        var result = await dispatcher.Send(ReserveStockCommand.From(context.Message), context.CancellationToken);
        result.ThrowIfFailure();
    }
}
