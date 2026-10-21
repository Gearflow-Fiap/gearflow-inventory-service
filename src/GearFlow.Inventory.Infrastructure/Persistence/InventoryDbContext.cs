using GearFlow.Inventory.Domain.Reservations;
using GearFlow.Inventory.Domain.Stock;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace GearFlow.Inventory.Infrastructure.Persistence;

public sealed class InventoryDbContext(DbContextOptions<InventoryDbContext> options) : DbContext(options)
{
    public DbSet<Part> Parts => Set<Part>();
    public DbSet<Consumable> Consumables => Set<Consumable>();
    public DbSet<StockReservation> StockReservations => Set<StockReservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("inventory");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InventoryDbContext).Assembly);

        // Tabelas do MassTransit no mesmo banco (e na mesma transação) do estoque:
        // OutboxMessage/OutboxState (publicação atômica) e InboxState (deduplicação por MessageId).
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();
    }
}
