using GearFlow.Inventory.Application.Abstractions;
using MassTransit;

namespace GearFlow.Inventory.Infrastructure.Messaging;

/// <summary>
/// Publica pelo <see cref="IPublishEndpoint"/> do escopo. Com o Outbox do EF Core ligado, dentro de um consumer a
/// mensagem vai para a transação do consumer e, numa requisição HTTP, para o bus outbox: nos dois casos ela só
/// sai para o broker depois do commit do <c>SaveChangesAsync</c> do handler, na mesma transação do saldo.
/// </summary>
internal sealed class MassTransitMessagePublisher(IPublishEndpoint endpoint) : IMessagePublisher
{
    public Task PublishAsync<TMessage>(TMessage message, CancellationToken ct = default) where TMessage : class =>
        endpoint.Publish(message, ct);
}
