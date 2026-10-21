using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace GearFlow.Inventory.Api.Configurations;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] // wiring de logging
public static class SerilogExtensions
{
    public static WebApplicationBuilder AddSerilogLogging(this WebApplicationBuilder builder, string apiName)
    {
        var isDevelopment = builder.Environment.IsDevelopment();

        Log.Logger = ConfigureLogging(apiName, isDevelopment);
        builder.Host.UseSerilog();

        return builder;
    }

    private static Serilog.ILogger ConfigureLogging(string apiName, bool isDevelopment)
    {
        var logTemplate = "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] {Message:lj}{NewLine}{Exception}";
        // Seq é opt-in: só envia quando configurado.
        var seqUrl = Environment.GetEnvironmentVariable("Seq__ServerUrl");

        var logConfig = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", apiName)
            .Enrich.WithProperty("Environment", isDevelopment ? "Development" : "Production")
            .Enrich.WithProperty("MachineName", Environment.MachineName);

        if (isDevelopment)
        {
            logConfig
                .WriteTo.Console(outputTemplate: logTemplate)
                .WriteTo.File(
                    path: "logs/app-.txt",
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: logTemplate,
                    retainedFileCountLimit: 7);
        }
        else
        {
            // Logs estruturados em JSON: correlação por TraceId/SpanId via LogContext (usados no New Relic).
            logConfig.WriteTo.Console(new JsonFormatter());
        }

        if (!string.IsNullOrWhiteSpace(seqUrl))
            logConfig.WriteTo.Seq(seqUrl);

        return logConfig.CreateLogger();
    }
}
