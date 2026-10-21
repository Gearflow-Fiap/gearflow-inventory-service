using FluentAssertions;
using GearFlow.Inventory.Api.Middleware;
using GearFlow.Inventory.Application.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace GearFlow.Inventory.UnitTests.Api;

public sealed class ExceptionHandlingMiddlewareTests
{
    private readonly IProblemDetailsService _problems = Substitute.For<IProblemDetailsService>();
    private readonly IHostEnvironment _environment = Substitute.For<IHostEnvironment>();
    private ProblemDetailsContext? _written;

    public ExceptionHandlingMiddlewareTests()
    {
        _environment.EnvironmentName.Returns(Environments.Production);
        _problems.WriteAsync(Arg.Do<ProblemDetailsContext>(c => _written = c)).Returns(ValueTask.CompletedTask);
    }

    private async Task<HttpContext> RunAsync(RequestDelegate next)
    {
        var middleware = new ExceptionHandlingMiddleware(
            next, NullLogger<ExceptionHandlingMiddleware>.Instance, _environment, _problems);
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/inventory/parts/1/stock";

        await middleware.InvokeAsync(context);
        return context;
    }

    [Fact]
    public async Task Passes_through_when_nothing_throws()
    {
        var context = await RunAsync(_ => Task.CompletedTask);

        context.Response.StatusCode.Should().Be(200);
        _written.Should().BeNull();
        context.Items.Should().ContainKey("TraceId");
    }

    [Fact]
    public async Task Concurrency_conflict_becomes_409_that_the_client_can_retry()
    {
        var context = await RunAsync(_ => throw new ConcurrencyConflictException("outro processo alterou o saldo"));

        context.Response.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        var problem = _written!.ProblemDetails;
        problem.Status.Should().Be(409);
        problem.Title.Should().Be("Conflict");
        problem.Extensions["code"].Should().Be("Inventory.ConcurrencyConflict");
        problem.Instance.Should().Be("/api/inventory/parts/1/stock");
    }

    [Fact]
    public async Task Unexpected_exception_becomes_500_without_leaking_details_in_production()
    {
        var context = await RunAsync(_ => throw new InvalidOperationException("segredo interno"));

        context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
        var problem = _written!.ProblemDetails;
        problem.Extensions["code"].Should().Be("Server.UnexpectedError");
        problem.Detail.Should().NotContain("segredo interno");
        problem.Extensions.Should().NotContainKey("stackTrace");
    }

    [Fact]
    public async Task Unexpected_exception_exposes_message_and_stack_trace_in_development()
    {
        _environment.EnvironmentName.Returns(Environments.Development);

        await RunAsync(_ => throw new InvalidOperationException("detalhe para o dev"));

        var problem = _written!.ProblemDetails;
        problem.Detail.Should().Be("detalhe para o dev");
        problem.Extensions.Should().ContainKey("stackTrace");
    }

    [Fact]
    public async Task Rethrows_when_the_response_has_already_started()
    {
        var middleware = new ExceptionHandlingMiddleware(
            _ => throw new InvalidOperationException("tarde demais"),
            NullLogger<ExceptionHandlingMiddleware>.Instance, _environment, _problems);
        var context = new DefaultHttpContext();
        context.Features.Set<Microsoft.AspNetCore.Http.Features.IHttpResponseFeature>(new StartedResponseFeature());

        var act = () => middleware.InvokeAsync(context);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("tarde demais");
        _written.Should().BeNull();
    }

    private sealed class StartedResponseFeature : Microsoft.AspNetCore.Http.Features.IHttpResponseFeature
    {
        public int StatusCode { get; set; } = 200;
        public string? ReasonPhrase { get; set; }
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public Stream Body { get; set; } = Stream.Null;
        public bool HasStarted => true;
        public void OnStarting(Func<object, Task> callback, object state) { }
        public void OnCompleted(Func<object, Task> callback, object state) { }
    }
}
