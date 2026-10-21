using GearFlow.Inventory.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GearFlow.Inventory.Infrastructure.Persistence;

/// <summary>
/// Commit da transação local. Traduz os conflitos esperados sob concorrência para
/// <see cref="ConcurrencyConflictException"/>, que o consumer deixa subir para o retry (e o endpoint HTTP
/// devolve como 409):
/// <list type="bullet">
/// <item><see cref="DbUpdateConcurrencyException"/>: outro processo alterou o mesmo item/reserva (o <c>xmin</c> mudou).</item>
/// <item>SQLSTATE 40001 (falha de serialização) e 40P01 (deadlock): a transação do consumer roda em isolamento
/// Serializable (padrão do Outbox do MassTransit), então a disputa pela mesma linha chega assim, e não como
/// <see cref="DbUpdateConcurrencyException"/>.</item>
/// <item>SQLSTATE 23505 (violação de unicidade): duas mensagens criando a reserva da mesma OS, ou o Inbox recebendo
/// a mesma mensagem duas vezes ao mesmo tempo.</item>
/// </list>
/// </summary>
internal sealed class EfUnitOfWork(InventoryDbContext db) : IUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException("Conflito de concorrência ao gravar o estoque.", ex);
        }
        catch (Exception ex) when (IsRetriableDatabaseConflict(ex))
        {
            throw new ConcurrencyConflictException("Conflito no banco ao gravar o estoque (a operação pode ser repetida).", ex);
        }
    }

    /// <summary>
    /// Procura o erro do Postgres em qualquer ponto da cadeia: o EF o embrulha em <see cref="DbUpdateException"/> e,
    /// dependendo da estratégia de execução, ainda em <see cref="InvalidOperationException"/>.
    /// </summary>
    internal static bool IsRetriableDatabaseConflict(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException
                {
                    SqlState: PostgresErrorCodes.SerializationFailure
                           or PostgresErrorCodes.DeadlockDetected
                           or PostgresErrorCodes.UniqueViolation,
                })
                return true;
        }

        return false;
    }
}
