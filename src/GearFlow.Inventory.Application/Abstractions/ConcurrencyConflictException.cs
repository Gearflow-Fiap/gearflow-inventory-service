namespace GearFlow.Inventory.Application.Abstractions;

/// <summary>
/// Conflito de concorrência no commit: outra transação alterou o mesmo item de estoque, ou duas
/// mensagens tentaram criar a reserva da mesma OS ao mesmo tempo. Não é erro de negócio: o consumer
/// deixa a exceção subir para o retry, e na reexecução o handler já enxerga o estado novo.
/// </summary>
public sealed class ConcurrencyConflictException : Exception
{
    public ConcurrencyConflictException(string message, Exception? innerException = null)
        : base(message, innerException) { }
}
