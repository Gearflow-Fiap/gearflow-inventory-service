using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Application.Abstractions;

/// <summary>Leitura de estado. Nunca muda nada nem publica mensagem.</summary>
public interface IQuery<TResult> where TResult : Result;

public interface IQueryHandler<in TQuery, TResult>
    where TQuery : IQuery<TResult>
    where TResult : Result
{
    Task<TResult> HandleAsync(TQuery query, CancellationToken ct);
}
