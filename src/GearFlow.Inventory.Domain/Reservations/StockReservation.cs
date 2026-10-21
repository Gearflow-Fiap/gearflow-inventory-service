using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Domain.Reservations;

/// <summary>
/// Reserva de estoque de UMA ordem de serviço (<see cref="ServiceOrderId"/> é único). É o registro que
/// o monólito não tinha: sem ele a Saga não consegue compensar (<c>ReleaseStock</c>) nem tolerar
/// mensagem duplicada, porque os contadores de <c>Part</c>/<c>Consumable</c> não dizem quem reservou o quê.
///
/// A reserva também pode nascer já <see cref="ReservationStatus.Released"/> sem linhas
/// (<see cref="CreateReleased"/>): é o "tombstone" que protege contra um <c>ReserveStock</c> atrasado
/// que chegue depois do <c>ReleaseStock</c> (mensagem fora de ordem).
/// </summary>
public sealed class StockReservation : AggregateRoot<StockReservationId>
{
    private readonly List<StockReservationLine> _lines = [];

    public Guid ServiceOrderId { get; private set; }
    public ReservationStatus Status { get; private set; }
    public DateTime CreatedOn { get; private set; }
    public DateTime UpdatedOn { get; private set; }
    public IReadOnlyList<StockReservationLine> Lines => _lines;

    // Construtor do EF Core.
    private StockReservation() : base(StockReservationId.New()) { }

    private StockReservation(Guid serviceOrderId, ReservationStatus status, DateTime now)
        : base(StockReservationId.New())
    {
        ServiceOrderId = serviceOrderId;
        Status = status;
        CreatedOn = now;
        UpdatedOn = now;
    }

    public static Result<StockReservation> Create(
        Guid serviceOrderId, IEnumerable<StockReservationLine> lines, DateTime now)
    {
        if (serviceOrderId == Guid.Empty)
            return Result.Failure<StockReservation>(
                Error.Validation("Reservation.ServiceOrderRequired", "O id da ordem de serviço é obrigatório."));

        var list = lines.ToList();
        if (list.Count == 0)
            return Result.Failure<StockReservation>(
                Error.Validation("Reservation.ItemsRequired", "A reserva precisa de pelo menos um item."));

        var duplicated = list.GroupBy(l => (l.ItemType, l.ItemId)).FirstOrDefault(g => g.Count() > 1);
        if (duplicated is not null)
            return Result.Failure<StockReservation>(
                Error.Validation("Reservation.DuplicateItem",
                    $"O item {duplicated.Key.ItemId} ({duplicated.Key.ItemType}) aparece mais de uma vez na reserva."));

        var reservation = new StockReservation(serviceOrderId, ReservationStatus.Reserved, now);
        reservation._lines.AddRange(list);
        return Result.Success(reservation);
    }

    /// <summary>Tombstone: reserva já liberada e sem linhas, criada quando o <c>ReleaseStock</c> chega antes do <c>ReserveStock</c>.</summary>
    public static StockReservation CreateReleased(Guid serviceOrderId, DateTime now) =>
        new(serviceOrderId, ReservationStatus.Released, now);

    /// <summary>
    /// Reserved → Released. Idempotente: liberar o que já foi liberado é sucesso. Liberar uma reserva
    /// já consumida é conflito: depois que a execução termina não há compensação.
    /// </summary>
    public Result Release(DateTime now)
    {
        switch (Status)
        {
            case ReservationStatus.Released:
                return Result.Success();
            case ReservationStatus.Consumed:
                return Result.Failure(Error.Conflict("Reservation.AlreadyConsumed",
                    $"A reserva da OS {ServiceOrderId} já foi consumida e não pode ser liberada."));
            default:
                Status = ReservationStatus.Released;
                UpdatedOn = now;
                return Result.Success();
        }
    }

    /// <summary>
    /// Reserved → Consumed. Idempotente: consumir o que já foi consumido é sucesso. Consumir uma reserva
    /// liberada é conflito.
    /// </summary>
    public Result Consume(DateTime now)
    {
        switch (Status)
        {
            case ReservationStatus.Consumed:
                return Result.Success();
            case ReservationStatus.Released:
                return Result.Failure(Error.Conflict("Reservation.AlreadyReleased",
                    $"A reserva da OS {ServiceOrderId} foi liberada e não pode ser consumida."));
            default:
                Status = ReservationStatus.Consumed;
                UpdatedOn = now;
                return Result.Success();
        }
    }
}
