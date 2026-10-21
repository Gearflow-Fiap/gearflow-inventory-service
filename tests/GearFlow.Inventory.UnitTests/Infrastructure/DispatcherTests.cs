using FluentAssertions;
using GearFlow.Contracts;
using GearFlow.Inventory.Application;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Application.Notifications;
using GearFlow.Inventory.Application.Parts.Commands.DeletePart;
using GearFlow.Inventory.Application.Parts.Queries.GetParts;
using GearFlow.Inventory.Application.Reservations.Commands.ReserveStock;
using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Domain.Repositories;
using GearFlow.Inventory.Infrastructure.Dispatching;
using GearFlow.Inventory.UnitTests.Application;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace GearFlow.Inventory.UnitTests.Infrastructure;

/// <summary>Dispatcher + decorators sobre o container de DI real (handlers registrados por varredura).</summary>
public sealed class DispatcherTests
{
    private static ServiceProvider Build(StockTestBed bed, IUnitOfWork? unitOfWork = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInventoryApplication();
        services.AddSingleton<IPartRepository>(bed.Parts);
        services.AddSingleton<IConsumableRepository>(bed.Consumables);
        services.AddSingleton<IStockReservationRepository>(bed.Reservations);
        services.AddSingleton<IMessagePublisher>(bed.Publisher);
        services.AddSingleton(unitOfWork ?? bed.UnitOfWork);
        services.AddSingleton<TimeProvider>(bed.Clock);
        services.AddSingleton(Substitute.For<IEmailSender>());
        services.AddSingleton(new StockAlertOptions());
        services.AddScoped<IDispatcher, Dispatcher>();

        // ValidateOnBuild: falha aqui se algum handler tiver dependência não registrada.
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    private static IDispatcher DispatcherOf(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<IDispatcher>();

    [Fact]
    public async Task Send_validates_before_calling_the_handler()
    {
        var bed = new StockTestBed();
        await using var provider = Build(bed);
        using var scope = provider.CreateScope();

        var result = await DispatcherOf(scope).Send(new ReserveStockCommand(Guid.Empty, []));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Validation.Failed");
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Description.Should().Contain("ordem de serviço").And.Contain("pelo menos um item");
        bed.Publisher.Messages.Should().BeEmpty();
        bed.UnitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Send_dispatches_a_valid_command_to_its_handler()
    {
        var bed = new StockTestBed();
        var part = bed.AddPart(10);
        await using var provider = Build(bed);
        using var scope = provider.CreateScope();

        var result = await DispatcherOf(scope).Send(new ReserveStockCommand(
            Guid.NewGuid(), [new StockItemLine(InventoryItemType.Part, part.Id.Value, 3)]));

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(ReserveStockStatus.Reserved);
        part.ReservedQuantity.Should().Be(3);
    }

    [Fact]
    public async Task Send_supports_commands_that_return_a_plain_Result_and_have_no_validator()
    {
        var bed = new StockTestBed();
        await using var provider = Build(bed);
        using var scope = provider.CreateScope();

        var result = await DispatcherOf(scope).Send(new DeletePartCommand(Guid.NewGuid()));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Part.NotFound");
    }

    [Fact]
    public async Task Query_dispatches_to_the_query_handler()
    {
        var bed = new StockTestBed();
        bed.AddPart(10, "PN-1", "Vela");
        await using var provider = Build(bed);
        using var scope = provider.CreateScope();

        var result = await DispatcherOf(scope).Query(new GetPartsQuery());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Name.Should().Be("Vela");
    }

    [Fact]
    public async Task The_same_command_type_can_be_dispatched_repeatedly()
    {
        var bed = new StockTestBed();
        await using var provider = Build(bed);
        using var scope = provider.CreateScope();
        var dispatcher = DispatcherOf(scope);

        for (var i = 0; i < 3; i++)
            (await dispatcher.Send(new DeletePartCommand(Guid.NewGuid()))).Error.Code.Should().Be("Part.NotFound");
    }

    [Fact]
    public async Task Exceptions_from_the_unit_of_work_propagate_so_the_consumer_can_retry()
    {
        var bed = new StockTestBed();
        var part = bed.AddPart(10);
        await using var provider = Build(bed, new ThrowingUnitOfWork());
        using var scope = provider.CreateScope();

        var act = () => DispatcherOf(scope).Send(new ReserveStockCommand(
            Guid.NewGuid(), [new StockItemLine(InventoryItemType.Part, part.Id.Value, 1)]));

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task Every_command_and_query_in_the_application_has_a_registered_handler()
    {
        var bed = new StockTestBed();
        await using var provider = Build(bed);
        using var scope = provider.CreateScope();

        var requests = typeof(ApplicationServiceCollectionExtensions).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType &&
                            (i.GetGenericTypeDefinition() == typeof(ICommand<>) || i.GetGenericTypeDefinition() == typeof(IQuery<>)))
                .Select(i => (Request: t, Contract: i)))
            .ToList();

        requests.Should().NotBeEmpty();
        foreach (var (request, contract) in requests)
        {
            var handlerDefinition = contract.GetGenericTypeDefinition() == typeof(ICommand<>)
                ? typeof(ICommandHandler<,>)
                : typeof(IQueryHandler<,>);
            var handlerType = handlerDefinition.MakeGenericType(request, contract.GetGenericArguments()[0]);

            scope.ServiceProvider.GetService(handlerType).Should().NotBeNull($"{request.Name} precisa de um handler registrado");
        }
    }

    private sealed class ThrowingUnitOfWork : IUnitOfWork
    {
        public Task SaveChangesAsync(CancellationToken ct = default) =>
            throw new ConcurrencyConflictException("simulado");
    }
}
