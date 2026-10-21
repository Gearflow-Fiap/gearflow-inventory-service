using GearFlow.Contracts;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Application.Notifications.NotifyLowStock;

/// <summary>Avisa o estoquista por e-mail que um item ficou no/abaixo do mínimo (<c>LowStockAlert</c>).</summary>
public sealed record NotifyLowStockCommand(
    InventoryItemType ItemType, string ItemName, decimal Remaining, decimal Minimum) : ICommand<Result>
{
    public static NotifyLowStockCommand From(LowStockAlert message) =>
        new(message.ItemType, message.ItemName, message.Remaining, message.Minimum);
}
