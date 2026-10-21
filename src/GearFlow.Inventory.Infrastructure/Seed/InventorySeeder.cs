using GearFlow.Inventory.Domain.Core;
using GearFlow.Inventory.Domain.Stock;
using GearFlow.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GearFlow.Inventory.Infrastructure.Seed;

/// <summary>
/// Massa de demonstração (10 peças e 7 insumos, a mesma do <c>DevDataSeeder</c> do monólito), com IDs
/// fixos (<see cref="DemoCatalogIds"/>). Idempotente: só semeia se o estoque estiver vazio.
/// Ligado por <c>Seed__EnableDevData=true</c> (ou ambiente Development).
/// </summary>
public static class InventorySeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        if (await db.Parts.AnyAsync(ct) || await db.Consumables.AnyAsync(ct))
            return;

        db.Parts.AddRange(
            APart(DemoCatalogIds.FiltroDeOleo, "Filtro de óleo", "Motores 1.0 a 1.6", "FL-1234", "Fram", 3500, 40),
            APart(DemoCatalogIds.PastilhaDeFreioDianteira, "Pastilha de freio dianteira", "HB20 / Onix / Gol", "PF-2201", "Bosch", 12000, 25),
            APart(DemoCatalogIds.KitCorreiaDentada, "Kit correia dentada", "Com tensor e polia", "CT-908", "Gates", 22000, 12),
            APart(DemoCatalogIds.Bateria60Ah, "Bateria 60Ah", "Livre de manutenção", "M60GD", "Moura", 42000, 15),
            APart(DemoCatalogIds.AmortecedorDianteiro, "Amortecedor dianteiro", "Gol / Fox / Voyage", "AM-330", "Cofap", 24000, 8),
            APart(DemoCatalogIds.VelaDeIgnicao, "Vela de ignição", "Iridium", "VI-6", "NGK", 4500, 60),
            APart(DemoCatalogIds.FiltroDeAr, "Filtro de ar", "Elemento filtrante", "FA-77", "Tecfil", 5500, 30),
            APart(DemoCatalogIds.DiscoDeFreioVentilado, "Disco de freio ventilado", "Par dianteiro", "DF-410", "Fremax", 28000, 10),
            APart(DemoCatalogIds.CorreiaDoAlternador, "Correia do alternador", "Poly-V", "CA-55", "Dayco", 3800, 20),
            APart(DemoCatalogIds.JogoDePalhetas, "Jogo de palhetas", "Par dianteiro", "PL-22", "Bosch", 4000, 4)); // abaixo do mínimo (5)

        db.Consumables.AddRange(
            AConsumable(DemoCatalogIds.Oleo5W30Sintetico, "Óleo 5W30 sintético (L)", 4500, 80m),
            AConsumable(DemoCatalogIds.Oleo15W40Semissintetico, "Óleo 15W40 semissintético (L)", 3200, 60m),
            AConsumable(DemoCatalogIds.FluidoDeFreioDot4, "Fluido de freio DOT4 (500ml)", 2500, 30m),
            AConsumable(DemoCatalogIds.AditivoParaRadiador, "Aditivo para radiador (L)", 2800, 25m),
            AConsumable(DemoCatalogIds.GraxaMultiuso, "Graxa multiuso (kg)", 3500, 15m),
            AConsumable(DemoCatalogIds.LimpaContatos, "Limpa contatos (spray)", 1900, 20m),
            AConsumable(DemoCatalogIds.AguaDesmineralizada, "Água desmineralizada (L)", 800, 4m)); // abaixo do mínimo (5)

        await db.SaveChangesAsync(ct);
    }

    private static Part APart(
        Guid id, string name, string description, string partNumber, string manufacturer, int priceCents, int quantity) =>
        Part.Create(PartId.From(id), name, description, partNumber, manufacturer, priceCents, quantity).Value;

    private static Consumable AConsumable(Guid id, string name, int unitPriceCents, decimal quantity) =>
        Consumable.Create(ConsumableId.From(id), name, unitPriceCents, quantity).Value;
}
