using GearFlow.Contracts;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Domain.Repositories;
using GearFlow.Inventory.Domain.Stock;

namespace GearFlow.Inventory.Application.Consumables.Commands.AddConsumableStock;

/// <summary>Repõe estoque de um insumo e publica <see cref="StockReplenished"/> (ver <c>AddPartStock</c>).</summary>
internal sealed class AddConsumableStockCommandHandler(
    IConsumableRepository consumables,
    IMessagePublisher publisher,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
    : ICommandHandler<AddConsumableStockCommand, Result<ConsumableDto>>
{
    public async Task<Result<ConsumableDto>> HandleAsync(AddConsumableStockCommand command, CancellationToken ct)
    {
        var consumable = await consumables.GetByIdAsync(ConsumableId.From(command.ConsumableId), ct);
        if (consumable is null)
            return Result.Failure<ConsumableDto>(Error.NotFound("Consumable.NotFound", "Insumo não encontrado."));

        var added = consumable.AddQuantity(command.Quantity);
        if (added.IsFailure)
            return Result.Failure<ConsumableDto>(added.Error);

        await publisher.PublishAsync(
            new StockReplenished(Guid.NewGuid(), clock.GetUtcNow().UtcDateTime, InventoryItemType.Consumable, consumable.Id.Value),
            ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(ConsumableDto.From(consumable));
    }
}
