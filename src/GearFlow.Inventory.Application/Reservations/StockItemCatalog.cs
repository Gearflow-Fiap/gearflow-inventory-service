using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Domain.Repositories;
using GearFlow.Inventory.Domain.Stock;

namespace GearFlow.Inventory.Application.Reservations;

/// <summary>
/// Itens de estoque (peças e insumos) carregados para uma operação de reserva. Esconde a diferença
/// entre <see cref="Part"/> (inteiro) e <see cref="Consumable"/> (decimal) dos handlers de reserva,
/// liberação e consumo, que tratam as linhas de forma uniforme.
///
/// Os agregados ficam rastreados pelo repositório: as mutações daqui são persistidas pelo
/// <c>IUnitOfWork.SaveChangesAsync</c> do handler, todas na mesma transação.
/// </summary>
internal sealed class StockItemCatalog
{
    private readonly Dictionary<Guid, Part> _parts;
    private readonly Dictionary<Guid, Consumable> _consumables;

    private StockItemCatalog(Dictionary<Guid, Part> parts, Dictionary<Guid, Consumable> consumables)
    {
        _parts = parts;
        _consumables = consumables;
    }

    public static async Task<StockItemCatalog> LoadAsync(
        IPartRepository parts,
        IConsumableRepository consumables,
        IEnumerable<(StockItemType Type, Guid Id)> keys,
        CancellationToken ct)
    {
        var wanted = keys.ToList();

        var partIds = wanted.Where(k => k.Type == StockItemType.Part).Select(k => PartId.From(k.Id)).Distinct().ToList();
        var consumableIds = wanted.Where(k => k.Type == StockItemType.Consumable).Select(k => ConsumableId.From(k.Id)).Distinct().ToList();

        var loadedParts = partIds.Count == 0 ? [] : await parts.GetByIdsAsync(partIds, ct);
        var loadedConsumables = consumableIds.Count == 0 ? [] : await consumables.GetByIdsAsync(consumableIds, ct);

        return new StockItemCatalog(
            loadedParts.ToDictionary(p => p.Id.Value),
            loadedConsumables.ToDictionary(c => c.Id.Value));
    }

    public bool Contains(StockItemType type, Guid id) =>
        type == StockItemType.Part ? _parts.ContainsKey(id) : _consumables.ContainsKey(id);

    public bool TryGetName(StockItemType type, Guid id, out string name)
    {
        name = type == StockItemType.Part
            ? _parts.GetValueOrDefault(id)?.Name ?? string.Empty
            : _consumables.GetValueOrDefault(id)?.Name ?? string.Empty;
        return Contains(type, id);
    }

    /// <summary>Disponível do item (0 se não existir).</summary>
    public decimal AvailableQuantity(StockItemType type, Guid id) =>
        type == StockItemType.Part
            ? _parts.GetValueOrDefault(id)?.Quantity ?? 0
            : _consumables.GetValueOrDefault(id)?.Quantity ?? 0m;

    public bool HasAvailability(StockItemType type, Guid id, decimal quantity) =>
        type == StockItemType.Part
            ? _parts.TryGetValue(id, out var part) && part.HasAvailability((int)quantity)
            : _consumables.TryGetValue(id, out var consumable) && consumable.HasAvailability(quantity);

    public Result Reserve(StockItemType type, Guid id, decimal quantity) =>
        Apply(type, id, part => part.Reserve((int)quantity), consumable => consumable.Reserve(quantity));

    public Result Release(StockItemType type, Guid id, decimal quantity) =>
        Apply(type, id, part => part.Release((int)quantity), consumable => consumable.Release(quantity));

    public Result Consume(StockItemType type, Guid id, decimal quantity) =>
        Apply(type, id, part => part.Consume((int)quantity), consumable => consumable.Consume(quantity));

    /// <summary>Saldo disponível e mínimo se o item está no/abaixo do estoque mínimo; senão <c>null</c>.</summary>
    public (decimal Remaining, decimal Minimum)? LowStock(StockItemType type, Guid id)
    {
        if (type == StockItemType.Part)
            return _parts.TryGetValue(id, out var part) && part.IsStockBelowMinimum()
                ? (part.Quantity, Part.MinimumStockQuantity)
                : null;

        return _consumables.TryGetValue(id, out var consumable) && consumable.IsStockBelowMinimum()
            ? (consumable.Quantity, Consumable.MinimumStockQuantity)
            : null;
    }

    private Result Apply(StockItemType type, Guid id, Func<Part, Result> onPart, Func<Consumable, Result> onConsumable)
    {
        if (type == StockItemType.Part)
            return _parts.TryGetValue(id, out var part)
                ? onPart(part)
                : Result.Failure(Error.NotFound("Part.NotFound", $"Peça {id} não encontrada."));

        return _consumables.TryGetValue(id, out var consumable)
            ? onConsumable(consumable)
            : Result.Failure(Error.NotFound("Consumable.NotFound", $"Insumo {id} não encontrado."));
    }
}
