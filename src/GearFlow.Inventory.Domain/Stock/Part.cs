using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Domain.Stock;

/// <summary>
/// Peça de estoque. Modelo bifásico herdado do monólito: <see cref="Quantity"/> é o <b>disponível</b>;
/// <see cref="Reserve"/> move disponível → reservado; <see cref="Release"/> desfaz a reserva
/// (compensação da Saga, novo); <see cref="Consume"/> baixa o reservado (a OS foi executada).
/// Erros de regra voltam como <see cref="Result"/>, nunca como exceção.
/// </summary>
public sealed class Part : AggregateRoot<PartId>
{
    public const int MinimumStockQuantity = 5;

    public string Name { get; private set; }
    public string Description { get; private set; }
    public string PartNumber { get; private set; }
    public string Manufacturer { get; private set; }
    public int PriceCents { get; private set; }
    public int Quantity { get; private set; }
    public int ReservedQuantity { get; private set; }
    public DateTime? UpdatedOn { get; private set; }

    private Part(PartId id, string name, string description, string partNumber, string manufacturer, int priceCents, int quantity)
        : base(id)
    {
        Name = name;
        Description = description;
        PartNumber = partNumber;
        Manufacturer = manufacturer;
        PriceCents = priceCents;
        Quantity = quantity;
        ReservedQuantity = 0;
    }

    // Construtor do EF Core.
    private Part() : base(PartId.New())
    {
        Name = null!;
        Description = null!;
        PartNumber = null!;
        Manufacturer = null!;
    }

    public static Result<Part> Create(
        string name, string description, string partNumber, string manufacturer, int priceCents, int quantity) =>
        Create(PartId.New(), name, description, partNumber, manufacturer, priceCents, quantity);

    /// <summary>Cria com id conhecido. Usado pelo seed, que precisa de GUIDs fixos entre os serviços.</summary>
    public static Result<Part> Create(
        PartId id, string name, string description, string partNumber, string manufacturer, int priceCents, int quantity)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Part>(Error.Validation("Part.NameRequired", "Nome da peça é obrigatório."));
        if (priceCents < 0)
            return Result.Failure<Part>(Error.Validation("Part.PriceNegative", "Preço não pode ser negativo."));
        if (quantity < 0)
            return Result.Failure<Part>(Error.Validation("Part.QuantityNegative", "Quantidade não pode ser negativa."));

        return Result.Success(new Part(id, name.Trim(), description?.Trim() ?? string.Empty,
            partNumber, manufacturer, priceCents, quantity));
    }

    public bool IsStockBelowMinimum() => Quantity <= MinimumStockQuantity;

    public bool HasAvailability(int quantity) => Quantity >= quantity;

    public Result Reserve(int quantity)
    {
        if (quantity <= 0)
            return Result.Failure(Error.Validation("Part.ReserveQuantityInvalid", "A quantidade reservada deve ser maior que zero."));
        if (Quantity < quantity)
            return Result.Failure(Error.Conflict("Part.InsufficientStock",
                $"Não há disponibilidade no estoque da peça {PartNumber}. No estoque: {Quantity}, Reservadas: {ReservedQuantity}, Pedido: {quantity}"));

        Quantity -= quantity;
        ReservedQuantity += quantity;
        UpdatedOn = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>Desfaz uma reserva: reservado → disponível. Compensação da Saga.</summary>
    public Result Release(int quantity)
    {
        if (quantity <= 0)
            return Result.Failure(Error.Validation("Part.ReleaseQuantityInvalid", "A quantidade liberada deve ser maior que zero."));
        if (ReservedQuantity < quantity)
            return Result.Failure(Error.Conflict("Part.InsufficientReserved",
                $"Peça {PartNumber} não possui quantidade reservada suficiente. Reservado: {ReservedQuantity}, Solicitado: {quantity}"));

        ReservedQuantity -= quantity;
        Quantity += quantity;
        UpdatedOn = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Consume(int quantity)
    {
        if (quantity <= 0)
            return Result.Failure(Error.Validation("Part.ConsumeQuantityInvalid", "A quantidade consumida deve ser maior que zero."));
        if (ReservedQuantity < quantity)
            return Result.Failure(Error.Conflict("Part.InsufficientReserved",
                $"Peça {PartNumber} não possui quantidade reservada suficiente. Reservado: {ReservedQuantity}, Solicitado: {quantity}"));

        ReservedQuantity -= quantity;
        UpdatedOn = DateTime.UtcNow;
        return Result.Success();
    }

    public Result AddQuantity(int quantity)
    {
        if (quantity <= 0)
            return Result.Failure(Error.Validation("Part.AddQuantityInvalid", "A quantidade a adicionar deve ser maior que zero."));

        Quantity += quantity;
        UpdatedOn = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>Peça com reserva ativa não pode ser removida: alguma OS em andamento depende dela.</summary>
    public Result EnsureCanBeRemoved() =>
        ReservedQuantity > 0
            ? Result.Failure(Error.Conflict("Part.HasActiveReservations",
                $"A peça {PartNumber} tem {ReservedQuantity} unidade(s) reservada(s) por OS em andamento e não pode ser removida."))
            : Result.Success();

    // Mantido como no monólito (sem validação). Sobrescreve o disponível direto; ver "pontos de atenção" no README.
    public void UpdateDetails(string name, string description, string partNumber, string manufacturer, int priceCents, int quantity)
    {
        Name = name;
        Description = description;
        PartNumber = partNumber;
        Manufacturer = manufacturer;
        PriceCents = priceCents;
        Quantity = quantity;
        UpdatedOn = DateTime.UtcNow;
    }
}
