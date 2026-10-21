using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Domain.Repositories;
using GearFlow.Inventory.Domain.Stock;

namespace GearFlow.Inventory.Application.Consumables.Commands.UpdateConsumable;

internal sealed class UpdateConsumableCommandHandler(IConsumableRepository consumables, IUnitOfWork unitOfWork)
    : ICommandHandler<UpdateConsumableCommand, Result<ConsumableDto>>
{
    public async Task<Result<ConsumableDto>> HandleAsync(UpdateConsumableCommand command, CancellationToken ct)
    {
        var consumable = await consumables.GetByIdAsync(ConsumableId.From(command.ConsumableId), ct);
        if (consumable is null)
            return Result.Failure<ConsumableDto>(Error.NotFound("Consumable.NotFound", "Insumo não encontrado."));

        consumable.UpdateDetails(command.Name, command.UnitPriceCents, command.Quantity);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(ConsumableDto.From(consumable));
    }
}
