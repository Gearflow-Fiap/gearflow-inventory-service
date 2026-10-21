using FluentAssertions;
using GearFlow.Inventory.Api;
using GearFlow.Inventory.Api.Endpoints;
using GearFlow.Inventory.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace GearFlow.Inventory.UnitTests.Architecture;

/// <summary>
/// Travas de contrato da API (herdadas do monólito): todo endpoint documenta summary/description (a
/// documentação é entregável do desafio) e exige autenticação. Roda sem subir o host: só mapeia as rotas.
/// </summary>
public sealed class EndpointConventionTests
{
    private static readonly List<RouteEndpoint> Endpoints = MapAll();

    private static List<RouteEndpoint> MapAll()
    {
        var builder = WebApplication.CreateBuilder();

        // O IDispatcher precisa estar registrado: é assim que o ASP.NET sabe que o parâmetro vem do DI
        // (e não do corpo da requisição) ao inferir os metadados do endpoint. Em produção ele vem da Infrastructure.
        builder.Services.AddScoped(_ => Substitute.For<IDispatcher>());

        var app = builder.Build();
        app.MapEndpoints(typeof(IApiMarker).Assembly);

        return ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToList();
    }

    private static string Describe(RouteEndpoint endpoint) =>
        $"{endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.FirstOrDefault()} {endpoint.RoutePattern.RawText}";

    [Fact]
    public void Every_endpoint_declares_summary_and_description()
    {
        Endpoints.Should().NotBeEmpty();

        var undocumented = Endpoints
            .Where(e => e.Metadata.GetMetadata<IEndpointSummaryMetadata>() is null
                        || e.Metadata.GetMetadata<IEndpointDescriptionMetadata>() is null)
            .Select(Describe)
            .ToList();

        undocumented.Should().BeEmpty("todo endpoint precisa de .WithSummary(...) e .WithDescription(...)");
    }

    [Fact]
    public void Every_endpoint_requires_authorization()
    {
        var anonymous = Endpoints
            .Where(e => !e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any())
            .Select(Describe)
            .ToList();

        anonymous.Should().BeEmpty("o estoque só é acessível por staff autenticado");
    }

    [Fact]
    public void The_routes_of_the_monolith_are_preserved_for_the_frontend_and_the_gateway()
    {
        var routes = Endpoints
            .Select(e => $"{e.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.First()} {e.RoutePattern.RawText!.TrimEnd('/')}")
            .ToList();

        foreach (var resource in new[] { "parts", "consumables" })
        {
            var root = $"/api/inventory/{resource}";
            routes.Should().Contain(
            [
                $"GET {root}",
                $"GET {root}/{{id:guid}}",
                $"POST {root}",
                $"PUT {root}/{{id:guid}}",
                $"DELETE {root}/{{id:guid}}",
                $"PATCH {root}/{{id:guid}}/stock",
            ]);
        }
    }

    [Fact]
    public void Reservation_lookup_is_read_only()
    {
        var reservationRoutes = Endpoints
            .Where(e => e.RoutePattern.RawText!.StartsWith("/api/inventory/reservations", StringComparison.Ordinal))
            .ToList();

        reservationRoutes.Should().ContainSingle();
        reservationRoutes.Single().Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Should().Equal("GET");
    }
}
