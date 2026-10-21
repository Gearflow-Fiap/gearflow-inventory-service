using FluentAssertions;
using GearFlow.Inventory.Application;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Application.Notifications;
using GearFlow.Inventory.Domain.Repositories;
using GearFlow.Inventory.Infrastructure;
using GearFlow.Inventory.Infrastructure.Email;
using GearFlow.Inventory.Infrastructure.Messaging;
using GearFlow.Inventory.Infrastructure.Persistence;
using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GearFlow.Inventory.UnitTests.Infrastructure;

/// <summary>Composition root: o grafo de DI do serviço real precisa montar por inteiro (sem abrir conexão).</summary>
public sealed class InfrastructureWiringTests
{
    private static IConfiguration ConfigWith(string? connectionString, params (string Key, string Value)[] extra)
    {
        var values = new Dictionary<string, string?>();
        if (connectionString is not null)
            values["ConnectionStrings:Postgres"] = connectionString;
        foreach (var (key, value) in extra)
            values[key] = value;

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    private static ServiceProvider BuildReal(IConfiguration? configuration = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInventoryApplication();
        services.AddInventoryInfrastructure(configuration ?? ConfigWith("Host=unused;Database=unused;Username=u;Password=p"));

        // ValidateOnBuild: falha aqui se alguma dependência (inclusive as do MassTransit) não estiver registrada.
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public void Missing_connection_string_fails_fast()
    {
        var act = () => new ServiceCollection().AddInventoryInfrastructure(ConfigWith(null));

        act.Should().Throw<InvalidOperationException>().WithMessage("*ConnectionStrings:Postgres*");
    }

    [Fact]
    public async Task The_whole_dependency_graph_resolves()
    {
        await using var provider = BuildReal();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IDispatcher>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IPartRepository>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IConsumableRepository>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IStockReservationRepository>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IUnitOfWork>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<InventoryDbContext>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IEmailSender>().Should().NotBeNull();
    }

    [Fact]
    public async Task Messages_are_published_through_the_masstransit_publisher()
    {
        await using var provider = BuildReal();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IMessagePublisher>().Should().BeOfType<MassTransitMessagePublisher>();
    }

    [Fact]
    public async Task Consumers_and_the_bus_are_registered()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInventoryApplication();
        services.AddInventoryInfrastructure(ConfigWith("Host=unused;Database=unused;Username=u;Password=p"));
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IBusControl>().Should().NotBeNull();
        services.Select(d => d.ServiceType)
            .Where(type => typeof(IConsumer).IsAssignableFrom(type) && type is { IsClass: true })
            .Select(type => type.Name)
            .Should().BeEquivalentTo(
                "ReserveStockConsumer", "ReleaseStockConsumer", "ConsumeStockConsumer",
                "LowStockAlertEmailConsumer", "StockInsufficientEmailConsumer");
    }

    [Fact]
    public async Task Smtp_and_stock_alert_settings_come_from_configuration()
    {
        var configuration = ConfigWith("Host=unused;Database=unused",
            ("Smtp:Host", "mailpit"), ("Smtp:Port", "2525"), ("Smtp:From", "estoque@oficina.test"),
            ("Stock:AlertEmail", "chefe@oficina.test"));
        await using var provider = BuildReal(configuration);

        var smtp = provider.GetRequiredService<SmtpOptions>();
        smtp.Host.Should().Be("mailpit");
        smtp.Port.Should().Be(2525);
        smtp.From.Should().Be("estoque@oficina.test");
        provider.GetRequiredService<StockAlertOptions>().AlertEmail.Should().Be("chefe@oficina.test");
    }

    [Fact]
    public async Task Defaults_apply_when_smtp_and_alert_settings_are_missing()
    {
        await using var provider = BuildReal();

        provider.GetRequiredService<SmtpOptions>().Port.Should().Be(1025);
        provider.GetRequiredService<StockAlertOptions>().AlertEmail.Should().Be("estoque@gearflow.com");
    }

    [Fact]
    public void Design_time_factory_builds_a_context_without_touching_the_database()
    {
        using var db = new DesignTimeDbContextFactory().CreateDbContext([]);

        db.Model.GetEntityTypes().Should().NotBeEmpty();
    }
}
