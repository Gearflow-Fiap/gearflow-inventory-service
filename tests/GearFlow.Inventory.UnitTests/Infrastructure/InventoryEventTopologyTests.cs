using System.Text.RegularExpressions;
using FluentAssertions;
using GearFlow.Contracts;
using GearFlow.Inventory.Infrastructure.Messaging;
using MassTransit;

namespace GearFlow.Inventory.UnitTests.Infrastructure;

/// <summary>
/// Convenção de nomes do grupo (mesma do Operation): eventos em exchanges <c>&lt;serviço&gt;.&lt;fato&gt;</c> e comandos em filas
/// <c>&lt;serviço&gt;-&lt;comando&gt;</c>, tudo em kebab-case, com o prefixo do repo sem <c>gearflow-</c> e sem <c>-service</c>.
/// </summary>
public sealed partial class InventoryEventTopologyTests
{
    private sealed class ExistingNames : IEntityNameFormatter
    {
        public string FormatEntityName<T>() => "existing:" + typeof(T).FullName;
    }

    private static readonly Type[] Commands = [typeof(ReserveStock), typeof(ReleaseStock), typeof(ConsumeStock)];

    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])")]
    private static partial Regex WordBoundary();

    private static string Kebab(string typeName) => WordBoundary().Replace(typeName, "-").ToLowerInvariant();

    [Fact]
    public void Every_event_has_its_agreed_exchange_name()
    {
        var expected = new Dictionary<Type, string>
        {
            [typeof(StockReserved)] = "inventory.stock-reserved",
            [typeof(StockInsufficient)] = "inventory.stock-insufficient",
            [typeof(StockReleased)] = "inventory.stock-released",
            [typeof(StockConsumed)] = "inventory.stock-consumed",
            [typeof(StockReplenished)] = "inventory.stock-replenished",
            [typeof(LowStockAlert)] = "inventory.low-stock-alert",
        };

        InventoryEventNames.All.Should().BeEquivalentTo(expected);
        InventoryEventNames.All.Values.Distinct().Should().HaveCount(expected.Count);
    }

    [Fact]
    public void Event_names_follow_the_service_prefix_and_the_kebab_case_of_the_type()
    {
        foreach (var (type, exchange) in InventoryEventNames.All)
        {
            exchange.Should().MatchRegex(@"^inventory\.[a-z]+(-[a-z]+)*$", $"{type.Name} segue <serviço>.<fato>");
            exchange.Should().Be("inventory." + Kebab(type.Name), $"o fato é o nome de {type.Name} em kebab-case");
            type.Namespace.Should().Be("GearFlow.Contracts", "o nome no broker não altera o namespace/URN da mensagem");
        }
    }

    [Fact]
    public void Every_published_event_is_named_and_no_command_is()
    {
        // Mensagem da Saga = record de GearFlow.Contracts com MessageId. O que não é comando é evento.
        var messages = typeof(InventoryEventNames).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsPublic: true, Namespace: "GearFlow.Contracts" } && t.GetProperty("MessageId") is not null)
            .ToArray();

        messages.Where(t => !Commands.Contains(t)).Should().BeEquivalentTo(InventoryEventNames.All.Keys,
            "todo evento novo precisa de um nome de exchange em InventoryEventNames");
        InventoryEventNames.All.Keys.Should().NotIntersectWith(Commands);
    }

    [Theory]
    [InlineData(typeof(ReserveStock), QueueNames.ReserveStock)]
    [InlineData(typeof(ReleaseStock), QueueNames.ReleaseStock)]
    [InlineData(typeof(ConsumeStock), QueueNames.ConsumeStock)]
    public void Command_queues_are_the_service_prefix_plus_the_kebab_case_of_the_command(Type command, string queue) =>
        queue.Should().Be("inventory-" + Kebab(command.Name));

    [Fact]
    public void The_formatter_renames_only_the_events()
    {
        var formatter = new InventoryEventEntityNameFormatter(new ExistingNames());
        var format = typeof(InventoryEventEntityNameFormatter).GetMethod(nameof(IEntityNameFormatter.FormatEntityName))!;

        foreach (var (type, exchange) in InventoryEventNames.All)
            format.MakeGenericMethod(type).Invoke(formatter, null).Should().Be(exchange);
    }

    [Fact]
    public void Commands_and_framework_types_keep_their_existing_entity_names()
    {
        var fallback = new ExistingNames();
        var formatter = new InventoryEventEntityNameFormatter(fallback);

        formatter.FormatEntityName<ReserveStock>().Should().Be(fallback.FormatEntityName<ReserveStock>());
        formatter.FormatEntityName<ReleaseStock>().Should().Be(fallback.FormatEntityName<ReleaseStock>());
        formatter.FormatEntityName<ConsumeStock>().Should().Be(fallback.FormatEntityName<ConsumeStock>());
        formatter.FormatEntityName<Fault<ReserveStock>>().Should().Be(fallback.FormatEntityName<Fault<ReserveStock>>());
    }
}
