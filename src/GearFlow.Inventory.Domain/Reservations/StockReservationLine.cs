using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Domain.Stock;

namespace GearFlow.Inventory.Domain.Reservations;

/// <summary>
/// Uma linha da reserva: o item, a quantidade reservada e o nome no momento da reserva (snapshot,
/// para a reserva continuar legível mesmo se o item for renomeado).
/// </summary>
public sealed class StockReservationLine
{
    public StockItemType ItemType { get; private set; }
    public Guid ItemId { get; private set; }
    public string ItemName { get; private set; }
    public decimal Quantity { get; private set; }

    // Construtor do EF Core.
    private StockReservationLine()
    {
        ItemName = null!;
    }

    private StockReservationLine(StockItemType itemType, Guid itemId, string itemName, decimal quantity)
    {
        ItemType = itemType;
        ItemId = itemId;
        ItemName = itemName;
        Quantity = quantity;
    }

    /// <summary>Quantidade como inteiro. Só faz sentido para peça; <see cref="Create"/> garante que é inteira.</summary>
    public int WholeQuantity => (int)Quantity;

    public static Result<StockReservationLine> Create(StockItemType itemType, Guid itemId, string itemName, decimal quantity)
    {
        if (!Enum.IsDefined(itemType))
            return Result.Failure<StockReservationLine>(
                Error.Validation("Reservation.ItemTypeInvalid", "Tipo de item inválido."));
        if (itemId == Guid.Empty)
            return Result.Failure<StockReservationLine>(
                Error.Validation("Reservation.ItemIdRequired", "O id do item é obrigatório."));
        if (quantity <= 0)
            return Result.Failure<StockReservationLine>(
                Error.Validation("Reservation.QuantityInvalid", "A quantidade reservada deve ser maior que zero."));
        if (itemType == StockItemType.Part && quantity != decimal.Truncate(quantity))
            return Result.Failure<StockReservationLine>(
                Error.Validation("Reservation.PartQuantityMustBeInteger", "A quantidade de peças deve ser um número inteiro."));

        return Result.Success(new StockReservationLine(itemType, itemId, itemName, quantity));
    }
}
