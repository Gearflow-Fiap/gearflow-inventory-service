using FluentAssertions;
using GearFlow.Inventory.Application.Behaviors;
using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.UnitTests.Application;

public sealed class ResultFactoryTests
{
    private static readonly Error SomeError = Error.Validation("Validation.Failed", "falhou");

    [Fact]
    public void Creates_a_plain_failure()
    {
        var result = ResultFactory<Result>.Failure(SomeError);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SomeError);
    }

    [Fact]
    public void Creates_a_typed_failure()
    {
        var result = ResultFactory<Result<List<string>>>.Failure(SomeError);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(SomeError);
        var access = () => result.Value;
        access.Should().Throw<InvalidOperationException>();
    }
}
