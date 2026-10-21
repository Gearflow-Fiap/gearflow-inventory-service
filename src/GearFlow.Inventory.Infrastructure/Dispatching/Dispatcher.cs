using System.Collections.Concurrent;
using FluentValidation;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Application.Behaviors;
using GearFlow.Inventory.Domain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GearFlow.Inventory.Infrastructure.Dispatching;

/// <summary>
/// Dispatcher próprio (no lugar do MediatR, cuja licença ficou comercial). Resolve o handler no DI pelo
/// tipo do command/query e o envolve no pipeline fixo, de fora para dentro:
/// <c>log + span → validação → handler</c> (queries: <c>log + span → handler</c>).
/// </summary>
internal sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    // Uma "ponte" por (tipo do command, tipo do resultado), criada uma vez. Evita reflexão a cada chamada.
    private static readonly ConcurrentDictionary<(Type Request, Type Result), object> Invokers = new();

    public Task<TResult> Send<TResult>(ICommand<TResult> command, CancellationToken ct = default)
        where TResult : Result
    {
        ArgumentNullException.ThrowIfNull(command);

        var invoker = (CommandInvoker<TResult>)Invokers.GetOrAdd(
            (command.GetType(), typeof(TResult)),
            static key => Activator.CreateInstance(typeof(CommandInvoker<,>).MakeGenericType(key.Request, key.Result))!);

        return invoker.InvokeAsync(command, services, ct);
    }

    public Task<TResult> Query<TResult>(IQuery<TResult> query, CancellationToken ct = default)
        where TResult : Result
    {
        ArgumentNullException.ThrowIfNull(query);

        var invoker = (QueryInvoker<TResult>)Invokers.GetOrAdd(
            (query.GetType(), typeof(TResult)),
            static key => Activator.CreateInstance(typeof(QueryInvoker<,>).MakeGenericType(key.Request, key.Result))!);

        return invoker.InvokeAsync(query, services, ct);
    }

    private abstract class CommandInvoker<TResult> where TResult : Result
    {
        public abstract Task<TResult> InvokeAsync(ICommand<TResult> command, IServiceProvider services, CancellationToken ct);
    }

    private sealed class CommandInvoker<TCommand, TResult> : CommandInvoker<TResult>
        where TCommand : ICommand<TResult>
        where TResult : Result
    {
        public override Task<TResult> InvokeAsync(ICommand<TResult> command, IServiceProvider services, CancellationToken ct)
        {
            ICommandHandler<TCommand, TResult> pipeline = services.GetRequiredService<ICommandHandler<TCommand, TResult>>();

            pipeline = new ValidationDecorator<TCommand, TResult>(pipeline, services.GetServices<IValidator<TCommand>>());
            pipeline = new CommandLoggingDecorator<TCommand, TResult>(pipeline, services.GetRequiredService<ILogger<TCommand>>());

            return pipeline.HandleAsync((TCommand)command, ct);
        }
    }

    private abstract class QueryInvoker<TResult> where TResult : Result
    {
        public abstract Task<TResult> InvokeAsync(IQuery<TResult> query, IServiceProvider services, CancellationToken ct);
    }

    private sealed class QueryInvoker<TQuery, TResult> : QueryInvoker<TResult>
        where TQuery : IQuery<TResult>
        where TResult : Result
    {
        public override Task<TResult> InvokeAsync(IQuery<TResult> query, IServiceProvider services, CancellationToken ct)
        {
            IQueryHandler<TQuery, TResult> pipeline = services.GetRequiredService<IQueryHandler<TQuery, TResult>>();

            pipeline = new QueryLoggingDecorator<TQuery, TResult>(pipeline, services.GetRequiredService<ILogger<TQuery>>());

            return pipeline.HandleAsync((TQuery)query, ct);
        }
    }
}
