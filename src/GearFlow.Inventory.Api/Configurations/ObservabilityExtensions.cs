using GearFlow.Inventory.Application.Behaviors;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace GearFlow.Inventory.Api.Configurations;

/// <summary>
/// OpenTelemetry (traces + métricas). Reaproveita a observabilidade da Fase 3 (New Relic): traces e
/// métricas saem por OTLP quando <c>Otlp:Endpoint</c> está configurado; <c>/metrics</c> (Prometheus) sempre existe.
/// Cada command/query vira um span (<see cref="InventoryTelemetry"/>), filho do trace da requisição HTTP.
/// O span da mensagem do RabbitMQ entra na fase 5 (fonte "MassTransit").
/// </summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] // wiring de OpenTelemetry
public static class ObservabilityExtensions
{
    /// <summary>Meter de métricas de negócio, compartilhado com os outros serviços (dashboard do New Relic).</summary>
    public const string BusinessMeterName = "GearFlow.Business";

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
                    .AddSource(InventoryTelemetry.ActivitySourceName);

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
