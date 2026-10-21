using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Application.Parts.Queries.GetPartById;

public sealed record GetPartByIdQuery(Guid PartId) : IQuery<Result<PartDto>>;
