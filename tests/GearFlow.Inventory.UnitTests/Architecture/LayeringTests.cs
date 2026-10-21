using System.Reflection;
using FluentAssertions;
using GearFlow.Inventory.Api;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Stock;
using GearFlow.Inventory.Infrastructure.Persistence;
using NetArchTest.Rules;

namespace GearFlow.Inventory.UnitTests.Architecture;

/// <summary>
/// Faz valer a direção de dependência (README): Api → Application → Domain e
/// Infrastructure → Application → Domain. O Domain é C# puro.
/// </summary>
public sealed class LayeringTests
{
    private const string DomainNs = "GearFlow.Inventory.Domain";
    private const string ApplicationNs = "GearFlow.Inventory.Application";
    private const string InfrastructureNs = "GearFlow.Inventory.Infrastructure";
    private const string ApiNs = "GearFlow.Inventory.Api";

    private static readonly Assembly Domain = typeof(Part).Assembly;
    private static readonly Assembly Application = typeof(IDispatcher).Assembly;
    private static readonly Assembly Infrastructure = typeof(InventoryDbContext).Assembly;
    private static readonly Assembly Api = typeof(IApiMarker).Assembly;

    private static void ShouldHaveNoViolations(TestResult result, string because) =>
        result.IsSuccessful.Should().BeTrue($"{because}. Violações: {string.Join(", ", result.FailingTypeNames ?? [])}");

    [Fact]
    public void Domain_is_pure()
    {
        var result = Types.InAssembly(Domain).Should()
            .NotHaveDependencyOnAny(
                ApplicationNs, InfrastructureNs, ApiNs, "GearFlow.Contracts",
                "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Npgsql", "FluentValidation", "MassTransit")
            .GetResult();

        ShouldHaveNoViolations(result, "o Domain não pode depender de outras camadas nem de frameworks");
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure_or_api_or_persistence_frameworks()
    {
        var result = Types.InAssembly(Application).Should()
            .NotHaveDependencyOnAny(
                InfrastructureNs, ApiNs, "Microsoft.EntityFrameworkCore", "Npgsql", "MassTransit", "Microsoft.AspNetCore")
            .GetResult();

        ShouldHaveNoViolations(result, "a Application só conhece abstrações (repositórios, IUnitOfWork, IMessagePublisher)");
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_the_api()
    {
        var result = Types.InAssembly(Infrastructure).Should().NotHaveDependencyOn(ApiNs).GetResult();

        ShouldHaveNoViolations(result, "a Infrastructure não conhece a Api");
    }

    [Fact]
    public void Command_and_query_handlers_are_internal_and_sealed()
    {
        var handlers = Types.InAssembly(Application).That()
            .AreClasses().And().HaveNameEndingWith("Handler").And().ResideInNamespaceStartingWith(ApplicationNs);

        ShouldHaveNoViolations(handlers.Should().BeSealed().GetResult(), "handlers devem ser sealed");
        ShouldHaveNoViolations(handlers.Should().NotBePublic().GetResult(), "handlers devem ser internal");
    }

    [Fact]
    public void Validators_are_internal_and_sealed()
    {
        var validators = Types.InAssembly(Application).That().AreClasses().And().HaveNameEndingWith("Validator");

        ShouldHaveNoViolations(validators.Should().BeSealed().GetResult(), "validators devem ser sealed");
        ShouldHaveNoViolations(validators.Should().NotBePublic().GetResult(), "validators devem ser internal");
    }

    [Fact]
    public void Infrastructure_implementations_are_internal()
    {
        var repositories = Types.InAssembly(Infrastructure).That()
            .AreClasses().And().HaveNameEndingWith("Repository");

        ShouldHaveNoViolations(repositories.Should().NotBePublic().GetResult(), "repositórios devem ser internal");
    }

    [Fact]
    public void Endpoints_do_not_reach_into_repositories_or_the_database()
    {
        var result = Types.InAssembly(Api).That().ImplementInterface(typeof(GearFlow.Inventory.Api.Endpoints.IEndpoint))
            .Should().NotHaveDependencyOnAny(
                "GearFlow.Inventory.Domain.Repositories", "GearFlow.Inventory.Infrastructure", "Microsoft.EntityFrameworkCore")
            .GetResult();

        ShouldHaveNoViolations(result, "endpoint só traduz HTTP → command/query via IDispatcher → HTTP");
    }
}
