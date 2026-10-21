using FluentAssertions;
using GearFlow.Inventory.Api.Http;
using GearFlow.Inventory.Domain.Core;
using Microsoft.AspNetCore.Http.HttpResults;

namespace GearFlow.Inventory.UnitTests.Api;

/// <summary>Tradução Result/Error → HTTP (ProblemDetails, RFC 9457). Os endpoints nunca montam o erro à mão.</summary>
public sealed class ResultExtensionsTests
{
    [Fact]
    public void ToOk_returns_200_with_the_value_on_success()
    {
        var response = Result.Success(42).ToOk();

        var ok = response.Should().BeOfType<Ok<int>>().Subject;
        ok.StatusCode.Should().Be(200);
        ok.Value.Should().Be(42);
    }

    [Fact]
    public void ToOk_without_value_returns_200_on_success()
    {
        Result.Success().ToOk().Should().BeOfType<Ok>().Which.StatusCode.Should().Be(200);
    }

    [Fact]
    public void ToNoContent_returns_204_on_success()
    {
        Result.Success().ToNoContent().Should().BeOfType<NoContent>();
    }

    [Fact]
    public void ToCreated_returns_201_with_location_on_success()
    {
        var created = Result.Success("x").ToCreated("/api/inventory/parts/1").Should().BeOfType<Created<string>>().Subject;

        created.StatusCode.Should().Be(201);
        created.Location.Should().Be("/api/inventory/parts/1");
    }

    [Theory]
    [InlineData(ErrorType.NotFound, 404, "Not Found")]
    [InlineData(ErrorType.Conflict, 409, "Conflict")]
    [InlineData(ErrorType.Forbidden, 403, "Forbidden")]
    [InlineData(ErrorType.Unauthorized, 401, "Unauthorized")]
    [InlineData(ErrorType.Failure, 400, "Bad Request")]
    public void Failure_maps_the_error_type_to_the_http_status(ErrorType type, int status, string title)
    {
        var error = new Error("Some.Code", "algo deu errado", type);

        var problem = Result.Failure(error).ToNoContent().Should().BeOfType<ProblemHttpResult>().Subject;

        problem.StatusCode.Should().Be(status);
        problem.ProblemDetails.Title.Should().Be(title);
        problem.ProblemDetails.Detail.Should().Be("algo deu errado");
        problem.ProblemDetails.Extensions["code"].Should().Be("Some.Code");
    }

    [Fact]
    public void Failure_of_a_typed_result_is_also_translated()
    {
        var problem = Result.Failure<int>(Error.NotFound("Part.NotFound", "Peça não encontrada.")).ToOk()
            .Should().BeOfType<ProblemHttpResult>().Subject;

        problem.StatusCode.Should().Be(404);
        problem.ProblemDetails.Extensions["code"].Should().Be("Part.NotFound");
    }

    [Fact]
    public void Validation_error_becomes_a_400_validation_problem_with_one_message_per_failure()
    {
        var error = Error.Validation("Validation.Failed", "A quantidade deve ser maior que zero.; O id é obrigatório.");

        var problem = Result.Failure(error).ToOk().Should().BeOfType<ValidationProblem>().Subject;

        problem.StatusCode.Should().Be(400);
        problem.ProblemDetails.Errors["request"].Should().Equal("A quantidade deve ser maior que zero.", "O id é obrigatório.");
        problem.ProblemDetails.Extensions["code"].Should().Be("Validation.Failed");
    }

    [Fact]
    public void Validation_error_uses_the_field_name_when_there_is_one()
    {
        var error = Error.Validation("Part.NameRequired", "Nome da peça é obrigatório.", field: "name");

        var problem = Result.Failure(error).ToOk().Should().BeOfType<ValidationProblem>().Subject;

        problem.ProblemDetails.Errors.Should().ContainKey("name");
    }
}
