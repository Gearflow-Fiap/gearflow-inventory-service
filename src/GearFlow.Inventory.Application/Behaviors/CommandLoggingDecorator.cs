using System.Diagnostics;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;
using Microsoft.Extensions.Logging;

namespace GearFlow.Inventory.Application.Behaviors;

/// <summary>Log + span do OpenTelemetry por command. Camada mais externa do pipeline.</summary>
public sealed class CommandLoggingDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    ILogger logger)
    : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
    where TResult : Result
{
    public async Task<TResult> HandleAsync(TCommand command, CancellationToken ct)
    {
        var name = typeof(TCommand).Name;
        using var activity = InventoryTelemetry.ActivitySource.StartActivity($"command {name}");
        activity?.SetTag("inventory.command", name);

        try
        {
            var result = await inner.HandleAsync(command, ct);

            activity?.SetTag("inventory.success", result.IsSuccess);
            if (result.IsFailure)
            {
                activity?.SetTag("inventory.error_code", result.Error.Code);
                logger.LogWarning("Command {Command} falhou: {ErrorCode} - {ErrorDescription}",
                    name, result.Error.Code, result.Error.Description);
            }
            else
            {
                logger.LogInformation("Command {Command} concluído", name);
            }

            return result;
        }
        catch (ConcurrencyConflictException ex)
        {
            // Esperado sob concorrência: o consumer tenta de novo. Não é erro.
            activity?.SetTag("inventory.concurrency_conflict", true);
            logger.LogWarning(ex, "Command {Command} teve conflito de concorrência", name);
            throw;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Command {Command} lançou exceção", name);
            throw;
        }
    }
}
