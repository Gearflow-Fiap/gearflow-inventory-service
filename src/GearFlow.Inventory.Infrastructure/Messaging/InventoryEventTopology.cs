using GearFlow.Contracts;
using MassTransit;

namespace GearFlow.Inventory.Infrastructure.Messaging;

/// <summary>Altera apenas os seis eventos do Inventory; comandos e tipos do framework (ex.: <c>Fault&lt;T&gt;</c>) usam o padrão existente.</summary>
public sealed class InventoryEventEntityNameFormatter(IEntityNameFormatter fallback) : IEntityNameFormatter
{
    public string FormatEntityName<T>() => InventoryEventNames.All.TryGetValue(typeof(T), out var name)
        ? name : fallback.FormatEntityName<T>();
}

public static class InventoryEventTopology
{
    /// <summary>Chamar antes de qualquer <c>ReceiveEndpoint</c>: os binds das filas leem o nome da exchange aqui.</summary>
    public static void ConfigureInventoryEventNames(this IRabbitMqBusFactoryConfigurator configuration) =>
        configuration.MessageTopology.SetEntityNameFormatter(new InventoryEventEntityNameFormatter(configuration.MessageTopology.EntityNameFormatter));
}
