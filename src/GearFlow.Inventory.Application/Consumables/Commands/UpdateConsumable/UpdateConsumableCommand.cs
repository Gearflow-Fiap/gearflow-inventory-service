using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Application.Consumables.Commands.UpdateConsumable;

public sealed record UpdateConsumableCommand(Guid ConsumableId, string Name, int UnitPriceCents, decimal Quantity)
    : ICommand<Result<ConsumableDto>>;
