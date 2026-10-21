namespace GearFlow.Inventory.Application.Abstractions;

/// <summary>
/// Publica mensagens de integração (<c>GearFlow.Contracts</c>). A Application não conhece MassTransit:
/// a implementação na Infrastructure grava no Outbox, na mesma transação do <see cref="IUnitOfWork"/>.
/// </summary>
public interface IMessagePublisher
{
    Task PublishAsync<TMessage>(TMessage message, CancellationToken ct = default) where TMessage : class;
}
