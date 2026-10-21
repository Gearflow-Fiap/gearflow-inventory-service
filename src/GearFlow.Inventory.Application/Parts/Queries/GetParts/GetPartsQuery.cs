using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Application.Parts.Queries.GetParts;

public sealed record GetPartsQuery : IQuery<Result<IReadOnlyList<PartDto>>>;
