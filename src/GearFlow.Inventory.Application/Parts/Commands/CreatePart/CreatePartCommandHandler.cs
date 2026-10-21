using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Domain.Repositories;
using GearFlow.Inventory.Domain.Stock;

namespace GearFlow.Inventory.Application.Parts.Commands.CreatePart;

internal sealed class CreatePartCommandHandler(IPartRepository parts, IUnitOfWork unitOfWork)
    : ICommandHandler<CreatePartCommand, Result<PartDto>>
{
    public async Task<Result<PartDto>> HandleAsync(CreatePartCommand command, CancellationToken ct)
    {
        var created = Part.Create(command.Name, command.Description, command.PartNumber,
            command.Manufacturer, command.PriceCents, command.Quantity);
        if (created.IsFailure)
            return Result.Failure<PartDto>(created.Error);

        await parts.AddAsync(created.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(PartDto.From(created.Value));
    }
}
