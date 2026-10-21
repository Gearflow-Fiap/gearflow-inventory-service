using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Application.Consumables.Queries.GetConsumables;

public sealed record GetConsumablesQuery : IQuery<Result<IReadOnlyList<ConsumableDto>>>;
