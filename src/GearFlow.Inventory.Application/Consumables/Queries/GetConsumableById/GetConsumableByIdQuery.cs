using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Application.Consumables.Queries.GetConsumableById;

public sealed record GetConsumableByIdQuery(Guid ConsumableId) : IQuery<Result<ConsumableDto>>;
