using GearFlow.Inventory.Api.Http;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Application.Consumables;
using GearFlow.Inventory.Application.Consumables.Commands.AddConsumableStock;
using GearFlow.Inventory.Application.Consumables.Commands.CreateConsumable;
using GearFlow.Inventory.Application.Consumables.Commands.DeleteConsumable;
using GearFlow.Inventory.Application.Consumables.Commands.UpdateConsumable;
using GearFlow.Inventory.Application.Consumables.Queries.GetConsumableById;
using GearFlow.Inventory.Application.Consumables.Queries.GetConsumables;

namespace GearFlow.Inventory.Api.Endpoints;

/// <summary>Endpoints de insumos (estoque) — staff autenticado. As rotas são as mesmas do monólito.</summary>
public sealed class ConsumableEndpoints : IEndpoint
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/inventory/consumables").WithTags("Inventory");

        group.MapGet("/", async (IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.Query(new GetConsumablesQuery(), ct)).ToOk())
            .WithSummary("Lista os insumos em estoque.")
            .WithDescription("Retorna todos os insumos com quantidade disponível/reservada e flag de estoque mínimo.")
            .Produces<IReadOnlyList<ConsumableDto>>()
            .RequireAuthorization();

        group.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.Query(new GetConsumableByIdQuery(id), ct)).ToOk())
            .WithSummary("Busca um insumo por id.")
            .WithDescription("Retorna o insumo com quantidade disponível/reservada e flag de estoque mínimo; 404 se não existir.")
            .Produces<ConsumableDto>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        group.MapPost("/", async (CreateConsumableCommand command, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.Send(command, ct)).ToOk())
            .WithSummary("Cadastra um insumo.")
            .WithDescription("Cria um insumo com nome, preço unitário (centavos) e quantidade inicial.")
            .Produces<ConsumableDto>()
            .ProducesValidationProblem()
            .RequireAuthorization();

        group.MapPut("/{id:guid}", async (Guid id, UpdateConsumableRequest body, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.Send(new UpdateConsumableCommand(id, body.Name, body.UnitPriceCents, body.Quantity), ct)).ToOk())
            .WithSummary("Atualiza os dados de um insumo.")
            .WithDescription("Altera nome, preço unitário e quantidade (disponível) do insumo; 404 se não existir.")
            .Produces<ConsumableDto>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem()
            .RequireAuthorization();

        group.MapDelete("/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.Send(new DeleteConsumableCommand(id), ct)).ToNoContent())
            .WithSummary("Remove um insumo.")
            .WithDescription("Exclui o insumo do estoque; 404 se não existir e 409 se houver quantidade reservada por OS em andamento.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .RequireAuthorization();

        group.MapPatch("/{id:guid}/stock", async (Guid id, AddConsumableStockRequest body, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.Send(new AddConsumableStockCommand(id, body.Quantity), ct)).ToOk())
            .WithSummary("Adiciona estoque a um insumo.")
            .WithDescription("Repõe a quantidade em estoque e publica o evento StockReplenished para o orquestrador retomar as OS que aguardam o insumo.")
            .Produces<ConsumableDto>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .ProducesValidationProblem()
            .RequireAuthorization();
    }

    public sealed record AddConsumableStockRequest(decimal Quantity);

    public sealed record UpdateConsumableRequest(string Name, int UnitPriceCents, decimal Quantity);
}
