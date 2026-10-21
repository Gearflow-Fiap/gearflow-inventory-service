using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Application.Parts.Commands.DeletePart;

public sealed record DeletePartCommand(Guid PartId) : ICommand<Result>;
