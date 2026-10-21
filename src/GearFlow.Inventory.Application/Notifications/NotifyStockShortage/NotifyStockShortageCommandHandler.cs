using System.Text;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;
using Microsoft.Extensions.Logging;

namespace GearFlow.Inventory.Application.Notifications.NotifyStockShortage;

internal sealed class NotifyStockShortageCommandHandler(
    IEmailSender email,
    StockAlertOptions options,
    ILogger<NotifyStockShortageCommandHandler> logger)
    : ICommandHandler<NotifyStockShortageCommand, Result>
{
    public async Task<Result> HandleAsync(NotifyStockShortageCommand command, CancellationToken ct)
    {
        var subject = $"Estoque insuficiente para a OS {command.ServiceOrderId}";

        var body = new StringBuilder()
            .AppendLine($"A ordem de serviço {command.ServiceOrderId} não conseguiu reservar o estoque. Itens em falta:")
            .AppendLine();
        foreach (var item in command.Missing)
            body.AppendLine($"- {item.ItemName} ({item.ItemType}): pedido {item.Requested}, disponível {item.Available}");

        await email.SendAsync(options.AlertEmail, subject, body.ToString(), ct);

        logger.LogInformation("Aviso de estoque insuficiente da OS {ServiceOrderId} enviado para {To}",
            command.ServiceOrderId, options.AlertEmail);
        return Result.Success();
    }
}
