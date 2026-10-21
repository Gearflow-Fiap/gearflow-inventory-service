using FluentAssertions;
using GearFlow.Inventory.Infrastructure.Messaging;
using Microsoft.Extensions.Configuration;

namespace GearFlow.Inventory.UnitTests.Infrastructure;

public sealed class RabbitMqOptionsTests
{
    [Fact]
    public void Defaults_are_local_guest_with_the_billing_retry_policy()
    {
        var options = new RabbitMqOptions();

        options.Host.Should().Be("localhost");
        options.Port.Should().Be(5672);
        options.VirtualHost.Should().Be("/");
        options.Username.Should().Be("guest");
        options.RetryIntervals().Should().Equal(
            TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void Binds_from_environment_style_configuration()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMq:Host"] = "rabbitmq.gearflow",
            ["RabbitMq:Port"] = "5673",
            ["RabbitMq:Username"] = "inventory",
            ["RabbitMq:RetryIntervalsMilliseconds:0"] = "50",
            ["RabbitMq:RetryIntervalsMilliseconds:1"] = "100",
        }).Build();

        var options = configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>()!;

        options.Host.Should().Be("rabbitmq.gearflow");
        options.Port.Should().Be(5673);
        options.Username.Should().Be("inventory");
        options.RetryIntervals().Should().Equal(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100));
    }

    [Fact]
    public void Queue_names_follow_the_service_command_convention()
    {
        QueueNames.ReserveStock.Should().Be("inventory-reserve-stock");
        QueueNames.ReleaseStock.Should().Be("inventory-release-stock");
        QueueNames.ConsumeStock.Should().Be("inventory-consume-stock");
    }
}
