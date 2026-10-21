namespace GearFlow.Inventory.Infrastructure.Seed;

/// <summary>
/// IDs FIXOS do catálogo de demonstração. No monólito o seed gerava GUIDs aleatórios e ligava os
/// orçamentos às peças por referência. Com serviços separados, o seed do OS Service e do Operation
/// Service precisa apontar para os mesmos IDs, então eles são determinísticos e documentados no README.
/// Peças: prefixo <c>a1…</c>. Insumos: prefixo <c>b2…</c>.
/// </summary>
public static class DemoCatalogIds
{
    // Peças
    public static readonly Guid FiltroDeOleo = new("a1000000-0000-0000-0000-000000000001");
    public static readonly Guid PastilhaDeFreioDianteira = new("a1000000-0000-0000-0000-000000000002");
    public static readonly Guid KitCorreiaDentada = new("a1000000-0000-0000-0000-000000000003");
    public static readonly Guid Bateria60Ah = new("a1000000-0000-0000-0000-000000000004");
    public static readonly Guid AmortecedorDianteiro = new("a1000000-0000-0000-0000-000000000005");
    public static readonly Guid VelaDeIgnicao = new("a1000000-0000-0000-0000-000000000006");
    public static readonly Guid FiltroDeAr = new("a1000000-0000-0000-0000-000000000007");
    public static readonly Guid DiscoDeFreioVentilado = new("a1000000-0000-0000-0000-000000000008");
    public static readonly Guid CorreiaDoAlternador = new("a1000000-0000-0000-0000-000000000009");

    /// <summary>Começa com 4 unidades (abaixo do mínimo de 5): serve para forçar <c>StockInsufficient</c> na demo.</summary>
    public static readonly Guid JogoDePalhetas = new("a1000000-0000-0000-0000-000000000010");

    // Insumos
    public static readonly Guid Oleo5W30Sintetico = new("b2000000-0000-0000-0000-000000000001");
    public static readonly Guid Oleo15W40Semissintetico = new("b2000000-0000-0000-0000-000000000002");
    public static readonly Guid FluidoDeFreioDot4 = new("b2000000-0000-0000-0000-000000000003");
    public static readonly Guid AditivoParaRadiador = new("b2000000-0000-0000-0000-000000000004");
    public static readonly Guid GraxaMultiuso = new("b2000000-0000-0000-0000-000000000005");
    public static readonly Guid LimpaContatos = new("b2000000-0000-0000-0000-000000000006");

    /// <summary>Começa com 4 litros (abaixo do mínimo de 5): serve para forçar <c>StockInsufficient</c> na demo.</summary>
    public static readonly Guid AguaDesmineralizada = new("b2000000-0000-0000-0000-000000000007");
}
