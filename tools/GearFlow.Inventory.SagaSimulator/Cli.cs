using System.Globalization;
using GearFlow.Contracts;

namespace GearFlow.Inventory.SagaSimulator;

internal sealed record Options(
    ConnectionOptions Connection,
    Guid? Order,
    IReadOnlyList<StockItemLine> Items,
    string Reason,
    TimeSpan Wait,
    string? Positional);

internal static class Cli
{
    public const string Usage = """
        Simulador do orquestrador da Saga: testa o Inventory sem o os-service.

        USO
          dotnet run --project tools/GearFlow.Inventory.SagaSimulator -- <comando> [opções]

        COMANDOS
          listen                       Imprime todos os eventos do Inventory (StockReserved, StockInsufficient, ...). Ctrl+C sai.
          reserve  [--order <guid>] --part <id>:<qtd> [--part ...] [--consumable <id>:<qtd> ...]
                                       Envia ReserveStock e mostra a resposta. Sem --order gera uma OS nova.
                                       Sem nenhum item = mensagem inválida (vai para a fila inventory-reserve-stock_error).
          release  --order <guid> [--reason <texto>]
                                       Envia ReleaseStock (compensação).
          consume  --order <guid>      Envia ConsumeStock (baixa definitiva).
          scenario <nome>              Roda um cenário pronto e confere o resultado:
                                         happy         reserva -> consumo
                                         shortage      falta de estoque (tudo ou nada) -> StockInsufficient
                                         compensation  reserva -> liberação
                                         lowstock      consumo que deixa o item abaixo do mínimo -> LowStockAlert
                                         outoforder    ReleaseStock chega antes do ReserveStock
                                         poison        mensagem inválida -> DLQ, sem resposta
                                         all           todos, em sequência

        OPÇÕES DE CONEXÃO (padrão: broker local do docker-compose)
          --host localhost  --port 5672  --user guest  --pass guest
          --wait 5          Segundos esperando a resposta (padrão 5)

        IDS DO CATÁLOGO DE DEMONSTRAÇÃO (README do serviço)
          peças    a1000000-0000-0000-0000-0000000000NN  (NN = 01..10; 10 = Jogo de palhetas, só 4 un.)
          insumos  b2000000-0000-0000-0000-0000000000NN  (NN = 01..07; 07 = Água desmineralizada, só 4 L)
        """;

    public static Options Parse(string[] args)
    {
        string host = "localhost", user = "guest", pass = "guest", reason = "simulador";
        ushort port = 5672;
        Guid? order = null;
        var wait = TimeSpan.FromSeconds(5);
        var items = new List<StockItemLine>();
        string? positional = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                positional ??= arg;
                continue;
            }

            string Value() => i + 1 < args.Length
                ? args[++i]
                : throw new ArgumentException($"A opção {arg} precisa de um valor.");

            switch (arg)
            {
                case "--host": host = Value(); break;
                case "--port": port = ushort.Parse(Value(), CultureInfo.InvariantCulture); break;
                case "--user": user = Value(); break;
                case "--pass": pass = Value(); break;
                case "--reason": reason = Value(); break;
                case "--wait": wait = TimeSpan.FromSeconds(double.Parse(Value(), CultureInfo.InvariantCulture)); break;
                case "--order": order = ParseGuid(Value(), "--order"); break;
                case "--part": items.Add(ParseItem(InventoryItemType.Part, Value())); break;
                case "--consumable": items.Add(ParseItem(InventoryItemType.Consumable, Value())); break;
                default: throw new ArgumentException($"Opção desconhecida: {arg}");
            }
        }

        return new Options(new ConnectionOptions(host, port, user, pass), order, items, reason, wait, positional);
    }

    private static Guid ParseGuid(string text, string option) =>
        Guid.TryParse(text, out var guid) ? guid : throw new ArgumentException($"{option}: '{text}' não é um GUID.");

    private static StockItemLine ParseItem(InventoryItemType type, string text)
    {
        var parts = text.Split(':');
        if (parts.Length != 2
            || !Guid.TryParse(parts[0], out var id)
            || !decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var quantity))
            throw new ArgumentException($"Item inválido '{text}'. Use <guid>:<quantidade>, ex.: a1000000-0000-0000-0000-000000000001:2");

        return new StockItemLine(type, id, quantity);
    }

    public static int Fail(string message)
    {
        Console.Error.WriteLine($"ERRO: {message}");
        return 1;
    }

    /// <summary>Mostra as respostas da OS que chegarem em <paramref name="wait"/> (mais 1 s de folga depois da primeira).</summary>
    public static async Task<int> PrintRepliesAsync(Simulator simulator, Guid order, TimeSpan wait, bool expectReply)
    {
        var printed = 0;
        var deadline = DateTime.UtcNow + wait;

        while (DateTime.UtcNow < deadline)
        {
            var replies = simulator.RepliesFor(order);
            for (; printed < replies.Count; printed++)
            {
                Console.WriteLine(Simulator.Describe(replies[printed]));
                deadline = DateTime.UtcNow + TimeSpan.FromSeconds(1); // folga para respostas seguintes
            }

            await Task.Delay(100);
        }

        if (printed > 0)
            return 0;

        if (!expectReply)
        {
            Console.WriteLine($"(sem resposta em {wait.TotalSeconds:0.#} s, como esperado para uma mensagem inválida: confira a fila inventory-reserve-stock_error no RabbitMQ)");
            return 0;
        }

        Console.WriteLine($"SEM RESPOSTA em {wait.TotalSeconds:0.#} s. Causas possíveis:");
        Console.WriteLine("  1) a API do Inventory não está rodando (a mensagem espera na fila e é processada quando ela voltar);");
        Console.WriteLine("  2) a mensagem foi rejeitada como falha permanente (ex.: ReleaseStock de uma reserva já consumida) e está na fila *_error:");
        Console.WriteLine("     http://localhost:15672 (guest/guest), aba Queues.");
        return 1;
    }
}
