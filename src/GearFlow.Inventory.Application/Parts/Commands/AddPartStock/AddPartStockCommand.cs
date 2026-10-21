using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Application.Parts.Commands.AddPartStock;

public sealed record AddPartStockCommand(Guid PartId, int Quantity) : ICommand<Result<PartDto>>;
