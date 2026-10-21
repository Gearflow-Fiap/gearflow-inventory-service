using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GearFlow.Inventory.Infrastructure.Persistence;

/// <summary>
/// Usada só pelo <c>dotnet ef</c> (gerar migrations/scripts) sem subir a Api. Não abre conexão ao gerar migration.
/// Para aplicar no banco: <c>ConnectionStrings__Postgres=... dotnet ef database update</c>.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5433;Database=gearflow_inventory;Username=gearflow;Password=gearflow_dev";

        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new InventoryDbContext(options);
    }
}
