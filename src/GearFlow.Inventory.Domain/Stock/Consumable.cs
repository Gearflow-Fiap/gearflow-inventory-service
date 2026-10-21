using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Domain.Stock;

/// <summary>
/// Insumo de estoque (quantidade fracionária, ex.: litros). Mesmo modelo bifásico da <see cref="Part"/>:
/// reserva → (liberação | consumo).
/// </summary>
public sealed class Consumable : AggregateRoot<ConsumableId>
{
    public const decimal MinimumStockQuantity = 5m;

    public string Name { get; private set; }
    public int UnitPriceCents { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal ReservedQuantity { get; private set; }
    public DateTime UpdatedOn { get; private set; }

    private Consumable(ConsumableId id, string name, int unitPriceCents, decimal quantity)
        : base(id)
    {
        Name = name;
        UnitPriceCents = unitPriceCents;
        Quantity = quantity;
        UpdatedOn = DateTime.UtcNow;
    }

    // Construtor do EF Core.
    private Consumable() : base(ConsumableId.New())
    {
        Name = null!;
    }

    public static Result<Consumable> Create(string name, int unitPriceCents, decimal quantity) =>
        Create(ConsumableId.New(), name, unitPriceCents, quantity);

    /// <summary>Cria com id conhecido. Usado pelo seed, que precisa de GUIDs fixos entre os serviços.</summary>
    public static Result<Consumable> Create(ConsumableId id, string name, int unitPriceCents, decimal quantity)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Consumable>(Error.Validation("Consumable.NameRequired", "Nome do insumo é obrigatório."));
        if (unitPriceCents < 0)
            return Result.Failure<Consumable>(Error.Validation("Consumable.PriceNegative", "Preço não pode ser negativo."));
        if (quantity < 0)
            return Result.Failure<Consumable>(Error.Validation("Consumable.QuantityNegative", "Quantidade não pode ser negativa."));

        return Result.Success(new Consumable(id, name.Trim(), unitPriceCents, quantity));
    }

    public bool IsStockBelowMinimum() => Quantity <= MinimumStockQuantity;

    public bool HasAvailability(decimal quantity) => Quantity >= quantity;

    public Result Reserve(decimal quantity)
    {
        if (quantity <= 0)
            return Result.Failure(Error.Validation("Consumable.ReserveQuantityInvalid", "A quantidade reservada deve ser maior que zero."));
        if (Quantity < quantity)
            return Result.Failure(Error.Conflict("Consumable.InsufficientStock",
                $"Não há disponibilidade no estoque do insumo {Name}. No estoque: {Quantity}, Reservadas: {ReservedQuantity}, Solicitado: {quantity}"));

        Quantity -= quantity;
        ReservedQuantity += quantity;
        UpdatedOn = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>Desfaz uma reserva: reservado → disponível. Compensação da Saga.</summary>
    public Result Release(decimal quantity)
    {
        if (quantity <= 0)
            return Result.Failure(Error.Validation("Consumable.ReleaseQuantityInvalid", "A quantidade liberada deve ser maior que zero."));
        if (ReservedQuantity < quantity)
            return Result.Failure(Error.Conflict("Consumable.InsufficientReserved",
                $"Insumo {Name} não possui quantidade reservada suficiente. Reservado: {ReservedQuantity}, Solicitado: {quantity}"));

        ReservedQuantity -= quantity;
        Quantity += quantity;
        UpdatedOn = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Consume(decimal quantity)
    {
        if (quantity <= 0)
            return Result.Failure(Error.Validation("Consumable.ConsumeQuantityInvalid", "A quantidade consumida deve ser maior que zero."));
        if (ReservedQuantity < quantity)
            return Result.Failure(Error.Conflict("Consumable.InsufficientReserved",
                $"Insumo {Name} não possui quantidade reservada suficiente. Reservado: {ReservedQuantity}, Solicitado: {quantity}"));

        ReservedQuantity -= quantity;
        UpdatedOn = DateTime.UtcNow;
        return Result.Success();
    }

    public Result AddQuantity(decimal quantity)
    {
        if (quantity <= 0)
            return Result.Failure(Error.Validation("Consumable.AddQuantityInvalid", "A quantidade a adicionar deve ser maior que zero."));

        Quantity += quantity;
        UpdatedOn = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>Insumo com reserva ativa não pode ser removido: alguma OS em andamento depende dele.</summary>
    public Result EnsureCanBeRemoved() =>
        ReservedQuantity > 0
            ? Result.Failure(Error.Conflict("Consumable.HasActiveReservations",
                $"O insumo {Name} tem {ReservedQuantity} reservado(s) por OS em andamento e não pode ser removido."))
            : Result.Success();

    // Mantido como no monólito (sem validação). Sobrescreve o disponível direto; ver "pontos de atenção" no README.
    public void UpdateDetails(string name, int unitPriceCents, decimal quantity)
    {
        Name = name;
        UnitPriceCents = unitPriceCents;
        Quantity = quantity;
        UpdatedOn = DateTime.UtcNow;
    }
}
