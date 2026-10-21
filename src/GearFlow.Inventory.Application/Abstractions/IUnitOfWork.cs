namespace GearFlow.Inventory.Application.Abstractions;

/// <summary>
/// Confirma a transação local. Regra do handler: <b>mutar os agregados, publicar as mensagens e só então
/// chamar <see cref="SaveChangesAsync"/></b>. Com o Outbox, as mensagens publicadas entram na mesma
/// transação do banco e saem para o broker depois do commit.
/// </summary>
public interface IUnitOfWork
{
    /// <exception cref="ConcurrencyConflictException">Outro processo alterou os mesmos dados antes do commit.</exception>
    Task SaveChangesAsync(CancellationToken ct = default);
}
