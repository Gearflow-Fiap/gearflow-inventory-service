using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace GearFlow.Inventory.Api.Configurations;

/// <summary>
/// Probes do Kubernetes: <c>/health/live</c> (o processo está de pé) e <c>/health/ready</c> (o banco responde).
/// O RabbitMQ entra no readiness na fase 5.
/// </summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] // wiring de health
public static class HealthCheckExtensions
{
    public static IServiceCollection AddInventoryHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres") ?? string.Empty;

        services.AddHealthChecks()
            .AddCheck("Liveness", () => HealthCheckResult.Healthy("App is running"), tags: ["live"])
            .AddAsyncCheck("Postgres",
                async ct =>
                {
                    try
                    {
                        await using var connection = new NpgsqlConnection(connectionString);
                        await connection.OpenAsync(ct);
                        await using var command = new NpgsqlCommand("SELECT 1", connection);
                        await command.ExecuteScalarAsync(ct);
                        return HealthCheckResult.Healthy("PostgreSQL connection OK");
                    }
                    catch (Exception ex)
                    {
                        return HealthCheckResult.Unhealthy("PostgreSQL connection failed", ex);
                    }
                },
                tags: ["ready", "database"]);

        return services;
    }

    public static WebApplication MapInventoryHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live"),
            AllowCachingResponses = false,
            ResponseWriter = WriteSummary,
        });

        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            AllowCachingResponses = false,
            ResponseWriter = WriteSummary,
        });

        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            AllowCachingResponses = false,
            ResponseWriter = WriteDetailed,
        });

        return app;
    }

    private static Task WriteSummary(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
            }),
        });
    }

    private static Task WriteDetailed(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        var json = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.TotalMilliseconds,
                data = e.Value.Data,
            }),
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        return context.Response.WriteAsync(json);
    }
}
