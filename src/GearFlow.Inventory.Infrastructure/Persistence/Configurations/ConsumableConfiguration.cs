using GearFlow.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GearFlow.Inventory.Infrastructure.Persistence.Configurations;

internal sealed class ConsumableConfiguration : IEntityTypeConfiguration<Consumable>
{
    public void Configure(EntityTypeBuilder<Consumable> builder)
    {
        builder.ToTable("consumables");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => ConsumableId.From(value));

        builder.Property(c => c.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(c => c.UnitPriceCents).HasColumnName("unit_price_cents");
        builder.Property(c => c.Quantity).HasColumnName("quantity").HasPrecision(18, 3);
        builder.Property(c => c.ReservedQuantity).HasColumnName("reserved_quantity").HasPrecision(18, 3);
        builder.Property(c => c.UpdatedOn).HasColumnName("updated_on");

        // Concorrência otimista (ver PartConfiguration).
        builder.Property<uint>("xmin").IsRowVersion();
    }
}
