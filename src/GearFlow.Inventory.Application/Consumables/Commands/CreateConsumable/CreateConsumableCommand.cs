using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Application.Consumables.Commands.CreateConsumable;

public sealed record CreateConsumableCommand(string Name, int UnitPriceCents, decimal Quantity)
    : ICommand<Result<ConsumableDto>>;
