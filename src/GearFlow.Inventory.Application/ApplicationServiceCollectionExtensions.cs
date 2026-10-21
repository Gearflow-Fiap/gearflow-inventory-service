using FluentValidation;
using GearFlow.Inventory.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace GearFlow.Inventory.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registra por varredura do assembly todos os command/query handlers e os validators
    /// (inclusive os <c>internal</c>). O <see cref="IDispatcher"/> é registrado na Infrastructure.
    /// Os decorators (<c>ValidationDecorator&lt;,&gt;</c> etc.) também implementam as interfaces de handler,
    /// mas são genéricos abertos e montados pelo Dispatcher: ficam fora da varredura.
    /// </summary>
    public static IServiceCollection AddInventoryApplication(this IServiceCollection services)
    {
        var assembly = typeof(ApplicationServiceCollectionExtensions).Assembly;

        var handlers = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .SelectMany(t => t.GetInterfaces()
                .Where(i => i.IsGenericType && IsHandlerInterface(i.GetGenericTypeDefinition()))
                .Select(i => (Service: i, Implementation: t)));

        foreach (var (service, implementation) in handlers)
            services.AddScoped(service, implementation);

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        return services;
    }

    private static bool IsHandlerInterface(Type definition) =>
        definition == typeof(ICommandHandler<,>) || definition == typeof(IQueryHandler<,>);
}
