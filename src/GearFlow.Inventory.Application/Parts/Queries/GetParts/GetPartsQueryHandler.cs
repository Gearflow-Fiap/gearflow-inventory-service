using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Domain.Repositories;

namespace GearFlow.Inventory.Application.Parts.Queries.GetParts;

internal sealed class GetPartsQueryHandler(IPartRepository parts)
    : IQueryHandler<GetPartsQuery, Result<IReadOnlyList<PartDto>>>
{
    public async Task<Result<IReadOnlyList<PartDto>>> HandleAsync(GetPartsQuery query, CancellationToken ct)
    {
        var all = await parts.ListAsync(ct);
        IReadOnlyList<PartDto> dtos = all.Select(PartDto.From).ToList();
        return Result.Success(dtos);
    }
}
