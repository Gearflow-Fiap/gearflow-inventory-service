using System.Net.Mail;
using GearFlow.Inventory.Application.Abstractions;

namespace GearFlow.Inventory.Infrastructure.Email;

/// <summary>Envio via SMTP, igual ao <c>SmtpEmailSender</c> do monólito. Falha de envio lança exceção (o consumer repete).</summary>
internal sealed class SmtpEmailSender(SmtpOptions options) : IEmailSender
{
    public async Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        using var client = new SmtpClient(options.Host, options.Port);
        using var message = new MailMessage(options.From, to, subject, body);
        await client.SendMailAsync(message, ct);
    }
}
