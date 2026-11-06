using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentAssertions;
using GearFlow.Contracts;
using GearFlow.Inventory.Api.Configurations;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Application.Reservations.Commands.ReleaseStock;
using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Infrastructure.Messaging.Consumers;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace GearFlow.Inventory.UnitTests.Infrastructure;

/// <summary>
/// O <c>AddObservability</c> registra a fonte e o medidor "MassTransit" para o trace atravessar o RabbitMQ (requisito de
/// observabilidade da Fase 4). Se o nome estiver errado nada quebra, o span só some: por isso este teste liga um listener
/// no nome registrado, consome uma mensagem de verdade no harness e exige que algo tenha sido emitido.
/// </summary>
public sealed class MassTransitTelemetryTests
{
    private static ServiceProvider Build(IDispatcher dispatcher) => new ServiceCollection()
        .AddSingleton(dispatcher)
        .AddMassTransitTestHarness(x => x.AddConsumer<ReleaseStockConsumer>())
        .BuildServiceProvider(true);

    private static async Task ConsumeOneMessage()
    {
        var dispatcher = Substitute.For<IDispatcher>();
        dispatcher.Send(Arg.Any<ReleaseStockCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        await using var provider = Build(dispatcher);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new ReleaseStock(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, "teste"));

        (await harness.Consumed.Any<ReleaseStock>()).Should().BeTrue();
    }

    [Fact]
    public async Task The_registered_source_name_is_the_one_MassTransit_emits_spans_on()
    {
        var activities = new ConcurrentBag<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ObservabilityExtensions.MassTransitSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Add(activity.OperationName),
        };
        ActivitySource.AddActivityListener(listener);

        await ConsumeOneMessage();

        activities.Should().NotBeEmpty("o trace só atravessa o RabbitMQ se a fonte registrada for a que o MassTransit usa");
    }

    [Fact]
    public async Task The_registered_meter_name_is_the_one_MassTransit_publishes_metrics_on()
    {
        var instruments = new ConcurrentBag<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name != ObservabilityExtensions.MassTransitSourceName) return;
                instruments.Add(instrument.Name);
                meterListener.EnableMeasurementEvents(instrument);
            },
        };
        listener.Start();

        await ConsumeOneMessage();

        instruments.Should().NotBeEmpty("as métricas de consumo só saem se o medidor registrado for o que o MassTransit usa");
    }
}
