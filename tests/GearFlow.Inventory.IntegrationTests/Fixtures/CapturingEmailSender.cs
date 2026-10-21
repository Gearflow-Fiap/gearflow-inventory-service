using System.Collections.Concurrent;
using GearFlow.Inventory.Application.Abstractions;

namespace GearFlow.Inventory.IntegrationTests.Fixtures;

/// <summary>Troca o SMTP real: guarda os e-mails enviados para os testes conferirem.</summary>
internal sealed class CapturingEmailSender : IEmailSender
{
    public sealed record Sent(string To, string Subject, string Body);

    public ConcurrentQueue<Sent> Emails { get; } = new();

    public Task SendAsync(string to, string subject, string body, CancellationToken ct = default)
    {
        Emails.Enqueue(new Sent(to, subject, body));
        return Task.CompletedTask;
    }
}
