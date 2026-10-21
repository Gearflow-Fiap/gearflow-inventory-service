using System.Diagnostics;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;
using Microsoft.Extensions.Logging;

namespace GearFlow.Inventory.Application.Behaviors;

/// <summary>Span por query. Só loga em Debug (leitura não muda estado) e em erro inesperado.</summary>
public sealed class QueryLoggingDecorator<TQuery, TResult>(
    IQueryHandler<TQuery, TResult> inner,
    ILogger logger)
    : IQueryHandler<TQuery, TResult>
    where TQuery : IQuery<TResult>
    where TResult : Result
{
    public async Task<TResult> HandleAsync(TQuery query, CancellationToken ct)
    {
        var name = typeof(TQuery).Name;
        using var activity = InventoryTelemetry.ActivitySource.StartActivity($"query {name}");
        activity?.SetTag("inventory.query", name);

        try
        {
            var result = await inner.HandleAsync(query, ct);

            activity?.SetTag("inventory.success", result.IsSuccess);
            if (result.IsFailure)
                activity?.SetTag("inventory.error_code", result.Error.Code);
            logger.LogDebug("Query {Query} concluída (sucesso: {Success})", name, result.IsSuccess);

            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Query {Query} lançou exceção", name);
            throw;
        }
    }
}
