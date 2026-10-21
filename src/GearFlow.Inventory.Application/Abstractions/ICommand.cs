using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Application.Abstractions;

/// <summary>Intenção de mudar estado. O resultado é sempre <see cref="Result"/> ou <see cref="Result{TValue}"/>.</summary>
public interface ICommand<TResult> where TResult : Result;

public interface ICommandHandler<in TCommand, TResult>
    where TCommand : ICommand<TResult>
    where TResult : Result
{
    Task<TResult> HandleAsync(TCommand command, CancellationToken ct);
}
