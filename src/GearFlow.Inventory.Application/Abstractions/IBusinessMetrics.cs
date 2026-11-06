namespace GearFlow.Inventory.Application.Abstractions;

/// <summary>
/// Métricas de negócio do estoque (mesmo papel do <c>IBusinessMetrics</c> do monólito da Fase 3). Saem pelo Meter
/// compartilhado <c>GearFlow.Business</c>, por OTLP para o New Relic, e alimentam o dashboard e os alertas de
/// <c>infra/newrelic</c>. Os nomes dos instrumentos são contrato com esses NRQLs: não renomeie sem atualizar os dois.
/// </summary>
public interface IBusinessMetrics
{
    /// <summary>Desfecho de um <c>ReserveStock</c> (<c>inventory.reservations{outcome}</c>): reserved, already_reserved, insufficient ou ignored_after_release.</summary>
    void Reservation(string outcome);

    /// <summary>Compensação concluída (<c>inventory.releases</c>).</summary>
    void Release();

    /// <summary>Baixa definitiva concluída (<c>inventory.consumptions</c>).</summary>
    void Consumption();

    /// <summary>Reposição de estoque pela API (<c>inventory.replenishments{item_type}</c>).</summary>
    void Replenishment(string itemType);

    /// <summary>Alerta de estoque baixo processado (<c>inventory.low_stock_alerts</c>).</summary>
    void LowStockAlert();

    /// <summary>Tempo de um passo da Saga em ms (<c>inventory.saga.step.duration{step}</c>): reserve, release ou consume.</summary>
    void SagaStepDuration(string step, double milliseconds);

    /// <summary>
    /// Falha de processamento (<c>integration.errors{source}</c>, o mesmo nome do monólito, então o alerta da Fase 3 vale
    /// aqui): comando da Saga que falhou ou exceção inesperada. Erro de cliente da REST (400/404/409) não conta.
    /// </summary>
    void IntegrationError(string source);
}
