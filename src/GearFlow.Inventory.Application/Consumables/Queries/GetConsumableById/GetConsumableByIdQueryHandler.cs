using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Domain.Repositories;
using GearFlow.Inventory.Domain.Stock;

namespace GearFlow.Inventory.Application.Consumables.Queries.GetConsumableById;

internal sealed class GetConsumableByIdQueryHandler(IConsumableRepository consumables)
    : IQueryHandler<GetConsumableByIdQuery, Result<ConsumableDto>>
{
    public async Task<Result<ConsumableDto>> HandleAsync(GetConsumableByIdQuery query, CancellationToken ct)
    {
        var consumable = await consumables.GetByIdAsync(ConsumableId.From(query.ConsumableId), ct);
        if (consumable is null)
            return Result.Failure<ConsumableDto>(Error.NotFound("Consumable.NotFound", "Insumo não encontrado."));

        return Result.Success(ConsumableDto.From(consumable));
    }
}
