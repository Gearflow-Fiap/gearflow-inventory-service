using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Domain.Repositories;
using GearFlow.Inventory.Domain.Stock;

namespace GearFlow.Inventory.Application.Consumables.Commands.DeleteConsumable;

internal sealed class DeleteConsumableCommandHandler(IConsumableRepository consumables, IUnitOfWork unitOfWork)
    : ICommandHandler<DeleteConsumableCommand, Result>
{
    public async Task<Result> HandleAsync(DeleteConsumableCommand command, CancellationToken ct)
    {
        var consumable = await consumables.GetByIdAsync(ConsumableId.From(command.ConsumableId), ct);
        if (consumable is null)
            return Result.Failure(Error.NotFound("Consumable.NotFound", "Insumo não encontrado."));

        var removable = consumable.EnsureCanBeRemoved();
        if (removable.IsFailure)
            return removable;

        consumables.Remove(consumable);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
