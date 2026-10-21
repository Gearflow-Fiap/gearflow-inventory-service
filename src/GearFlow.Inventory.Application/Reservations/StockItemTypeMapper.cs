using GearFlow.Contracts;
using GearFlow.Inventory.Domain.Stock;

namespace GearFlow.Inventory.Application.Reservations;

/// <summary>Conversão entre o enum do contrato (mensagens) e o do domínio, que não se referenciam.</summary>
internal static class StockItemTypeMapper
{
    public static StockItemType ToDomain(this InventoryItemType type) => type switch
    {
        InventoryItemType.Part => StockItemType.Part,
        InventoryItemType.Consumable => StockItemType.Consumable,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Tipo de item desconhecido."),
    };

    public static InventoryItemType ToContract(this StockItemType type) => type switch
    {
        StockItemType.Part => InventoryItemType.Part,
        StockItemType.Consumable => InventoryItemType.Consumable,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Tipo de item desconhecido."),
    };
}
