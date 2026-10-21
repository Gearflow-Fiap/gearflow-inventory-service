using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GearFlow.Contracts;
using GearFlow.Inventory.Application.Consumables;
using GearFlow.Inventory.Application.Parts;
using GearFlow.Inventory.IntegrationTests.Fixtures;

namespace GearFlow.Inventory.IntegrationTests.Rest;

/// <summary>
/// O contrato REST que o frontend usa (as 12 rotas de peças e insumos do monólito + a consulta de reserva),
/// de ponta a ponta: JWT, binding do JSON, handler, Postgres real e ProblemDetails nos erros.
/// </summary>
[Collection(InfrastructureCollection.Name)]
public sealed class RestApiTests(InfrastructureFixture fx) : IAsyncLifetime
{
    public Task InitializeAsync()
    {
        fx.Api.CreateClient().Dispose(); // garante a API (e os consumers) no ar
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpClient Client()
    {
        var client = fx.Api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestTokens.Staff());
        return client;
    }

    private static async Task<string> ProblemCode(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        return problem.GetProperty("code").GetString()!;
    }

    // ---- Peças ---------------------------------------------------------------

    [Fact]
    public async Task Parts_full_crud_cycle()
    {
        using var client = Client();
        var partNumber = $"PN-{Guid.NewGuid():N}"[..12];

        // POST
        var created = await client.PostAsJsonAsync("/api/inventory/parts", new
        {
            name = "Filtro de óleo", description = "Motores 1.0", partNumber, manufacturer = "Fram", priceCents = 3500, quantity = 40,
        });
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        var part = (await created.Content.ReadFromJsonAsync<PartDto>())!;
        part.Id.Should().NotBeEmpty();
        part.Quantity.Should().Be(40);
        part.ReservedQuantity.Should().Be(0);
        part.BelowMinimum.Should().BeFalse();

        // GET by id e lista
        (await client.GetFromJsonAsync<PartDto>($"/api/inventory/parts/{part.Id}"))!.PartNumber.Should().Be(partNumber);
        (await client.GetFromJsonAsync<List<PartDto>>("/api/inventory/parts"))!.Should().Contain(p => p.Id == part.Id);

        // PUT
        var updated = await client.PutAsJsonAsync($"/api/inventory/parts/{part.Id}", new
        {
            name = "Filtro de óleo premium", description = "Motores 1.0", partNumber, manufacturer = "Fram", priceCents = 4200, quantity = 3,
        });
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        var afterUpdate = (await updated.Content.ReadFromJsonAsync<PartDto>())!;
        afterUpdate.Name.Should().Be("Filtro de óleo premium");
        afterUpdate.PriceCents.Should().Be(4200);
        afterUpdate.BelowMinimum.Should().BeTrue("3 unidades estão abaixo do mínimo de 5");

        // DELETE
        (await client.DeleteAsync($"/api/inventory/parts/{part.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var gone = await client.GetAsync($"/api/inventory/parts/{part.Id}");
        gone.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ProblemCode(gone)).Should().Be("Part.NotFound");
    }

    [Fact]
    public async Task Creating_a_part_with_invalid_data_returns_a_400_validation_problem_with_the_error_code()
    {
        using var client = Client();

        var response = await client.PostAsJsonAsync("/api/inventory/parts", new
        {
            name = "", description = "x", partNumber = "PN", manufacturer = "M", priceCents = 100, quantity = 1,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCode(response)).Should().Be("Part.NameRequired");
    }

    [Fact]
    public async Task Patching_the_stock_adds_to_the_available_quantity()
    {
        var partId = await fx.SeedPartAsync(2);
        using var client = Client();

        var response = await client.PatchAsJsonAsync($"/api/inventory/parts/{partId}/stock", new { quantity = 8 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<PartDto>())!.Quantity.Should().Be(10);
    }

    [Fact]
    public async Task Patching_the_stock_with_an_invalid_quantity_is_rejected()
    {
        var partId = await fx.SeedPartAsync(2);
        using var client = Client();

        var response = await client.PatchAsJsonAsync($"/api/inventory/parts/{partId}/stock", new { quantity = 0 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemCode(response)).Should().Be("Part.AddQuantityInvalid");
    }

    [Fact]
    public async Task Unknown_part_returns_404_with_a_problem_code_on_every_verb()
    {
        using var client = Client();
        var id = Guid.NewGuid();

        foreach (var response in new[]
        {
            await client.GetAsync($"/api/inventory/parts/{id}"),
            await client.PutAsJsonAsync($"/api/inventory/parts/{id}", new { name = "x", description = "d", partNumber = "PN", manufacturer = "M", priceCents = 1, quantity = 1 }),
            await client.DeleteAsync($"/api/inventory/parts/{id}"),
            await client.PatchAsJsonAsync($"/api/inventory/parts/{id}/stock", new { quantity = 1 }),
        })
        {
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await ProblemCode(response)).Should().Be("Part.NotFound");
        }
    }

    [Fact]
    public async Task A_part_reserved_by_a_service_order_cannot_be_deleted()
    {
        var partId = await fx.SeedPartAsync(10);
        var serviceOrderId = Guid.NewGuid();
        await fx.Bus.Bus.Publish(new ReserveStock(serviceOrderId, Guid.NewGuid(), DateTime.UtcNow,
            [new StockItemLine(InventoryItemType.Part, partId, 2)]));
        await fx.Bus.WaitForAsync<StockReserved>(m => m.CorrelationId == serviceOrderId);
        using var client = Client();

        var response = await client.DeleteAsync($"/api/inventory/parts/{partId}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCode(response)).Should().Be("Part.HasActiveReservations");
        (await client.GetFromJsonAsync<PartDto>($"/api/inventory/parts/{partId}"))!.ReservedQuantity.Should().Be(2);
    }

    // ---- Insumos -------------------------------------------------------------

    [Fact]
    public async Task Consumables_full_crud_cycle_with_fractional_quantities()
    {
        using var client = Client();

        var created = await client.PostAsJsonAsync("/api/inventory/consumables",
            new { name = $"Óleo {Guid.NewGuid():N}", unitPriceCents = 4500, quantity = 80.5m });
        created.StatusCode.Should().Be(HttpStatusCode.OK);
        var consumable = (await created.Content.ReadFromJsonAsync<ConsumableDto>())!;
        consumable.Quantity.Should().Be(80.5m);

        (await client.GetFromJsonAsync<ConsumableDto>($"/api/inventory/consumables/{consumable.Id}"))!.UnitPriceCents.Should().Be(4500);
        (await client.GetFromJsonAsync<List<ConsumableDto>>("/api/inventory/consumables"))!.Should().Contain(c => c.Id == consumable.Id);

        var patched = await client.PatchAsJsonAsync($"/api/inventory/consumables/{consumable.Id}/stock", new { quantity = 2.25m });
        (await patched.Content.ReadFromJsonAsync<ConsumableDto>())!.Quantity.Should().Be(82.75m);

        var updated = await client.PutAsJsonAsync($"/api/inventory/consumables/{consumable.Id}",
            new { name = "Óleo 15W40", unitPriceCents = 3200, quantity = 4m });
        var afterUpdate = (await updated.Content.ReadFromJsonAsync<ConsumableDto>())!;
        afterUpdate.Name.Should().Be("Óleo 15W40");
        afterUpdate.BelowMinimum.Should().BeTrue();

        (await client.DeleteAsync($"/api/inventory/consumables/{consumable.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var gone = await client.GetAsync($"/api/inventory/consumables/{consumable.Id}");
        gone.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ProblemCode(gone)).Should().Be("Consumable.NotFound");
    }

    [Fact]
    public async Task A_consumable_reserved_by_a_service_order_cannot_be_deleted()
    {
        var oilId = await fx.SeedConsumableAsync(10m);
        var serviceOrderId = Guid.NewGuid();
        await fx.Bus.Bus.Publish(new ReserveStock(serviceOrderId, Guid.NewGuid(), DateTime.UtcNow,
            [new StockItemLine(InventoryItemType.Consumable, oilId, 1.5m)]));
        await fx.Bus.WaitForAsync<StockReserved>(m => m.CorrelationId == serviceOrderId);
        using var client = Client();

        var response = await client.DeleteAsync($"/api/inventory/consumables/{oilId}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemCode(response)).Should().Be("Consumable.HasActiveReservations");
    }

    // ---- Reserva (consulta) --------------------------------------------------

    [Fact]
    public async Task The_reservation_of_a_service_order_is_readable_with_enums_as_text()
    {
        var partId = await fx.SeedPartAsync(10, "Pastilha de teste");
        var serviceOrderId = Guid.NewGuid();
        await fx.Bus.Bus.Publish(new ReserveStock(serviceOrderId, Guid.NewGuid(), DateTime.UtcNow,
            [new StockItemLine(InventoryItemType.Part, partId, 3)]));
        await fx.Bus.WaitForAsync<StockReserved>(m => m.CorrelationId == serviceOrderId);
        using var client = Client();

        var response = await client.GetAsync($"/api/inventory/reservations/service-order/{serviceOrderId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("serviceOrderId").GetGuid().Should().Be(serviceOrderId);
        json.GetProperty("status").GetString().Should().Be("Reserved");
        var line = json.GetProperty("lines").EnumerateArray().Single();
        line.GetProperty("itemType").GetString().Should().Be("Part");
        line.GetProperty("itemName").GetString().Should().Be("Pastilha de teste");
        line.GetProperty("quantity").GetDecimal().Should().Be(3m);
    }

    [Fact]
    public async Task A_service_order_without_reservation_returns_404()
    {
        using var client = Client();

        var response = await client.GetAsync($"/api/inventory/reservations/service-order/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ProblemCode(response)).Should().Be("Reservation.NotFound");
    }

    // ---- Documentação e segurança ----------------------------------------------

    [Fact]
    public async Task The_openapi_document_lists_all_thirteen_operations_and_the_bearer_scheme()
    {
        using var client = fx.Api.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        var operations = doc.GetProperty("paths").EnumerateObject()
            .SelectMany(path => path.Value.EnumerateObject().Select(verb => $"{verb.Name.ToUpperInvariant()} {path.Name}"))
            .ToList();
        operations.Should().HaveCount(13);
        operations.Should().Contain("GET /api/inventory/reservations/service-order/{serviceOrderId}");
        doc.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("/api/inventory/parts")]
    [InlineData("/api/inventory/consumables")]
    [InlineData("/api/inventory/reservations/service-order/3fa85f64-5717-4562-b3fc-2c963f66afa6")]
    public async Task Every_protected_route_rejects_a_missing_or_forged_token(string route)
    {
        using var anonymous = fx.Api.CreateClient();
        using var forged = fx.Api.CreateClient();
        forged.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");

        (await anonymous.GetAsync(route)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await forged.GetAsync(route)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
