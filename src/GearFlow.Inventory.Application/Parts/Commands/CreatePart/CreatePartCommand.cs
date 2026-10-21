using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Application.Parts.Commands.CreatePart;

public sealed record CreatePartCommand(
    string Name, string Description, string PartNumber, string Manufacturer, int PriceCents, int Quantity)
    : ICommand<Result<PartDto>>;
