using System.Diagnostics.Metrics;
using GearFlow.Inventory.Application.Abstractions;

namespace GearFlow.Inventory.Application.Behaviors;

/// <summary>
/// Implementação de <see cref="IBusinessMetrics"/> sobre o Meter <c>GearFlow.Business</c>, o mesmo nome que o monólito da
/// Fase 3 usava (todos os serviços exportam para ele, então um dashboard único enxerga todos). Singleton: os
/// instrumentos são criados uma vez por processo.
/// </summary>
public sealed class BusinessMetrics : IBusinessMetrics, IDisposable
{
    public const string MeterName = "GearFlow.Business";

    // Nomes dos instrumentos. Os NRQLs de infra/newrelic dependem deles (um teste unitário trava os valores).
    public const string ReservationsName = "inventory.reservations";
    public const string ReleasesName = "inventory.releases";
    public const string ConsumptionsName = "inventory.consumptions";
    public const string ReplenishmentsName = "inventory.replenishments";
    public const string LowStockAlertsName = "inventory.low_stock_alerts";
    public const string SagaStepDurationName = "inventory.saga.step.duration";
    public const string IntegrationErrorsName = "integration.errors";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _reservations;
    private readonly Counter<long> _releases;
    private readonly Counter<long> _consumptions;
    private readonly Counter<long> _replenishments;
    private readonly Counter<long> _lowStockAlerts;
    private readonly Histogram<double> _sagaStepDuration;
    private readonly Counter<long> _integrationErrors;

    public BusinessMetrics()
    {
        _reservations = _meter.CreateCounter<long>(
            ReservationsName, description: "Reservas de estoque pedidas pela Saga, por desfecho.");
        _releases = _meter.CreateCounter<long>(
            ReleasesName, description: "Reservas liberadas (compensação da Saga).");
        _consumptions = _meter.CreateCounter<long>(
            ConsumptionsName, description: "Baixas definitivas de estoque (OS executada).");
        _replenishments = _meter.CreateCounter<long>(
            ReplenishmentsName, description: "Reposições de estoque feitas pela API.");
        _lowStockAlerts = _meter.CreateCounter<long>(
            LowStockAlertsName, description: "Alertas de estoque baixo processados.");
        _sagaStepDuration = _meter.CreateHistogram<double>(
            SagaStepDurationName, unit: "ms", description: "Duração de cada passo da Saga no Inventory.");
        _integrationErrors = _meter.CreateCounter<long>(
            IntegrationErrorsName, description: "Falhas de processamento de comandos (Saga) e exceções inesperadas.");
    }

    /// <summary>O Meter desta instância. Os testes filtram por ele: o nome do Meter é global ao processo.</summary>
    internal Meter Meter => _meter;

    public void Reservation(string outcome) =>
        _reservations.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

    public void Release() => _releases.Add(1);

    public void Consumption() => _consumptions.Add(1);

    public void Replenishment(string itemType) =>
        _replenishments.Add(1, new KeyValuePair<string, object?>("item_type", itemType));

    public void LowStockAlert() => _lowStockAlerts.Add(1);

    public void SagaStepDuration(string step, double milliseconds) =>
        _sagaStepDuration.Record(milliseconds, new KeyValuePair<string, object?>("step", step));

    public void IntegrationError(string source) =>
        _integrationErrors.Add(1, new KeyValuePair<string, object?>("source", source));

    public void Dispose() => _meter.Dispose();
}
