namespace GearFlow.Inventory.Application.Abstractions;

/// <summary>
/// Envio de e-mail (SMTP; Mailpit em dev). Falha de envio lança exceção: o consumer que disparou o envio
/// deixa subir e o retry do MassTransit tenta de novo, sem afetar a reserva (o envio é assíncrono).
/// </summary>
public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct = default);
}
