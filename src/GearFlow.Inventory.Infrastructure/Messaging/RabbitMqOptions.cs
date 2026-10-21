namespace GearFlow.Inventory.Infrastructure.Messaging;

/// <summary>Conexão e política de retry do RabbitMQ (seção <c>RabbitMq</c>; variáveis <c>RabbitMq__Host</c> etc.).</summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; init; } = "localhost";
    public ushort Port { get; init; } = 5672;
    public string VirtualHost { get; init; } = "/";
    public string Username { get; init; } = "guest";
    public string Password { get; init; } = "guest";

    /// <summary>
    /// Intervalos entre as tentativas de um consumer que falhou por motivo transitório. Sem configuração vale o
    /// padrão do Billing: 3 tentativas (1s, 5s, 15s); depois a mensagem vai para a fila <c>*_error</c> (DLQ).
    /// <para>
    /// Anulável de propósito: o binder de configuração do .NET <b>anexa</b> os valores configurados a um array que já
    /// tenha valor padrão (<c>[1000, 5000, 15000]</c> + <c>RetryIntervalsMilliseconds__0=50</c> viraria quatro tentativas).
    /// </para>
    /// </summary>
    public int[]? RetryIntervalsMilliseconds { get; init; }

    internal static readonly int[] DefaultRetryIntervalsMilliseconds = [1000, 5000, 15000];

    internal TimeSpan[] RetryIntervals() =>
        (RetryIntervalsMilliseconds is { Length: > 0 } configured ? configured : DefaultRetryIntervalsMilliseconds)
            .Select(ms => TimeSpan.FromMilliseconds(ms))
            .ToArray();
}
