using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace GearFlow.Inventory.IntegrationTests.Fixtures;

/// <summary>JWT de staff assinado com a mesma chave HMAC que a API de teste valida (issuer e audience do cluster).</summary>
internal static class TestTokens
{
    public static string Staff() =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "GearFlow.Api",
            Audience = "GearFlow.Client",
            Expires = DateTime.UtcNow.AddMinutes(10),
            Subject = new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())]),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(InventoryApiFactory.JwtSecret)),
                SecurityAlgorithms.HmacSha256),
        });
}
