using System.Collections.Frozen;

namespace GearFlow.Contracts;

/// <summary>
/// Nomes de exchange dos eventos do Inventory: <c>inventory.&lt;fato&gt;</c> (prefixo = repo sem <c>gearflow-</c> e sem
/// <c>-service</c>; fato em kebab-case). Mesma convenção do Operation (<c>operation.diagnostic-finished</c>).
/// São nomes de exchange (fanout durável do MassTransit), não routing keys de um topic compartilhado. Namespace/URN
/// (<c>GearFlow.Contracts:&lt;Tipo&gt;</c>) e payload permanecem independentes do nome no broker. Comandos não entram aqui:
/// vão para filas <c>inventory-&lt;comando&gt;</c> (<c>QueueNames</c>).
/// </summary>
public static class InventoryEventNames
{
    public static IReadOnlyDictionary<Type, string> All { get; } = new Dictionary<Type, string>
    {
        [typeof(StockReserved)] = "inventory.stock-reserved",
        [typeof(StockInsufficient)] = "inventory.stock-insufficient",
        [typeof(StockReleased)] = "inventory.stock-released",
        [typeof(StockConsumed)] = "inventory.stock-consumed",
        [typeof(StockReplenished)] = "inventory.stock-replenished",
        [typeof(LowStockAlert)] = "inventory.low-stock-alert",
    }.ToFrozenDictionary();
}
