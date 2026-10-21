namespace GearFlow.Inventory.Domain.Reservations;

/// <summary>
/// Ciclo de vida da reserva de uma OS: <c>Reserved → Released</c> (compensação) ou
/// <c>Reserved → Consumed</c> (OS executada, ponto sem volta). <c>Released</c> e <c>Consumed</c> são finais.
/// </summary>
public enum ReservationStatus
{
    Reserved = 1,
    Released = 2,
    Consumed = 3,
}
