using System.Reflection;

namespace GearFlow.Inventory.Api.Endpoints;

[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] // descoberta/registro de endpoints (wiring)
public static class EndpointExtensions
{
    /// <summary>
    /// Descobre todas as implementações de <see cref="IEndpoint"/> nos assemblies dados e as registra.
    /// No Program.cs: <c>app.MapEndpoints(typeof(IApiMarker).Assembly)</c>.
    /// </summary>
    public static WebApplication MapEndpoints(this WebApplication app, params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            var endpointTypes = assembly.GetTypes()
                .Where(t => typeof(IEndpoint).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false });

            foreach (var type in endpointTypes)
            {
                var endpoint = (IEndpoint)Activator.CreateInstance(type)!;
                endpoint.MapEndpoints(app);
            }
        }

        return app;
    }
}
