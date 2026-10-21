using System.Diagnostics;

namespace GearFlow.Inventory.Application.Behaviors;

/// <summary>
/// Fonte de spans do serviço: cada command/query vira um span filho do trace da requisição HTTP ou da
/// mensagem do RabbitMQ. A Api registra o nome com <c>AddSource(InventoryTelemetry.ActivitySourceName)</c>.
/// </summary>
public static class InventoryTelemetry
{
    public const string ActivitySourceName = "GearFlow.Inventory";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
}
