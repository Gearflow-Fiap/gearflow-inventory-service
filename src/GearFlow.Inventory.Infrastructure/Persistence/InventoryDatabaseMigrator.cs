using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GearFlow.Inventory.Infrastructure.Persistence;

/// <summary>
/// Aplica as migrations no startup de forma segura para várias réplicas subindo juntas.
/// </summary>
/// <remarks>
/// O Deployment tem 2 réplicas e a migration roda no <c>Program.cs</c>. Num banco vazio, as duas tentam criar as mesmas tabelas
/// ao mesmo tempo e uma cai com <c>42P07 relation "InboxState" already exists</c> (o K8s reinicia e resolve, mas o Pod falha e
/// gera ruído). O <c>MigrateAsync</c> do provider Npgsql não serializa as réplicas, então a exclusão mútua é feita aqui com um
/// <i>advisory lock</i> do PostgreSQL: quem chega depois espera a primeira terminar, encontra tudo aplicado e segue.
/// O lock é de SESSÃO e usa uma conexão própria, que fica aberta durante toda a migration (a do EF pode ser fechada e reaberta
/// pelo pool no meio do caminho). Se o processo morrer, o PostgreSQL derruba a sessão e libera o lock sozinho.
/// </remarks>
public static class InventoryDatabaseMigrator
{
    /// <summary>Chave fixa do lock ("GearFlow" em ASCII). Só concorre com o próprio Inventory: cada serviço tem o seu banco.</summary>
    public const long AdvisoryLockKey = 0x47656172_466C6F77;

    /// <summary>Quanto esperar o lock: acima da migration mais lenta esperada, abaixo da janela do startupProbe (60 x 2 s).</summary>
    public static readonly TimeSpan DefaultLockTimeout = TimeSpan.FromSeconds(90);

    public static async Task MigrateAsync(
        InventoryDbContext db, TimeSpan? lockTimeout = null, CancellationToken cancellationToken = default)
    {
        var connectionString = db.Database.GetConnectionString()
            ?? throw new InvalidOperationException("O DbContext do Inventory não tem connection string.");

        await using var lockConnection = new NpgsqlConnection(connectionString);
        await lockConnection.OpenAsync(cancellationToken);
        await AcquireAsync(lockConnection, lockTimeout ?? DefaultLockTimeout, cancellationToken);

        try
        {
            await db.Database.MigrateAsync(cancellationToken);
        }
        finally
        {
            await ReleaseAsync(lockConnection);
        }
    }

    private static async Task AcquireAsync(NpgsqlConnection connection, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            await using var command = new NpgsqlCommand("SELECT pg_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", AdvisoryLockKey);
            await command.ExecuteNonQueryAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Não consegui o lock das migrations em {timeout.TotalSeconds:0} s: outra réplica segue migrando o banco.");
        }
    }

    private static async Task ReleaseAsync(NpgsqlConnection connection)
    {
        try
        {
            await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(@key)", connection);
            command.Parameters.AddWithValue("key", AdvisoryLockKey);
            await command.ExecuteNonQueryAsync();
        }
        catch (NpgsqlException)
        {
            // A conexão já caiu: ao encerrar a sessão o PostgreSQL libera o lock. Não mascaramos o erro da migration.
        }
    }
}
