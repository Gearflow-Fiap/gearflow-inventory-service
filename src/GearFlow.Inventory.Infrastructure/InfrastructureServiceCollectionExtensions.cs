using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Application.Notifications;
using GearFlow.Inventory.Domain.Repositories;
using GearFlow.Inventory.Infrastructure.Dispatching;
using GearFlow.Inventory.Infrastructure.Email;
using GearFlow.Inventory.Infrastructure.Messaging;
using GearFlow.Inventory.Infrastructure.Persistence;
using GearFlow.Inventory.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GearFlow.Inventory.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Nome da connection string do Postgres do Inventory (<c>ConnectionStrings__Postgres</c>).</summary>
    public const string ConnectionStringName = "Postgres";

    public static IServiceCollection AddInventoryInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"ConnectionStrings:{ConnectionStringName} não configurada.");

        services.AddDbContext<InventoryDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IPartRepository, PartRepository>();
        services.AddScoped<IConsumableRepository, ConsumableRepository>();
        services.AddScoped<IStockReservationRepository, StockReservationRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        services.AddScoped<IDispatcher, Dispatcher>();
        services.TryAddSingleton(TimeProvider.System);

        // E-mail (avisos de estoque baixo e de estoque insuficiente)
        services.AddSingleton(configuration.GetSection(SmtpOptions.SectionName).Get<SmtpOptions>() ?? new SmtpOptions());
        services.AddSingleton(configuration.GetSection(StockAlertOptions.SectionName).Get<StockAlertOptions>() ?? new StockAlertOptions());
        services.TryAddScoped<IEmailSender, SmtpEmailSender>();

        // RabbitMQ + Outbox/Inbox + consumers
        services.AddInventoryMessaging(configuration);

        return services;
    }
}
