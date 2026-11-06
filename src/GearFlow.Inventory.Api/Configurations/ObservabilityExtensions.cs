using GearFlow.Inventory.Application.Behaviors;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace GearFlow.Inventory.Api.Configurations;

/// <summary>
/// OpenTelemetry (traces + métricas). Reaproveita a observabilidade da Fase 3 (New Relic): traces e
/// métricas saem por OTLP quando <c>Otlp:Endpoint</c> está configurado; <c>/metrics</c> (Prometheus) sempre existe.
/// Cada command/query vira um span (<see cref="InventoryTelemetry"/>), filho do trace da requisição HTTP.
/// Cada mensagem do RabbitMQ (publicar, consumir, retry) vira um span da fonte "MassTransit"; o MassTransit leva o
/// contexto do trace nos headers da mensagem, então o trace atravessa o broker e liga quem enviou o comando
/// (ex.: OS/Operation) ao consumer do Inventory, desde que o outro lado também propague o contexto.
/// </summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] // wiring de OpenTelemetry
public static class ObservabilityExtensions
{
    /// <summary>Meter de métricas de negócio, compartilhado com os outros serviços (dashboard do New Relic).</summary>
    public const string BusinessMeterName = "GearFlow.Business";

    /// <summary>
    /// <c>ActivitySource</c> e <c>Meter</c> que o MassTransit 8 publica (<c>DiagnosticHeaders.DefaultListenerName</c> e
    /// <c>InstrumentationOptions.MeterName</c>). Um teste unitário confere que é esse o nome, porque um erro de digitação
    /// aqui não quebra nada: o trace só deixa de passar pelo RabbitMQ.
    /// </summary>
    public const string MassTransitSourceName = "MassTransit";

    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder, string serviceName)
    {
        var otlpEndpoint = builder.Configuration["Otlp:Endpoint"];
        var serviceVersion = typeof(ObservabilityExtensions).Assembly.GetName().Version?.ToString() ?? "1.0.0";

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService(serviceName, serviceVersion: serviceVersion)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = builder.Environment.EnvironmentName,
                }))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(o => o.RecordException = true)
                    .AddHttpClientInstrumentation()
                    .AddSource(InventoryTelemetry.ActivitySourceName)
                    .AddSource(MassTransitSourceName);

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint));
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter(BusinessMeterName)
                    .AddMeter(MassTransitSourceName)   // consumidas, falhas e duração por consumer
                    .AddPrometheusExporter();

                // Delta (não Cumulative): evita somas duplicadas quando o New Relic agrega várias leituras.
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    metrics.AddOtlpExporter((exporterOptions, readerOptions) =>
                    {
                        exporterOptions.Endpoint = new Uri(otlpEndpoint);
                        readerOptions.TemporalityPreference = MetricReaderTemporalityPreference.Delta;
                    });
            });

        return builder;
    }

    /// <summary>Endpoint de scraping Prometheus em <c>/metrics</c>. Chamar depois do <c>Build()</c>.</summary>
    public static WebApplication MapObservability(this WebApplication app)
    {
        app.MapPrometheusScrapingEndpoint();
        return app;
    }
}
