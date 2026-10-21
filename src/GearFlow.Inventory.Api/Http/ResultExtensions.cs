using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Api.Http;

/// <summary>
/// Tradução central de <see cref="Result"/>/<see cref="Error"/> para HTTP no padrão ProblemDetails
/// (RFC 9457). O endpoint faz <c>result.ToOk()</c> em vez de montar <c>Results.BadRequest/NotFound</c>
/// à mão: o status vem do <see cref="ErrorType"/> do erro.
/// </summary>
public static class ResultExtensions
{
    /// <summary>200 com o valor no sucesso; ProblemDetails no erro.</summary>
    public static IResult ToOk<T>(this Result<T> result) =>
        result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();

    /// <summary>200 sem corpo no sucesso; ProblemDetails no erro.</summary>
    public static IResult ToOk(this Result result) =>
        result.IsSuccess ? TypedResults.Ok() : result.Error.ToProblem();

    /// <summary>204 no sucesso; ProblemDetails no erro. Para deletes.</summary>
    public static IResult ToNoContent(this Result result) =>
        result.IsSuccess ? TypedResults.NoContent() : result.Error.ToProblem();

    /// <summary>201 + Location no sucesso; ProblemDetails no erro.</summary>
    public static IResult ToCreated<T>(this Result<T> result, string location) =>
        result.IsSuccess ? TypedResults.Created(location, result.Value) : result.Error.ToProblem();

    /// <summary>Converte um <see cref="Error"/> no ProblemDetails correspondente.</summary>
    public static IResult ToProblem(this Error error)
    {
        var status = StatusFor(error);

        if (error.Type == ErrorType.Validation || error.Code.StartsWith("Validation.", StringComparison.Ordinal))
        {
            var messages = error.Description.Split("; ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    [error.Field ?? "request"] = messages.Length > 0 ? messages : [error.Description],
                },
                detail: error.Description,
                extensions: new Dictionary<string, object?> { ["code"] = error.Code });
        }

        return TypedResults.Problem(
            detail: error.Description,
            statusCode: status,
            title: TitleFor(status),
            extensions: new Dictionary<string, object?> { ["code"] = error.Code });
    }

    private static int StatusFor(Error error) => error.Type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        _ => StatusCodes.Status400BadRequest,
    };

    private static string TitleFor(int status) => status switch
    {
        StatusCodes.Status404NotFound => "Not Found",
        StatusCodes.Status403Forbidden => "Forbidden",
        StatusCodes.Status401Unauthorized => "Unauthorized",
        StatusCodes.Status409Conflict => "Conflict",
        _ => "Bad Request",
    };
}
