using GearFlow.Inventory.Domain.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GearFlow.Inventory.Infrastructure.Persistence.Configurations;

internal sealed class PartConfiguration : IEntityTypeConfiguration<Part>
{
    public void Configure(EntityTypeBuilder<Part> builder)
    {
        builder.ToTable("parts");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => PartId.From(value));

        builder.Property(p => p.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(p => p.Description).HasColumnName("description").HasMaxLength(1000);
        builder.Property(p => p.PartNumber).HasColumnName("part_number").HasMaxLength(100);
        builder.Property(p => p.Manufacturer).HasColumnName("manufacturer").HasMaxLength(120);
        builder.Property(p => p.PriceCents).HasColumnName("price_cents");
        builder.Property(p => p.Quantity).HasColumnName("quantity");
        builder.Property(p => p.ReservedQuantity).HasColumnName("reserved_quantity");
        builder.Property(p => p.UpdatedOn).HasColumnName("updated_on");

        // Concorrência otimista: o xmin do Postgres muda a cada UPDATE da linha. Duas reservas disputando
        // a última unidade: a segunda a gravar falha (DbUpdateConcurrencyException) em vez de estourar o saldo.
        // Shadow property: o domínio não conhece o token.
        builder.Property<uint>("xmin").IsRowVersion();

        builder.HasIndex(p => p.PartNumber);
    }
}
