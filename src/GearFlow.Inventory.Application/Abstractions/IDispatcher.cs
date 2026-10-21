using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Application.Abstractions;

/// <summary>
/// Ponto único de entrada da Application (substitui o MediatR). Endpoints HTTP e consumers de
/// mensageria só montam o command/query e despacham; validação e log são decorators aplicados a todo handler.
/// </summary>
public interface IDispatcher
{
    Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default) where TResult : Result;
    Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default) where TResult : Result;
}
