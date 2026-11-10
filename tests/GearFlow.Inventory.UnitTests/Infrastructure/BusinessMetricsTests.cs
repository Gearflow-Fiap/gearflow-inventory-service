using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using FluentAssertions;
using GearFlow.Contracts;
using GearFlow.Inventory.Api.Configurations;
using GearFlow.Inventory.Application;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Application.Behaviors;
using GearFlow.Inventory.Application.Notifications;
using GearFlow.Inventory.Application.Notifications.NotifyLowStock;
using GearFlow.Inventory.Application.Parts.Commands.AddPartStock;
using GearFlow.Inventory.Application.Reservations.Commands.ReleaseStock;
using GearFlow.Inventory.Application.Reservations.Commands.ReserveStock;
using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Domain.Repositories;
using GearFlow.Inventory.Infrastructure.Dispatching;
using GearFlow.Inventory.UnitTests.Application;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace GearFlow.Inventory.UnitTests.Infrastructure;

/// <summary>
/// Métricas de negócio do Inventory (o que alimenta o dashboard e os alertas de infra/newrelic). Os testes leem o Meter
/// de verdade, com um <see cref="MeterListener"/>, e travam os nomes: os NRQLs do Terraform dependem deles.
/// </summary>
public sealed class BusinessMetricsTests
{
    private sealed record Measurement(string Instrument, double Value, IReadOnlyDictionary<string, string?> Tags);

    /// <summary>Escuta só o Meter DESTA instância (o nome é global ao processo e outros testes também publicam nele).</summary>
    private sealed class Capture : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentQueue<Measurement> _measurements = new();

        public Capture(Meter meter)
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (ReferenceEquals(instrument.Meter, meter)) listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((i, v, tags, _) => Add(i, v, tags));
            _listener.SetMeasurementEventCallback<double>((i, v, tags, _) => Add(i, v, tags));
            _listener.Start();
        }

        public IReadOnlyList<Measurement> Measurements => [.. _measurements];

        public IEnumerable<Measurement> Of(string instrument) => Measurements.Where(m => m.Instrument == instrument);

        private void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var map = new Dictionary<string, string?>();
            foreach (var tag in tags) map[tag.Key] = tag.Value?.ToString();
            _measurements.Enqueue(new Measurement(instrument.Name, value, map));
        }

        public void Dispose() => _listener.Dispose();
    }

    private static BusinessMetricsDecorator<TCommand, TResult> Decorate<TCommand, TResult>(
        Func<TCommand, Task<TResult>> handler, IBusinessMetrics metrics)
        where TCommand : ICommand<TResult>
        where TResult : Result
    {
        var inner = Substitute.For<ICommandHandler<TCommand, TResult>>();
        inner.HandleAsync(Arg.Any<TCommand>(), Arg.Any<CancellationToken>()).Returns(call => handler(call.Arg<TCommand>()));
        return new BusinessMetricsDecorator<TCommand, TResult>(inner, metrics);
    }

    private static ReserveStockCommand Reserve() =>
        new(Guid.NewGuid(), [new StockItemLine(InventoryItemType.Part, Guid.NewGuid(), 1)]);

    [Fact]
    public void The_instrument_names_are_a_contract_with_the_new_relic_queries()
    {
        // Literais de propósito: se alguém renomear um instrumento, o NRQL de infra/newrelic para de achar a métrica.
        BusinessMetrics.MeterName.Should().Be("GearFlow.Business");
        ObservabilityExtensions.BusinessMeterName.Should().Be(BusinessMetrics.MeterName);
        BusinessMetrics.ReservationsName.Should().Be("inventory.reservations");
        BusinessMetrics.ReleasesName.Should().Be("inventory.releases");
        BusinessMetrics.ConsumptionsName.Should().Be("inventory.consumptions");
        BusinessMetrics.ReplenishmentsName.Should().Be("inventory.replenishments");
        BusinessMetrics.LowStockAlertsName.Should().Be("inventory.low_stock_alerts");
        BusinessMetrics.SagaStepDurationName.Should().Be("inventory.saga.step.duration");
        BusinessMetrics.IntegrationErrorsName.Should().Be("integration.errors");
    }

    [Fact]
    public void Every_instrument_is_published_on_the_business_meter()
    {
        using var metrics = new BusinessMetrics();
        using var capture = new Capture(metrics.Meter);

        metrics.Reservation("reserved");
        metrics.Release();
        metrics.Consumption();
        metrics.Replenishment("Part");
        metrics.LowStockAlert();
        metrics.SagaStepDuration("reserve", 12.5);
        metrics.IntegrationError("inventory.ReserveStock");

        capture.Measurements.Select(m => m.Instrument).Should().BeEquivalentTo(
        [
            "inventory.reservations", "inventory.releases", "inventory.consumptions", "inventory.replenishments",
            "inventory.low_stock_alerts", "inventory.saga.step.duration", "integration.errors",
        ]);
        capture.Of("inventory.reservations").Single().Tags["outcome"].Should().Be("reserved");
        capture.Of("inventory.saga.step.duration").Single().Value.Should().Be(12.5);
    }

    [Theory]
    [InlineData(ReserveStockStatus.Reserved, "reserved")]
    [InlineData(ReserveStockStatus.AlreadyReserved, "already_reserved")]
    [InlineData(ReserveStockStatus.Insufficient, "insufficient")]
    [InlineData(ReserveStockStatus.IgnoredAfterRelease, "ignored_after_release")]
    public async Task ReserveStock_counts_each_outcome_and_times_the_saga_step(ReserveStockStatus status, string outcome)
    {
        using var metrics = new BusinessMetrics();
        using var capture = new Capture(metrics.Meter);
        var decorator = Decorate<ReserveStockCommand, Result<ReserveStockOutcome>>(
            _ => Task.FromResult(Result.Success(ReserveStockOutcome.Of(status))), metrics);

        await decorator.HandleAsync(Reserve(), CancellationToken.None);

        capture.Of("inventory.reservations").Single().Tags["outcome"].Should().Be(outcome);
        capture.Of("inventory.saga.step.duration").Single().Tags["step"].Should().Be("reserve");
        capture.Of("integration.errors").Should().BeEmpty("faltar estoque é um desfecho previsto da Saga, não falha");
    }

    [Fact]
    public async Task A_failed_saga_command_is_an_integration_error_and_not_a_reservation()
    {
        using var metrics = new BusinessMetrics();
        using var capture = new Capture(metrics.Meter);
        var decorator = Decorate<ReserveStockCommand, Result<ReserveStockOutcome>>(
            _ => Task.FromResult(Result.Failure<ReserveStockOutcome>(Error.Validation("Reservation.DuplicateItem", "repetido"))),
            metrics);

        await decorator.HandleAsync(Reserve(), CancellationToken.None);

        capture.Of("integration.errors").Single().Tags["source"].Should().Be("inventory.ReserveStock");
        capture.Of("inventory.reservations").Should().BeEmpty();
        capture.Of("inventory.saga.step.duration").Should().ContainSingle("a duração do passo é medida mesmo quando ele falha");
    }

    [Fact]
    public async Task Release_counts_only_when_it_succeeds()
    {
        using var metrics = new BusinessMetrics();
        using var capture = new Capture(metrics.Meter);
        var ok = Decorate<ReleaseStockCommand, Result>(_ => Task.FromResult(Result.Success()), metrics);
        var failed = Decorate<ReleaseStockCommand, Result>(
            _ => Task.FromResult(Result.Failure(Error.Conflict("Reservation.AlreadyConsumed", "já consumida"))), metrics);
        var command = new ReleaseStockCommand(Guid.NewGuid(), "teste");

        await ok.HandleAsync(command, CancellationToken.None);
        await failed.HandleAsync(command, CancellationToken.None);

        capture.Of("inventory.releases").Should().ContainSingle();
        capture.Of("integration.errors").Single().Tags["source"].Should().Be("inventory.ReleaseStock");
    }

    [Fact]
    public async Task An_unexpected_exception_is_an_integration_error_and_is_rethrown()
    {
        using var metrics = new BusinessMetrics();
        using var capture = new Capture(metrics.Meter);
        var decorator = Decorate<ReserveStockCommand, Result<ReserveStockOutcome>>(
            _ => throw new InvalidOperationException("banco fora"), metrics);

        var act = () => decorator.HandleAsync(Reserve(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        capture.Of("integration.errors").Single().Tags["source"].Should().Be("inventory.ReserveStock");
    }

    [Fact]
    public async Task A_concurrency_conflict_is_expected_and_is_not_counted_as_an_error()
    {
        using var metrics = new BusinessMetrics();
        using var capture = new Capture(metrics.Meter);
        var decorator = Decorate<ReserveStockCommand, Result<ReserveStockOutcome>>(
            _ => throw new ConcurrencyConflictException("duas OS na última unidade"), metrics);

        var act = () => decorator.HandleAsync(Reserve(), CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        capture.Of("integration.errors").Should().BeEmpty();
    }

    [Fact]
    public async Task Replenishment_and_low_stock_alert_are_counted_on_success_only()
    {
        using var metrics = new BusinessMetrics();
        using var capture = new Capture(metrics.Meter);
        var part = Decorate<AddPartStockCommand, Result<GearFlow.Inventory.Application.Parts.PartDto>>(
            _ => Task.FromResult(Result.Failure<GearFlow.Inventory.Application.Parts.PartDto>(Error.NotFound("Part.NotFound", "x"))),
            metrics);
        var alert = Decorate<NotifyLowStockCommand, Result>(_ => Task.FromResult(Result.Success()), metrics);

        await part.HandleAsync(new AddPartStockCommand(Guid.NewGuid(), 5), CancellationToken.None);
        await alert.HandleAsync(new NotifyLowStockCommand(InventoryItemType.Part, "Filtro", 1m, 2m), CancellationToken.None);

        capture.Of("inventory.replenishments").Should().BeEmpty("a reposição falhou (404)");
        capture.Of("integration.errors").Should().BeEmpty("404 da REST é erro de cliente, não falha de integração");
        capture.Of("inventory.low_stock_alerts").Should().ContainSingle();
    }

    private static ServiceProvider BuildRealPipeline(StockTestBed bed)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInventoryApplication();
        services.AddSingleton<IPartRepository>(bed.Parts);
        services.AddSingleton<IConsumableRepository>(bed.Consumables);
        services.AddSingleton<IStockReservationRepository>(bed.Reservations);
        services.AddSingleton<IMessagePublisher>(bed.Publisher);
        services.AddSingleton<IUnitOfWork>(bed.UnitOfWork);
        services.AddSingleton<TimeProvider>(bed.Clock);
        services.AddSingleton(Substitute.For<IEmailSender>());
        services.AddSingleton(new StockAlertOptions());
        services.AddScoped<IDispatcher, Dispatcher>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public async Task The_real_dispatcher_pipeline_records_the_reservation_and_the_validation_failure()
    {
        // Container de DI real e Dispatcher real: prova que o decorator está montado no pipeline e que o singleton
        // registrado por AddInventoryApplication é o que ele usa (um Meter só por processo).
        var bed = new StockTestBed();
        var part = bed.AddPart(10);
        await using var provider = BuildRealPipeline(bed);
        var metrics = (BusinessMetrics)provider.GetRequiredService<IBusinessMetrics>();
        using var capture = new Capture(metrics.Meter);
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<IDispatcher>();

        await dispatcher.Send(new ReserveStockCommand(
            Guid.NewGuid(), [new StockItemLine(InventoryItemType.Part, part.Id.Value, 3)]));
        await dispatcher.Send(new ReserveStockCommand(Guid.Empty, []));   // a validação recusa: mensagem malformada

        capture.Of("inventory.reservations").Should().ContainSingle()
            .Which.Tags["outcome"].Should().Be("reserved");
        capture.Of("integration.errors").Should().ContainSingle()
            .Which.Tags["source"].Should().Be("inventory.ReserveStock");
        capture.Of("inventory.saga.step.duration").Should().HaveCount(2);
        provider.GetRequiredService<IBusinessMetrics>().Should().BeSameAs(provider.GetRequiredService<IBusinessMetrics>());
    }
}
