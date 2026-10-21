using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace GearFlow.Inventory.Api.OpenApi;

/// <summary>
/// Declara o esquema Bearer (JWT) no documento OpenAPI. Sem isto o Scalar não mostra o botão "Authorize"
/// e as chamadas a endpoints protegidos vão sem o header Authorization.
/// </summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] // setup do OpenAPI
internal sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    private const string SchemeId = "Bearer";

    public Task TransformAsync(
        OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes[SchemeId] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "JWT de staff. Faça login em POST /api/identity/auth/login (OS/Identity) e cole aqui só o accessToken (sem 'Bearer ').",
        };

        document.Security ??= new List<OpenApiSecurityRequirement>();
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SchemeId, document)] = new List<string>(),
        });

        return Task.CompletedTask;
    }
}
