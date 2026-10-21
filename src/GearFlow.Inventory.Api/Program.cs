using GearFlow.Inventory.Api;
using GearFlow.Inventory.Api.Configurations;
using GearFlow.Inventory.Api.Endpoints;
using GearFlow.Inventory.Api.OpenApi;
using GearFlow.Inventory.Application;
using GearFlow.Inventory.Infrastructure;
using GearFlow.Inventory.Infrastructure.Persistence;
using GearFlow.Inventory.Infrastructure.Seed;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Observabilidade + logging estruturado (reaproveita a Fase 3)
builder.AddSerilogLogging("GearFlow.Inventory.Api");
builder.AddObservability("GearFlow.Inventory.Api");

// Erro central (ProblemDetails RFC 9457) + autenticação
builder.Services.AddErrorHandling();
builder.Services.AddJwtAuthentication(builder.Configuration);

// Application + Infrastructure
builder.Services.AddInventoryApplication();
builder.Services.AddInventoryInfrastructure(builder.Configuration);
builder.Services.AddInventoryHealthChecks(builder.Configuration);

// OpenAPI / Scalar, com o esquema Bearer para o botão "Authorize"
builder.Services.AddOpenApi(options => options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

var app = builder.Build();

// Migrations no startup (idempotente) e seed de demonstração opt-in.
if (!app.Environment.IsEnvironment("Testing"))
{
    await using (var scope = app.Services.CreateAsyncScope())
        await scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Database.MigrateAsync();

    // Sempre em Development; nos demais ambientes só com Seed__EnableDevData=true (ex.: demo no cluster).
    var seedDevData = app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("Seed:EnableDevData");
    if (seedDevData)
        await InventorySeeder.SeedAsync(app.Services);
}

app.UseExceptionHandling();
app.UseAuthentication();
app.UseAuthorization();

app.MapObservability();            // GET /metrics
app.MapInventoryHealthChecks();    // /health, /health/live, /health/ready
app.MapEndpoints(typeof(IApiMarker).Assembly);

// A documentação da API é entregável do desafio (Swagger/Scalar): fica disponível em todos os ambientes.
app.MapOpenApi();                  // GET /openapi/v1.json
app.MapScalarApiReference();       // GET /scalar/v1

await app.RunAsync();

namespace GearFlow.Inventory.Api
{
    /// <summary>Marcador de assembly para descoberta de <see cref="IEndpoint"/> e testes de integração.</summary>
    public interface IApiMarker { }
}

/// <summary>Exposto para o <c>WebApplicationFactory&lt;Program&gt;</c> dos testes de integração.</summary>
public partial class Program { }
