using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Infrastructure.Messaging;

/// <summary>
/// Falha que repetir não resolve (mensagem malformada, reserva inexistente, conflito de estado). O retry
/// ignora esta exceção e a mensagem vai direto para a fila <c>*_error</c> (DLQ), visível para quem opera o
/// serviço, em vez de gastar as tentativas. Falha transitória (banco fora, conflito de concorrência) usa
/// as outras exceções e é repetida.
/// </summary>
public sealed class PermanentMessageException(Error error)
    : Exception($"{error.Code}: {error.Description}")
{
    public Error Error { get; } = error;
}

internal static class ResultMessagingExtensions
{
    /// <summary>Converte um <see cref="Result"/> de falha em <see cref="PermanentMessageException"/>.</summary>
    public static void ThrowIfFailure(this Result result)
    {
        if (result.IsFailure)
            throw new PermanentMessageException(result.Error);
    }
}
