using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Domain.Repositories;
using GearFlow.Inventory.Domain.Stock;

namespace GearFlow.Inventory.Application.Parts.Queries.GetPartById;

internal sealed class GetPartByIdQueryHandler(IPartRepository parts)
    : IQueryHandler<GetPartByIdQuery, Result<PartDto>>
{
    public async Task<Result<PartDto>> HandleAsync(GetPartByIdQuery query, CancellationToken ct)
    {
        var part = await parts.GetByIdAsync(PartId.From(query.PartId), ct);
        if (part is null)
            return Result.Failure<PartDto>(Error.NotFound("Part.NotFound", "Peça não encontrada."));

        return Result.Success(PartDto.From(part));
    }
}
