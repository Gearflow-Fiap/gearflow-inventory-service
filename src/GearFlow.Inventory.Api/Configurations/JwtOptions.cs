using System.ComponentModel.DataAnnotations;

namespace GearFlow.Inventory.Api.Configurations;

/// <summary>
/// Config tipada do JWT (seção "Jwt"), validada no boot. É a MESMA chave HMAC do monólito e da Lambda
/// de CPF: o Kong valida o token na borda e o serviço revalida (defesa em profundidade).
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>Chave HMAC-SHA256. Mínimo 32 caracteres. Vem de Secret do K8s, nunca do repositório.</summary>
    [Required(AllowEmptyStrings = false)]
    [MinLength(32, ErrorMessage = "Jwt:Secret deve ter no mínimo 32 caracteres (HMAC-SHA256).")]
    public string Secret { get; init; } = string.Empty;

    public string Issuer { get; init; } = "GearFlow.Api";

    public string Audience { get; init; } = "GearFlow.Client";
}
