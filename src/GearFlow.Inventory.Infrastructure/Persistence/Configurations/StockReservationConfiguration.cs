using GearFlow.Inventory.Domain.Reservations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GearFlow.Inventory.Infrastructure.Persistence.Configurations;

internal sealed class StockReservationConfiguration : IEntityTypeConfiguration<StockReservation>
{
    public void Configure(EntityTypeBuilder<StockReservation> builder)
    {
        builder.ToTable("stock_reservations");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => StockReservationId.From(value));

        builder.Property(r => r.ServiceOrderId).HasColumnName("service_order_id").IsRequired();
        builder.Property(r => r.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.CreatedOn).HasColumnName("created_on");
        builder.Property(r => r.UpdatedOn).HasColumnName("updated_on");

        // Uma reserva por OS. É o que torna o ReserveStock idempotente mesmo com duas mensagens
        // concorrentes: a segunda viola o índice, o consumer tenta de novo e já enxerga a reserva.
        builder.HasIndex(r => r.ServiceOrderId).IsUnique();

        builder.Property<uint>("xmin").IsRowVersion();

        builder.OwnsMany(r => r.Lines, lines =>
        {
            lines.ToTable("stock_reservation_lines");

            lines.WithOwner().HasForeignKey("reservation_id");
            lines.HasKey("reservation_id", nameof(StockReservationLine.ItemType), nameof(StockReservationLine.ItemId));

            lines.Property(l => l.ItemType).HasColumnName("item_type").HasConversion<string>().HasMaxLength(20);
            lines.Property(l => l.ItemId).HasColumnName("item_id");
            lines.Property(l => l.ItemName).HasColumnName("item_name").HasMaxLength(200).IsRequired();
            lines.Property(l => l.Quantity).HasColumnName("quantity").HasPrecision(18, 3);
        });

        builder.Navigation(r => r.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
