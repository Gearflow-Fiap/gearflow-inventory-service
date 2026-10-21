namespace GearFlow.Inventory.Infrastructure.Email;

/// <summary>SMTP (Mailpit em dev). Seção <c>Smtp</c>: <c>Smtp__Host</c>, <c>Smtp__Port</c>, <c>Smtp__From</c>.</summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    public string Host { get; init; } = "localhost";
    public int Port { get; init; } = 1025;
    public string From { get; init; } = "no-reply@gearflow.com";
}
