using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Application.Consumables.Commands.DeleteConsumable;

public sealed record DeleteConsumableCommand(Guid ConsumableId) : ICommand<Result>;
