using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Domain.Repositories;
using GearFlow.Inventory.Domain.Stock;

namespace GearFlow.Inventory.Application.Parts.Commands.DeletePart;

internal sealed class DeletePartCommandHandler(IPartRepository parts, IUnitOfWork unitOfWork)
    : ICommandHandler<DeletePartCommand, Result>
{
    public async Task<Result> HandleAsync(DeletePartCommand command, CancellationToken ct)
    {
        var part = await parts.GetByIdAsync(PartId.From(command.PartId), ct);
        if (part is null)
            return Result.Failure(Error.NotFound("Part.NotFound", "Peça não encontrada."));

        var removable = part.EnsureCanBeRemoved();
        if (removable.IsFailure)
            return removable;

        parts.Remove(part);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
