using System.Collections.Concurrent;
using System.Text.Json;
using GearFlow.Contracts;
using MassTransit;

namespace GearFlow.Inventory.SagaSimulator;

/// <summary>
/// Um bus MassTransit que faz o papel do orquestrador: publica comandos e captura todos os eventos que o Inventory
/// responde (numa fila temporária, só desta execução).
/// </summary>
internal sealed class Simulator : IAsyncDisposable
{
    public sealed record Reply(DateTime At, object Message, Guid? CorrelationId);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IBusControl _bus;
    private readonly ConcurrentQueue<Reply> _replies;

    private Simulator(IBusControl bus, ConcurrentQueue<Reply> replies)
    {
        _bus = bus;
        _replies = replies;
    }

    /// <summary>Chamado a cada evento recebido (para o modo <c>listen</c> imprimir na hora).</summary>
    public event Action<Reply>? OnReply;

    public static async Task<Simulator> StartAsync(ConnectionOptions connection)
    {
        var replies = new ConcurrentQueue<Reply>();
        Simulator? simulator = null;

        void Capture(object message, Guid? correlationId)
        {
            var reply = new Reply(DateTime.Now, message, correlationId);
            replies.Enqueue(reply);
            simulator?.OnReply?.Invoke(reply);
        }

        var bus = Bus.Factory.CreateUsingRabbitMq(cfg =>
        {
            cfg.Host(connection.Host, connection.Port, "/", h =>
            {
                h.Username(connection.User);
                h.Password(connection.Password);
            });

            // Fila temporária (some quando o simulador termina), ligada às exchanges dos eventos do Inventory.
            cfg.ReceiveEndpoint($"saga-simulator-{Guid.NewGuid():N}", endpoint =>
            {
                endpoint.AutoDelete = true;
                endpoint.Durable = false;

                endpoint.Handler<StockReserved>(c => { Capture(c.Message, c.CorrelationId); return Task.CompletedTask; });
                endpoint.Handler<StockInsufficient>(c => { Capture(c.Message, c.CorrelationId); return Task.CompletedTask; });
                endpoint.Handler<StockReleased>(c => { Capture(c.Message, c.CorrelationId); return Task.CompletedTask; });
                endpoint.Handler<StockConsumed>(c => { Capture(c.Message, c.CorrelationId); return Task.CompletedTask; });
                endpoint.Handler<StockReplenished>(c => { Capture(c.Message, c.CorrelationId); return Task.CompletedTask; });
                endpoint.Handler<LowStockAlert>(c => { Capture(c.Message, c.CorrelationId); return Task.CompletedTask; });
            });
        });

        try
        {
            await bus.StartAsync(new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Não consegui conectar no RabbitMQ em {connection.Host}:{connection.Port} (usuário '{connection.User}'). " +
                "O broker está de pé? (docker compose up -d)  Detalhe: " + ex.Message, ex);
        }

        simulator = new Simulator(bus, replies);
        return simulator;
    }

    // ---- Comandos da Saga -------------------------------------------------------

    public Task ReserveAsync(Guid order, IReadOnlyList<StockItemLine> items) =>
        _bus.Publish(new ReserveStock(order, Guid.NewGuid(), DateTime.UtcNow, items));

    public Task ReleaseAsync(Guid order, string reason) =>
        _bus.Publish(new ReleaseStock(order, Guid.NewGuid(), DateTime.UtcNow, reason));

    public Task ConsumeAsync(Guid order) =>
        _bus.Publish(new ConsumeStock(order, Guid.NewGuid(), DateTime.UtcNow));

    // ---- Respostas --------------------------------------------------------------

    /// <summary>Espera um evento do tipo <typeparamref name="T"/> da OS dada. Devolve <c>null</c> se não chegar a tempo.</summary>
    public async Task<T?> WaitForAsync<T>(Guid order, TimeSpan timeout) where T : class
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var match = _replies.FirstOrDefault(r => r.CorrelationId == order && r.Message is T);
            if (match is not null)
                return (T)match.Message;
            await Task.Delay(100);
        }

        return null;
    }

    public IReadOnlyList<Reply> RepliesFor(Guid order) => _replies.Where(r => r.CorrelationId == order).ToList();

    /// <summary>Tudo que chegou, inclusive eventos sem OS (<c>StockReplenished</c>, <c>LowStockAlert</c>).</summary>
    public IReadOnlyList<Reply> AllReplies() => _replies.ToList();

    public static string Describe(Reply reply) =>
        $"{reply.At:HH:mm:ss.fff}  <- {reply.Message.GetType().Name,-18} " +
        (reply.CorrelationId is { } id ? $"OS {id}  " : string.Empty) +
        JsonSerializer.Serialize(reply.Message, reply.Message.GetType(), Json);

    public async ValueTask DisposeAsync() => await _bus.StopAsync();
}

internal sealed record ConnectionOptions(string Host, ushort Port, string User, string Password);
