using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Application.Parts.Commands.UpdatePart;

public sealed record UpdatePartCommand(
    Guid PartId, string Name, string Description, string PartNumber, string Manufacturer, int PriceCents, int Quantity)
    : ICommand<Result<PartDto>>;
