using GearFlow.Contracts;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Domain.Repositories;
using GearFlow.Inventory.Domain.Stock;

namespace GearFlow.Inventory.Application.Parts.Commands.AddPartStock;

/// <summary>
/// Repõe estoque de uma peça (<c>PATCH …/stock</c>). Publica <see cref="StockReplenished"/> para o
/// orquestrador reenviar <c>ReserveStock</c> às OS que aguardam a peça (auto-resume do monólito).
/// </summary>
internal sealed class AddPartStockCommandHandler(
    IPartRepository parts,
    IMessagePublisher publisher,
    IUnitOfWork unitOfWork,
    TimeProvider clock)
    : ICommandHandler<AddPartStockCommand, Result<PartDto>>
{
    public async Task<Result<PartDto>> HandleAsync(AddPartStockCommand command, CancellationToken ct)
    {
        var part = await parts.GetByIdAsync(PartId.From(command.PartId), ct);
        if (part is null)
            return Result.Failure<PartDto>(Error.NotFound("Part.NotFound", "Peça não encontrada."));

        var added = part.AddQuantity(command.Quantity);
        if (added.IsFailure)
            return Result.Failure<PartDto>(added.Error);

        // Publica antes do commit: com o Outbox a mensagem entra na mesma transação do saldo.
        await publisher.PublishAsync(
            new StockReplenished(Guid.NewGuid(), clock.GetUtcNow().UtcDateTime, InventoryItemType.Part, part.Id.Value),
            ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(PartDto.From(part));
    }
}
