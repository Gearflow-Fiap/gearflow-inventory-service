using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Domain.Repositories;
using GearFlow.Inventory.Domain.Stock;

namespace GearFlow.Inventory.Application.Parts.Commands.UpdatePart;

internal sealed class UpdatePartCommandHandler(IPartRepository parts, IUnitOfWork unitOfWork)
    : ICommandHandler<UpdatePartCommand, Result<PartDto>>
{
    public async Task<Result<PartDto>> HandleAsync(UpdatePartCommand command, CancellationToken ct)
    {
        var part = await parts.GetByIdAsync(PartId.From(command.PartId), ct);
        if (part is null)
            return Result.Failure<PartDto>(Error.NotFound("Part.NotFound", "Peça não encontrada."));

        part.UpdateDetails(command.Name, command.Description, command.PartNumber,
            command.Manufacturer, command.PriceCents, command.Quantity);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(PartDto.From(part));
    }
}
