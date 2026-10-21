using FluentAssertions;
using GearFlow.Contracts;
using GearFlow.Inventory.Application.Consumables.Commands.AddConsumableStock;
using GearFlow.Inventory.Application.Consumables.Commands.CreateConsumable;
using GearFlow.Inventory.Application.Consumables.Commands.DeleteConsumable;
using GearFlow.Inventory.Application.Consumables.Commands.UpdateConsumable;
using GearFlow.Inventory.Application.Consumables.Queries.GetConsumableById;
using GearFlow.Inventory.Application.Consumables.Queries.GetConsumables;
using GearFlow.Inventory.Application.Parts.Commands.AddPartStock;
using GearFlow.Inventory.Application.Parts.Commands.CreatePart;
using GearFlow.Inventory.Application.Parts.Commands.DeletePart;
using GearFlow.Inventory.Application.Parts.Commands.UpdatePart;
using GearFlow.Inventory.Application.Parts.Queries.GetPartById;
using GearFlow.Inventory.Application.Parts.Queries.GetParts;
using GearFlow.Inventory.Application.Reservations.Commands.ReserveStock;
using GearFlow.Inventory.Application.Reservations.Queries.GetReservationByServiceOrder;
using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.UnitTests.Application;

/// <summary>
/// CRUD portado do monólito (12 handlers): cobre sucesso e falha de cada um sobre o domínio real.
/// A diferença para o monólito: reposição publica pelo <c>IMessagePublisher</c> e remoção respeita reservas.
/// </summary>
public sealed class CrudHandlerTests
{
    private readonly StockTestBed _bed = new();

    // ---- Peça: Create --------------------------------------------------------

    [Fact]
    public async Task CreatePart_persists_commits_and_returns_dto()
    {
        var result = await _bed.CreatePart.HandleAsync(
            new CreatePartCommand("Filtro", "óleo", "PN-1", "Bosch", 5000, 10), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("Filtro");
        result.Value.BelowMinimum.Should().BeFalse();
        _bed.Parts.Items.Should().ContainSingle();
        _bed.UnitOfWork.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task CreatePart_with_invalid_data_fails_and_does_not_persist()
    {
        var result = await _bed.CreatePart.HandleAsync(
            new CreatePartCommand("", "óleo", "PN-1", "Bosch", 5000, 10), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Part.NameRequired");
        _bed.Parts.Items.Should().BeEmpty();
        _bed.UnitOfWork.SaveCount.Should().Be(0);
    }

    // ---- Peça: Update / Delete / Get -----------------------------------------

    [Fact]
    public async Task UpdatePart_updates_when_found()
    {
        var part = _bed.AddPart();

        var result = await _bed.UpdatePart.HandleAsync(
            new UpdatePartCommand(part.Id.Value, "Correia", "d", "PN-2", "Gates", 7000, 20), default);

        result.IsSuccess.Should().BeTrue();
        result.Value.PartNumber.Should().Be("PN-2");
        _bed.UnitOfWork.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task UpdatePart_returns_not_found_when_missing()
    {
        var result = await _bed.UpdatePart.HandleAsync(
            new UpdatePartCommand(Guid.NewGuid(), "x", "d", "PN", "M", 1, 1), default);

        result.Error.Code.Should().Be("Part.NotFound");
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task DeletePart_removes_when_found()
    {
        var part = _bed.AddPart();

        var result = await _bed.DeletePart.HandleAsync(new DeletePartCommand(part.Id.Value), default);

        result.IsSuccess.Should().BeTrue();
        _bed.Parts.Items.Should().BeEmpty();
        _bed.UnitOfWork.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task DeletePart_returns_not_found_when_missing()
    {
        (await _bed.DeletePart.HandleAsync(new DeletePartCommand(Guid.NewGuid()), default))
            .Error.Code.Should().Be("Part.NotFound");
    }

    [Fact]
    public async Task DeletePart_is_refused_while_units_are_reserved_by_a_service_order()
    {
        var part = _bed.AddPart(10);
        await _bed.Reserve.HandleAsync(
            new ReserveStockCommand(Guid.NewGuid(), [new StockItemLine(InventoryItemType.Part, part.Id.Value, 2)]), default);
        var commitsBefore = _bed.UnitOfWork.SaveCount;

        var result = await _bed.DeletePart.HandleAsync(new DeletePartCommand(part.Id.Value), default);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Part.HasActiveReservations");
        result.Error.Type.Should().Be(ErrorType.Conflict);
        _bed.Parts.Items.Should().ContainSingle();
        _bed.UnitOfWork.SaveCount.Should().Be(commitsBefore);
    }

    [Fact]
    public async Task GetPartById_returns_dto_or_not_found()
    {
        var part = _bed.AddPart();

        (await _bed.GetPartById.HandleAsync(new GetPartByIdQuery(part.Id.Value), default)).IsSuccess.Should().BeTrue();
        (await _bed.GetPartById.HandleAsync(new GetPartByIdQuery(Guid.NewGuid()), default))
            .Error.Code.Should().Be("Part.NotFound");
    }

    [Fact]
    public async Task GetParts_returns_the_list_ordered_by_name_and_flags_low_stock()
    {
        _bed.AddPart(10, "PN-B", "Vela");
        _bed.AddPart(3, "PN-A", "Amortecedor");

        var result = await _bed.GetParts.HandleAsync(new GetPartsQuery(), default);

        result.Value.Select(p => p.Name).Should().Equal("Amortecedor", "Vela");
        result.Value.Select(p => p.BelowMinimum).Should().Equal(true, false);
    }

    // ---- Peça: AddStock ------------------------------------------------------

    [Fact]
    public async Task AddPartStock_replenishes_and_publishes_StockReplenished()
    {
        var part = _bed.AddPart(2);

        var result = await _bed.AddPartStock.HandleAsync(new AddPartStockCommand(part.Id.Value, 8), default);

        result.IsSuccess.Should().BeTrue();
        part.Quantity.Should().Be(10);
        var message = _bed.Publisher.Of<StockReplenished>().Should().ContainSingle().Subject;
        message.ItemType.Should().Be(InventoryItemType.Part);
        message.ItemId.Should().Be(part.Id.Value);
        message.OccurredOn.Should().Be(StockTestBed.Now);
        _bed.UnitOfWork.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task AddPartStock_returns_not_found_when_missing_and_publishes_nothing()
    {
        var result = await _bed.AddPartStock.HandleAsync(new AddPartStockCommand(Guid.NewGuid(), 8), default);

        result.Error.Code.Should().Be("Part.NotFound");
        _bed.Publisher.Messages.Should().BeEmpty();
        _bed.UnitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task AddPartStock_with_invalid_quantity_fails_and_publishes_nothing()
    {
        var part = _bed.AddPart();

        var result = await _bed.AddPartStock.HandleAsync(new AddPartStockCommand(part.Id.Value, 0), default);

        result.Error.Code.Should().Be("Part.AddQuantityInvalid");
        _bed.Publisher.Messages.Should().BeEmpty();
        _bed.UnitOfWork.SaveCount.Should().Be(0);
    }

    // ---- Insumo --------------------------------------------------------------

    [Fact]
    public async Task CreateConsumable_persists_or_fails_on_invalid()
    {
        (await _bed.CreateConsumable.HandleAsync(new CreateConsumableCommand("Óleo", 3000, 10m), default))
            .IsSuccess.Should().BeTrue();
        (await _bed.CreateConsumable.HandleAsync(new CreateConsumableCommand(" ", 3000, 10m), default))
            .Error.Code.Should().Be("Consumable.NameRequired");

        _bed.Consumables.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task UpdateConsumable_updates_or_not_found()
    {
        var consumable = _bed.AddConsumable();

        (await _bed.UpdateConsumable.HandleAsync(
            new UpdateConsumableCommand(consumable.Id.Value, "Aditivo", 4200, 25m), default)).IsSuccess.Should().BeTrue();
        (await _bed.UpdateConsumable.HandleAsync(
            new UpdateConsumableCommand(Guid.NewGuid(), "x", 1, 1m), default)).Error.Code.Should().Be("Consumable.NotFound");
    }

    [Fact]
    public async Task DeleteConsumable_removes_or_not_found()
    {
        var consumable = _bed.AddConsumable();

        (await _bed.DeleteConsumable.HandleAsync(new DeleteConsumableCommand(consumable.Id.Value), default))
            .IsSuccess.Should().BeTrue();
        _bed.Consumables.Items.Should().BeEmpty();
        (await _bed.DeleteConsumable.HandleAsync(new DeleteConsumableCommand(Guid.NewGuid()), default))
            .Error.Code.Should().Be("Consumable.NotFound");
    }

    [Fact]
    public async Task DeleteConsumable_is_refused_while_quantity_is_reserved()
    {
        var oil = _bed.AddConsumable(10m);
        await _bed.Reserve.HandleAsync(
            new ReserveStockCommand(Guid.NewGuid(), [new StockItemLine(InventoryItemType.Consumable, oil.Id.Value, 1.5m)]), default);

        var result = await _bed.DeleteConsumable.HandleAsync(new DeleteConsumableCommand(oil.Id.Value), default);

        result.Error.Code.Should().Be("Consumable.HasActiveReservations");
        _bed.Consumables.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task GetConsumableById_returns_dto_or_not_found()
    {
        var consumable = _bed.AddConsumable();

        (await _bed.GetConsumableById.HandleAsync(new GetConsumableByIdQuery(consumable.Id.Value), default))
            .IsSuccess.Should().BeTrue();
        (await _bed.GetConsumableById.HandleAsync(new GetConsumableByIdQuery(Guid.NewGuid()), default))
            .Error.Code.Should().Be("Consumable.NotFound");
    }

    [Fact]
    public async Task GetConsumables_returns_the_list()
    {
        _bed.AddConsumable(10m, "Óleo");
        _bed.AddConsumable(3m, "Água");

        var result = await _bed.GetConsumables.HandleAsync(new GetConsumablesQuery(), default);

        result.Value.Select(c => c.Name).Should().Equal("Água", "Óleo");
    }

    [Fact]
    public async Task AddConsumableStock_replenishes_publishes_or_not_found()
    {
        var consumable = _bed.AddConsumable(2m);

        var ok = await _bed.AddConsumableStock.HandleAsync(new AddConsumableStockCommand(consumable.Id.Value, 8.5m), default);
        ok.IsSuccess.Should().BeTrue();
        consumable.Quantity.Should().Be(10.5m);
        _bed.Publisher.Of<StockReplenished>().Should().ContainSingle()
            .Which.ItemType.Should().Be(InventoryItemType.Consumable);

        var missing = await _bed.AddConsumableStock.HandleAsync(new AddConsumableStockCommand(Guid.NewGuid(), 8m), default);
        missing.Error.Code.Should().Be("Consumable.NotFound");
    }

    // ---- Reserva (consulta) --------------------------------------------------

    [Fact]
    public async Task GetReservationByServiceOrder_returns_status_and_lines_or_not_found()
    {
        var part = _bed.AddPart(10, name: "Filtro de óleo");
        var serviceOrderId = Guid.NewGuid();
        await _bed.Reserve.HandleAsync(
            new ReserveStockCommand(serviceOrderId, [new StockItemLine(InventoryItemType.Part, part.Id.Value, 2)]), default);

        var found = await _bed.GetReservation.HandleAsync(new GetReservationByServiceOrderQuery(serviceOrderId), default);

        found.IsSuccess.Should().BeTrue();
        found.Value.Status.Should().Be("Reserved");
        found.Value.Lines.Should().ContainSingle()
            .Which.Should().Be(new GearFlow.Inventory.Application.Reservations.ReservationLineDto("Part", part.Id.Value, "Filtro de óleo", 2m));

        (await _bed.GetReservation.HandleAsync(new GetReservationByServiceOrderQuery(Guid.NewGuid()), default))
            .Error.Code.Should().Be("Reservation.NotFound");
    }
}
