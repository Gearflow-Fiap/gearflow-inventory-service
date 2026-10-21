using GearFlow.Contracts;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Application.Notifications.NotifyStockShortage;

/// <summary>
/// Avisa o estoquista por e-mail que uma OS não conseguiu reservar o estoque (<c>StockInsufficient</c>), para
/// ele repor. Era o <c>StockMissingNotificationHandler</c> do monólito.
/// </summary>
public sealed record NotifyStockShortageCommand(Guid ServiceOrderId, IReadOnlyList<MissingStockItem> Missing)
    : ICommand<Result>
{
    public static NotifyStockShortageCommand From(StockInsufficient message) =>
        new(message.CorrelationId, message.Missing);
}
