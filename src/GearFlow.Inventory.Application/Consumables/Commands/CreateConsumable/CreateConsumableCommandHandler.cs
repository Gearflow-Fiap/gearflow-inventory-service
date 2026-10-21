using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Domain.Repositories;
using GearFlow.Inventory.Domain.Stock;

namespace GearFlow.Inventory.Application.Consumables.Commands.CreateConsumable;

internal sealed class CreateConsumableCommandHandler(IConsumableRepository consumables, IUnitOfWork unitOfWork)
    : ICommandHandler<CreateConsumableCommand, Result<ConsumableDto>>
{
    public async Task<Result<ConsumableDto>> HandleAsync(CreateConsumableCommand command, CancellationToken ct)
    {
        var created = Consumable.Create(command.Name, command.UnitPriceCents, command.Quantity);
        if (created.IsFailure)
            return Result.Failure<ConsumableDto>(created.Error);

        await consumables.AddAsync(created.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(ConsumableDto.From(created.Value));
    }
}
