using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Domain.Repositories;

namespace GearFlow.Inventory.Application.Consumables.Queries.GetConsumables;

internal sealed class GetConsumablesQueryHandler(IConsumableRepository consumables)
    : IQueryHandler<GetConsumablesQuery, Result<IReadOnlyList<ConsumableDto>>>
{
    public async Task<Result<IReadOnlyList<ConsumableDto>>> HandleAsync(GetConsumablesQuery query, CancellationToken ct)
    {
        var all = await consumables.ListAsync(ct);
        IReadOnlyList<ConsumableDto> dtos = all.Select(ConsumableDto.From).ToList();
        return Result.Success(dtos);
    }
}
