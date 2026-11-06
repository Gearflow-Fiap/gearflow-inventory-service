using System.Diagnostics;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Application.Consumables.Commands.AddConsumableStock;
using GearFlow.Inventory.Application.Notifications.NotifyLowStock;
using GearFlow.Inventory.Application.Parts.Commands.AddPartStock;
using GearFlow.Inventory.Application.Reservations.Commands.ConsumeStock;
using GearFlow.Inventory.Application.Reservations.Commands.ReleaseStock;
using GearFlow.Inventory.Application.Reservations.Commands.ReserveStock;
using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Application.Behaviors;

/// <summary>
/// Métricas de negócio por command (volume por desfecho, duração do passo da Saga, falhas). Fica por dentro do
/// log/span e por fora da validação: mensagem malformada (que a validação recusa) também conta como falha.
/// </summary>
public sealed class BusinessMetricsDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    IBusinessMetrics metrics)
    : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
    where TResult : Result
{
    public async Task<TResult> HandleAsync(TCommand command, CancellationToken ct)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await inner.HandleAsync(command, ct);
            Record(command, result, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return result;
        }
        catch (ConcurrencyConflictException)
        {
            // Esperado sob concorrência: o consumer tenta de novo. Não é falha de negócio.
            throw;
        }
        catch
        {
            metrics.IntegrationError(Source());
            throw;
        }
    }

    private void Record(TCommand command, TResult result, double milliseconds)
    {
        switch (command)
        {
            case ReserveStockCommand:
                SagaStep("reserve", result, milliseconds);
                if (result is Result<ReserveStockOutcome> { IsSuccess: true } reserved)
                    metrics.Reservation(Outcome(reserved.Value.Status));
                break;
            case ReleaseStockCommand:
                SagaStep("release", result, milliseconds);
                if (result.IsSuccess) metrics.Release();
                break;
            case ConsumeStockCommand:
                SagaStep("consume", result, milliseconds);
                if (result.IsSuccess) metrics.Consumption();
                break;
            case AddPartStockCommand when result.IsSuccess:
                metrics.Replenishment("Part");
                break;
            case AddConsumableStockCommand when result.IsSuccess:
                metrics.Replenishment("Consumable");
                break;
            case NotifyLowStockCommand when result.IsSuccess:
                metrics.LowStockAlert();
                break;
        }
    }

    private void SagaStep(string step, TResult result, double milliseconds)
    {
        metrics.SagaStepDuration(step, milliseconds);
        if (result.IsFailure) metrics.IntegrationError(Source());
    }

    private static string Outcome(ReserveStockStatus status) => status switch
    {
        ReserveStockStatus.Reserved => "reserved",
        ReserveStockStatus.AlreadyReserved => "already_reserved",
        ReserveStockStatus.Insufficient => "insufficient",
        ReserveStockStatus.IgnoredAfterRelease => "ignored_after_release",
        _ => "unknown",
    };

    /// <summary>Ex.: <c>inventory.ReserveStock</c> (o nome do command sem o sufixo).</summary>
    private static string Source() => "inventory." + typeof(TCommand).Name.Replace("Command", string.Empty);
}
