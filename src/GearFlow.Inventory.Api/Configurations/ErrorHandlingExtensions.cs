using GearFlow.Inventory.Api.Middleware;

namespace GearFlow.Inventory.Api.Configurations;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] // wiring do pipeline
public static class ErrorHandlingExtensions
{
    /// <summary>ProblemDetails (RFC 9457) como formato único de erro, com traceId em toda resposta de problema.</summary>
    public static IServiceCollection AddErrorHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ctx =>
            {
                var traceId = System.Diagnostics.Activity.Current?.TraceId.ToString()
                    ?? (ctx.HttpContext.Items.TryGetValue("TraceId", out var cid)
                        ? cid?.ToString()
                        : ctx.HttpContext.TraceIdentifier);
                ctx.ProblemDetails.Extensions["traceId"] = traceId;
                ctx.HttpContext.Response.Headers["X-Trace-Id"] = traceId;
            };
        });
        return services;
    }

    public static IApplicationBuilder UseExceptionHandling(this IApplicationBuilder app) =>
        app.UseMiddleware<ExceptionHandlingMiddleware>();
}
