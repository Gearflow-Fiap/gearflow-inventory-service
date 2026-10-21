using FluentAssertions;
using GearFlow.Contracts;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Application.Reservations.Commands.ConsumeStock;
using GearFlow.Inventory.Application.Reservations.Commands.ReleaseStock;
using GearFlow.Inventory.Application.Reservations.Commands.ReserveStock;
using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Infrastructure.Messaging;
using GearFlow.Inventory.Infrastructure.Messaging.Consumers;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace GearFlow.Inventory.UnitTests.Infrastructure;

/// <summary>
/// Os consumers são finos: montam o command, despacham e traduzem falha de negócio em
/// <see cref="PermanentMessageException"/> (DLQ direto). Rodam no test harness em memória do MassTransit.
/// </summary>
public sealed class ConsumerTests
{
    private static readonly Guid ServiceOrderId = Guid.NewGuid();
    private readonly IDispatcher _dispatcher = Substitute.For<IDispatcher>();

    private ServiceProvider Build() => new ServiceCollection()
        .AddSingleton(_dispatcher)
        .AddMassTransitTestHarness(x =>
        {
            x.AddConsumer<ReserveStockConsumer>();
            x.AddConsumer<ReleaseStockConsumer>();
            x.AddConsumer<ConsumeStockConsumer>();
        })
        .BuildServiceProvider(true);

    private static readonly Error SomeError = Error.Conflict("Reservation.AlreadyConsumed", "já consumida");

    [Fact]
    public async Task ReserveStock_is_translated_to_the_command_and_dispatched()
    {
        _dispatcher.Send(Arg.Any<ReserveStockCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(ReserveStockOutcome.Of(ReserveStockStatus.Reserved)));
        await using var provider = Build();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        var items = new List<StockItemLine> { new(InventoryItemType.Part, Guid.NewGuid(), 2) };

        await harness.Bus.Publish(new ReserveStock(ServiceOrderId, Guid.NewGuid(), DateTime.UtcNow, items));

        (await harness.Consumed.Any<ReserveStock>()).Should().BeTrue();
        await _dispatcher.Received(1).Send(
            Arg.Is<ReserveStockCommand>(c => c.ServiceOrderId == ServiceOrderId && c.Items.Count == 1),
            Arg.Any<CancellationToken>());
        (await harness.Published.Any<Fault<ReserveStock>>()).Should().BeFalse();
    }

    [Fact]
    public async Task ReserveStock_that_the_domain_rejects_faults_as_permanent()
    {
        _dispatcher.Send(Arg.Any<ReserveStockCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure<ReserveStockOutcome>(Error.Validation("Reservation.DuplicateItem", "repetido")));
        await using var provider = Build();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new ReserveStock(ServiceOrderId, Guid.NewGuid(), DateTime.UtcNow, []));

        (await harness.Published.Any<Fault<ReserveStock>>()).Should().BeTrue();
        var fault = harness.Published.Select<Fault<ReserveStock>>().Single().Context.Message;
        fault.Exceptions.Single().ExceptionType.Should().Be(typeof(PermanentMessageException).FullName);
    }

    [Fact]
    public async Task ReleaseStock_is_dispatched_with_the_reason()
    {
        _dispatcher.Send(Arg.Any<ReleaseStockCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        await using var provider = Build();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new ReleaseStock(ServiceOrderId, Guid.NewGuid(), DateTime.UtcNow, "pagamento recusado"));

        (await harness.Consumed.Any<ReleaseStock>()).Should().BeTrue();
        await _dispatcher.Received(1).Send(
            Arg.Is<ReleaseStockCommand>(c => c.ServiceOrderId == ServiceOrderId && c.Reason == "pagamento recusado"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReleaseStock_on_a_consumed_reservation_faults_as_permanent()
    {
        _dispatcher.Send(Arg.Any<ReleaseStockCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Failure(SomeError));
        await using var provider = Build();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new ReleaseStock(ServiceOrderId, Guid.NewGuid(), DateTime.UtcNow, "x"));

        (await harness.Published.Any<Fault<ReleaseStock>>()).Should().BeTrue();
        harness.Published.Select<Fault<ReleaseStock>>().Single().Context.Message.Exceptions.Single().Message
            .Should().Contain("Reservation.AlreadyConsumed");
    }

    [Fact]
    public async Task ConsumeStock_is_dispatched()
    {
        _dispatcher.Send(Arg.Any<ConsumeStockCommand>(), Arg.Any<CancellationToken>()).Returns(Result.Success());
        await using var provider = Build();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new ConsumeStock(ServiceOrderId, Guid.NewGuid(), DateTime.UtcNow));

        (await harness.Consumed.Any<ConsumeStock>()).Should().BeTrue();
        await _dispatcher.Received(1).Send(
            Arg.Is<ConsumeStockCommand>(c => c.ServiceOrderId == ServiceOrderId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ConsumeStock_without_a_reservation_faults_as_permanent()
    {
        _dispatcher.Send(Arg.Any<ConsumeStockCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(Error.NotFound("Reservation.NotFound", "sem reserva")));
        await using var provider = Build();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new ConsumeStock(ServiceOrderId, Guid.NewGuid(), DateTime.UtcNow));

        (await harness.Published.Any<Fault<ConsumeStock>>()).Should().BeTrue();
    }

    [Fact]
    public async Task A_transient_failure_is_not_marked_permanent()
    {
        _dispatcher.Send(Arg.Any<ConsumeStockCommand>(), Arg.Any<CancellationToken>())
            .Returns<Result>(_ => throw new ConcurrencyConflictException("conflito"));
        await using var provider = Build();
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new ConsumeStock(ServiceOrderId, Guid.NewGuid(), DateTime.UtcNow));

        (await harness.Published.Any<Fault<ConsumeStock>>()).Should().BeTrue();
        harness.Published.Select<Fault<ConsumeStock>>().Single().Context.Message.Exceptions.Single().ExceptionType
            .Should().Be(typeof(ConcurrencyConflictException).FullName);
    }
}
