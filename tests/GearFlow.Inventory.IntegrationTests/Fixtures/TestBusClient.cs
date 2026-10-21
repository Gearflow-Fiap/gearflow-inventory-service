using System.Collections.Concurrent;
using GearFlow.Contracts;
using MassTransit;

namespace GearFlow.Inventory.IntegrationTests.Fixtures;

/// <summary>
/// O "outro lado" da Saga: um bus MassTransit independente, no mesmo RabbitMQ, que faz o papel do orquestrador
/// (publica os comandos) e captura os eventos que o Inventory responde. Cada evento recebido guarda o
/// <c>CorrelationId</c> do envelope, para provar que a correlação com a OS sobrevive ao Outbox.
/// </summary>
internal sealed class TestBusClient : IAsyncDisposable
{
    public sealed record Received(object Message, Guid? EnvelopeCorrelationId, Guid? EnvelopeMessageId);

    private readonly IBusControl _bus;

    public ConcurrentQueue<Received> Messages { get; }

    private TestBusClient(IBusControl bus, ConcurrentQueue<Received> messages)
    {
        _bus = bus;
        Messages = messages;
    }

    public IBus Bus => _bus;

    public static async Task<TestBusClient> StartAsync(string host, int port, string username, string password)
    {
        var messages = new ConcurrentQueue<Received>();

        var bus = MassTransit.Bus.Factory.CreateUsingRabbitMq(cfg =>
        {
            cfg.Host(host, (ushort)port, "/", h =>
            {
                h.Username(username);
                h.Password(password);
            });

            cfg.ReceiveEndpoint($"integration-test-{Guid.NewGuid():N}", endpoint =>
            {
                endpoint.AutoDelete = true;
                endpoint.Durable = false;

                Capture<StockReserved>(endpoint, messages);
                Capture<StockInsufficient>(endpoint, messages);
                Capture<StockReleased>(endpoint, messages);
                Capture<StockConsumed>(endpoint, messages);
                Capture<StockReplenished>(endpoint, messages);
                Capture<LowStockAlert>(endpoint, messages);
            });
        });

        await bus.StartAsync(new CancellationTokenSource(TimeSpan.FromSeconds(60)).Token);
        return new TestBusClient(bus, messages);
    }

    private static void Capture<T>(IReceiveEndpointConfigurator endpoint, ConcurrentQueue<Received> messages) where T : class =>
        endpoint.Handler<T>(context =>
        {
            messages.Enqueue(new Received(context.Message, context.CorrelationId, context.MessageId));
            return Task.CompletedTask;
        });

    public IReadOnlyList<Received> Of<T>(Func<T, bool> predicate) where T : class =>
        Messages.Where(r => r.Message is T m && predicate(m)).ToList();

    /// <summary>Espera chegar uma mensagem do tipo <typeparamref name="T"/> que atenda ao filtro.</summary>
    public async Task<Received> WaitForAsync<T>(Func<T, bool> predicate) where T : class
    {
        Received? found = null;
        await Eventually.UntilAsync(
            () =>
            {
                found = Of(predicate).FirstOrDefault();
                return Task.FromResult(found is not null);
            },
            $"mensagem {typeof(T).Name} não chegou");
        return found!;
    }

    public async ValueTask DisposeAsync() => await _bus.StopAsync();
}
