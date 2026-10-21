namespace GearFlow.Inventory.Application.Notifications;

/// <summary>
/// Configuração dos avisos de estoque (seção <c>Stock</c>). No monólito o endereço do estoquista era uma
/// constante dentro do handler (<c>estoque@gearflow.com</c>); aqui vira configuração.
/// </summary>
public sealed class StockAlertOptions
{
    public const string SectionName = "Stock";

    /// <summary>Quem recebe o aviso de estoque baixo e de estoque faltante.</summary>
    public string AlertEmail { get; init; } = "estoque@gearflow.com";
}
