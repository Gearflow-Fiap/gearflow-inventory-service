using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Infrastructure.Messaging.Consumers;
using GearFlow.Inventory.Infrastructure.Persistence;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GearFlow.Inventory.Infrastructure.Messaging;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// RabbitMQ + MassTransit com Outbox/Inbox no EF Core (mesmo banco e mesma transação do estoque).
    ///
    /// <list type="bullet">
    /// <item><b>Outbox no bus</b> (<c>UseBusOutbox</c>): o que um endpoint HTTP publica (ex.: <c>StockReplenished</c> do
    /// PATCH de estoque) vai para a tabela <c>OutboxMessage</c> junto com o saldo e é entregue depois do commit.</item>
    /// <item><b>Outbox no consumer</b> (<c>UseEntityFrameworkOutbox</c>): o que um consumer publica só sai se a transação do
    /// consumer commitar; o Inbox (<c>InboxState</c>) descarta a mesma mensagem entregue duas vezes.</item>
    /// <item><b>Retry</b> só para falha transitória; <see cref="PermanentMessageException"/> vai direto para a DLQ.</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddInventoryMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>() ?? new RabbitMqOptions();

        services.AddScoped<IMessagePublisher, MassTransitMessagePublisher>();

        services.AddMassTransit(x =>
        {
            x.AddEntityFrameworkOutbox<InventoryDbContext>(o =>
            {
                o.UsePostgres();
                o.UseBusOutbox();
                o.QueryDelay = TimeSpan.FromSeconds(1);
            });

            x.AddConsumer<ReserveStockConsumer>();
            x.AddConsumer<ReleaseStockConsumer>();
            x.AddConsumer<ConsumeStockConsumer>();
            x.AddConsumer<LowStockAlertEmailConsumer>();
            x.AddConsumer<StockInsufficientEmailConsumer>();

            x.UsingRabbitMq((context, cfg) =>
            {
                cfg.ConfigureInventoryEventNames();

                cfg.Host(options.Host, options.Port, options.VirtualHost, host =>
                {
                    host.Username(options.Username);
                    host.Password(options.Password);
                });

                ConfigureEndpoint<ReserveStockConsumer>(cfg, context, QueueNames.ReserveStock, options);
                ConfigureEndpoint<ReleaseStockConsumer>(cfg, context, QueueNames.ReleaseStock, options);
                ConfigureEndpoint<ConsumeStockConsumer>(cfg, context, QueueNames.ConsumeStock, options);
                ConfigureEndpoint<LowStockAlertEmailConsumer>(cfg, context, QueueNames.LowStockAlertEmail, options);
                ConfigureEndpoint<StockInsufficientEmailConsumer>(cfg, context, QueueNames.StockInsufficientEmail, options);
            });
        });

        return services;
    }

    // Ordem do pipeline = ordem da configuração: retry por fora, outbox/inbox por dentro. Assim cada tentativa
    // abre uma transação nova do zero.
    private static void ConfigureEndpoint<TConsumer>(
        IRabbitMqBusFactoryConfigurator cfg, IBusRegistrationContext context, string queue, RabbitMqOptions options)
        where TConsumer : class, IConsumer
    {
        cfg.ReceiveEndpoint(queue, endpoint =>
        {
            endpoint.UseMessageRetry(retry =>
            {
                retry.Intervals(options.RetryIntervals());
                retry.Ignore<PermanentMessageException>();
            });
            endpoint.UseEntityFrameworkOutbox<InventoryDbContext>(context);
            endpoint.ConfigureConsumer<TConsumer>(context);
        });
    }
}
