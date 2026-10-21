using System.Diagnostics;
using GearFlow.Inventory.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Serilog.Context;

namespace GearFlow.Inventory.Api.Middleware;

/// <summary>
/// Última barreira da API: exceção não tratada vira ProblemDetails no mesmo formato dos erros de negócio.
/// Conflito de concorrência (dois PATCH de estoque no mesmo item) vira 409, que o cliente pode repetir.
/// Também coloca TraceId/SpanId no contexto do log para correlacionar log e trace.
/// </summary>
public sealed class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger,
    IHostEnvironment environment,
    IProblemDetailsService problemDetailsService)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
        var spanId = Activity.Current?.SpanId.ToString();

        context.Items["TraceId"] = traceId;

        using (LogContext.PushProperty("TraceId", traceId))
        using (LogContext.PushProperty("SpanId", spanId))
        {
            try
            {
                await next(context);
            }
            catch (Exception ex)
            {
                if (context.Response.HasStarted)
                    throw;

                await WriteProblemAsync(context, ex, traceId);
            }
        }
    }

    private async Task WriteProblemAsync(HttpContext context, Exception ex, string traceId)
    {
        var conflict = ex is ConcurrencyConflictException;

        if (conflict)
            logger.LogWarning(ex, "Conflito de concorrência. TraceId: {TraceId}, Path: {Path}", traceId, context.Request.Path);
        else
            logger.LogError(ex,
                "Unhandled exception occurred. TraceId: {TraceId}, Path: {Path}, Method: {Method}",
                traceId, context.Request.Path, context.Request.Method);

        var status = conflict ? StatusCodes.Status409Conflict : StatusCodes.Status500InternalServerError;

        context.Response.Clear();
        context.Response.StatusCode = status;

        var problem = new ProblemDetails
        {
            Type = conflict
                ? "https://tools.ietf.org/html/rfc9110#section-15.5.10"
                : "https://tools.ietf.org/html/rfc9110#section-15.6.1",
            Title = conflict ? "Conflict" : "Internal Server Error",
            Status = status,
            Detail = conflict
                ? "O estoque foi alterado por outra operação ao mesmo tempo. Tente novamente."
                : environment.IsDevelopment() ? ex.Message : "Ocorreu um erro inesperado. Tente novamente mais tarde.",
            Instance = context.Request.Path,
        };
        problem.Extensions["code"] = conflict ? "Inventory.ConcurrencyConflict" : "Server.UnexpectedError";

        if (!conflict && environment.IsDevelopment())
            problem.Extensions["stackTrace"] = ex.StackTrace;

        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = problem,
            Exception = ex,
        });
    }
}
