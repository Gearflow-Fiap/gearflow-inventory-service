using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;
using Microsoft.Extensions.Logging;

namespace GearFlow.Inventory.Application.Notifications.NotifyLowStock;

/// <summary>Mesmo texto do <c>LowStockAlertHandler</c> do monólito, agora configurável e disparado pela mensagem.</summary>
internal sealed class NotifyLowStockCommandHandler(
    IEmailSender email,
    StockAlertOptions options,
    ILogger<NotifyLowStockCommandHandler> logger)
    : ICommandHandler<NotifyLowStockCommand, Result>
{
    public async Task<Result> HandleAsync(NotifyLowStockCommand command, CancellationToken ct)
    {
        var subject = $"Estoque baixo: {command.ItemName}";
        var body = $"O item '{command.ItemName}' ({command.ItemType}) está em {command.Remaining} (mínimo {command.Minimum}).";

        await email.SendAsync(options.AlertEmail, subject, body, ct);

        logger.LogInformation("Aviso de estoque baixo enviado para {To}: {Item}", options.AlertEmail, command.ItemName);
        return Result.Success();
    }
}
