namespace GearFlow.Inventory.Infrastructure.Messaging;

/// <summary>
/// Filas do serviço (convenção do Billing e do Operation: <c>&lt;serviço&gt;-&lt;comando&gt;</c>, ex.:
/// <c>operation-start-diagnostic</c>). O orquestrador pode usar <c>Send</c> para <c>queue:inventory-reserve-stock</c> ou
/// <c>Publish</c> do comando: as duas formas chegam aqui, porque cada fila também é ligada à exchange do tipo da
/// mensagem. A fila de erro (DLQ) é <c>&lt;fila&gt;_error</c>. Os <b>eventos</b> saem em exchanges
/// <c>inventory.&lt;fato&gt;</c> (<see cref="GearFlow.Contracts.InventoryEventNames"/>).
/// </summary>
public static class QueueNames
{
    // Comandos da Saga
    public const string ReserveStock = "inventory-reserve-stock";
    public const string ReleaseStock = "inventory-release-stock";
    public const string ConsumeStock = "inventory-consume-stock";

    // Eventos do próprio serviço que disparam e-mail (consumidos aqui mesmo, de forma assíncrona)
    public const string LowStockAlertEmail = "inventory-low-stock-alert-email";
    public const string StockInsufficientEmail = "inventory-stock-insufficient-email";
}
