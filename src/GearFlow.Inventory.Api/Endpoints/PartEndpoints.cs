using GearFlow.Inventory.Api.Http;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Application.Parts;
using GearFlow.Inventory.Application.Parts.Commands.AddPartStock;
using GearFlow.Inventory.Application.Parts.Commands.CreatePart;
using GearFlow.Inventory.Application.Parts.Commands.DeletePart;
using GearFlow.Inventory.Application.Parts.Commands.UpdatePart;
using GearFlow.Inventory.Application.Parts.Queries.GetPartById;
using GearFlow.Inventory.Application.Parts.Queries.GetParts;

namespace GearFlow.Inventory.Api.Endpoints;

/// <summary>Endpoints de peças (estoque) — staff autenticado. As rotas são as mesmas do monólito.</summary>
public sealed class PartEndpoints : IEndpoint
{
    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/inventory/parts").WithTags("Inventory");

        group.MapGet("/", async (IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.Query(new GetPartsQuery(), ct)).ToOk())
            .WithSummary("Lista as peças em estoque.")
            .WithDescription("Retorna todas as peças com quantidade disponível/reservada e flag de estoque mínimo.")
            .Produces<IReadOnlyList<PartDto>>()
            .RequireAuthorization();

        group.MapGet("/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.Query(new GetPartByIdQuery(id), ct)).ToOk())
            .WithSummary("Busca uma peça por id.")
            .WithDescription("Retorna a peça com quantidade disponível/reservada e flag de estoque mínimo; 404 se não existir.")
            .Produces<PartDto>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        group.MapPost("/", async (CreatePartCommand command, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.Send(command, ct)).ToOk())
            .WithSummary("Cadastra uma peça.")
            .WithDescription("Cria uma peça com nome, número, fabricante, preço (centavos) e quantidade inicial.")
            .Produces<PartDto>()
            .ProducesValidationProblem()
            .RequireAuthorization();

        group.MapPut("/{id:guid}", async (Guid id, UpdatePartRequest body, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.Send(new UpdatePartCommand(
                    id, body.Name, body.Description, body.PartNumber, body.Manufacturer, body.PriceCents, body.Quantity), ct)).ToOk())
            .WithSummary("Atualiza os dados de uma peça.")
            .WithDescription("Altera nome, número, fabricante, preço e quantidade (disponível) da peça; 404 se não existir.")
            .Produces<PartDto>()
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem()
            .RequireAuthorization();

        group.MapDelete("/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.Send(new DeletePartCommand(id), ct)).ToNoContent())
            .WithSummary("Remove uma peça.")
            .WithDescription("Exclui a peça do estoque; 404 se não existir e 409 se houver unidades reservadas por OS em andamento.")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .RequireAuthorization();

        group.MapPatch("/{id:guid}/stock", async (Guid id, AddPartStockRequest body, IDispatcher dispatcher, CancellationToken ct) =>
                (await dispatcher.Send(new AddPartStockCommand(id, body.Quantity), ct)).ToOk())
            .WithSummary("Adiciona estoque a uma peça.")
            .WithDescription("Repõe a quantidade em estoque e publica o evento StockReplenished para o orquestrador retomar as OS que aguardam a peça.")
            .Produces<PartDto>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .ProducesValidationProblem()
            .RequireAuthorization();
    }

    public sealed record AddPartStockRequest(int Quantity);

    public sealed record UpdatePartRequest(
        string Name, string Description, string PartNumber, string Manufacturer, int PriceCents, int Quantity);
}
