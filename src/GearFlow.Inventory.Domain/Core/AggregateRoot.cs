namespace GearFlow.Inventory.Domain.Core;

/// <summary>
/// Raiz de agregado. Sem domain events de propósito: os eventos de integração saem dos handlers
/// pelo Outbox (o monólito arrastava MediatR.Contracts para o Domain só por causa disso).
/// </summary>
public abstract class AggregateRoot<TId> : Entity<TId>
    where TId : notnull
{
    protected AggregateRoot(TId id) : base(id) { }
}
