using FluentAssertions;
using GearFlow.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GearFlow.Inventory.UnitTests.Infrastructure;

/// <summary>Quais erros do Postgres viram "conflito, pode repetir". Os caminhos reais estão nos testes de integração.</summary>
public sealed class EfUnitOfWorkTests
{
    private static PostgresException Postgres(string sqlState) =>
        new("erro simulado", "ERROR", "ERROR", sqlState);

    [Theory]
    [InlineData("40001")] // falha de serialização (transação Serializable do Outbox)
    [InlineData("40P01")] // deadlock
    [InlineData("23505")] // violação de unicidade
    public void Conflict_errors_are_recognised_even_when_wrapped_by_ef(string sqlState)
    {
        var wrapped = new InvalidOperationException(
            "transient failure", new DbUpdateException("save failed", Postgres(sqlState)));

        EfUnitOfWork.IsRetriableDatabaseConflict(wrapped).Should().BeTrue();
    }

    [Fact]
    public void A_bare_postgres_exception_is_recognised()
    {
        EfUnitOfWork.IsRetriableDatabaseConflict(Postgres("40001")).Should().BeTrue();
    }

    [Theory]
    [InlineData("23503")] // violação de chave estrangeira: bug, repetir não resolve
    [InlineData("42P01")] // tabela inexistente
    [InlineData("28000")] // credencial inválida
    public void Other_database_errors_are_not_treated_as_conflicts(string sqlState)
    {
        var wrapped = new DbUpdateException("save failed", Postgres(sqlState));

        EfUnitOfWork.IsRetriableDatabaseConflict(wrapped).Should().BeFalse();
    }

    [Fact]
    public void Unrelated_exceptions_are_not_conflicts()
    {
        EfUnitOfWork.IsRetriableDatabaseConflict(new InvalidOperationException("outra coisa")).Should().BeFalse();
        EfUnitOfWork.IsRetriableDatabaseConflict(new TimeoutException()).Should().BeFalse();
    }
}
