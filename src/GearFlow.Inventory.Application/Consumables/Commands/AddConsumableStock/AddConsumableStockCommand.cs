using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Application.Consumables.Commands.AddConsumableStock;

public sealed record AddConsumableStockCommand(Guid ConsumableId, decimal Quantity)
    : ICommand<Result<ConsumableDto>>;
