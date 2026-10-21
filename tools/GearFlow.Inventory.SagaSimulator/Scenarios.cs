using GearFlow.Contracts;

namespace GearFlow.Inventory.SagaSimulator;

/// <summary>Cenários prontos da Saga, do ponto de vista do Inventory. Cada passo imprime OK ou FALHA.</summary>
internal static class Scenarios
{
    // Catálogo de demonstração (IDs fixos do seed do Inventory; ver README do serviço).
    private static readonly Guid FiltroDeOleo = new("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid JogoDePalhetas = new("a1000000-0000-0000-0000-000000000010");   // 4 un., abaixo do mínimo
    private static readonly Guid Oleo5W30 = new("b2000000-0000-0000-0000-000000000001");
    private static readonly Guid AguaDesmineralizada = new("b2000000-0000-0000-0000-000000000007"); // 4 L, abaixo do mínimo

    private static readonly string[] All = ["happy", "shortage", "compensation", "lowstock", "outoforder", "poison"];

    public static async Task<int> RunAsync(Simulator simulator, Options options)
    {
        var name = options.Positional ?? throw new ArgumentException("Informe o cenário: happy | shortage | compensation | lowstock | outoforder | poison | all");
        var timeout = options.Wait;

        if (name == "all")
        {
            var failed = new List<string>();
            foreach (var scenario in All)
            {
                if (!await RunOneAsync(simulator, scenario, timeout))
                    failed.Add(scenario);
                Console.WriteLine();
            }

            Console.WriteLine(failed.Count == 0
                ? $"RESULTADO: os {All.Length} cenários passaram."
                : $"RESULTADO: falharam {failed.Count} de {All.Length}: {string.Join(", ", failed)}");
            return failed.Count == 0 ? 0 : 1;
        }

        return await RunOneAsync(simulator, name, timeout) ? 0 : 1;
    }

    private static async Task<bool> RunOneAsync(Simulator simulator, string name, TimeSpan timeout)
    {
        var order = Guid.NewGuid();
        Console.WriteLine($"=== Cenário '{name}'  (OS {order})");

        return name switch
        {
            "happy" => await Happy(simulator, order, timeout),
            "shortage" => await Shortage(simulator, order, timeout),
            "compensation" => await Compensation(simulator, order, timeout),
            "lowstock" => await LowStock(simulator, order, timeout),
            "outoforder" => await OutOfOrder(simulator, order, timeout),
            "poison" => await Poison(simulator, order, timeout),
            _ => throw new ArgumentException($"Cenário desconhecido: '{name}'."),
        };
    }

    private static async Task<bool> Happy(Simulator sim, Guid order, TimeSpan timeout)
    {
        Console.WriteLine("  reserva Filtro de óleo x2 + Óleo 5W30 x4,5 L, depois consome");
        await sim.ReserveAsync(order, [Part(FiltroDeOleo, 2), Consumable(Oleo5W30, 4.5m)]);
        var reserved = await sim.WaitForAsync<StockReserved>(order, timeout);
        var ok = Step(reserved is { Lines.Count: 2 }, "StockReserved com as 2 linhas");

        await sim.ConsumeAsync(order);
        var consumed = await sim.WaitForAsync<StockConsumed>(order, timeout);
        return Step(consumed is not null, "StockConsumed") && ok;
    }

    private static async Task<bool> Shortage(Simulator sim, Guid order, TimeSpan timeout)
    {
        Console.WriteLine("  pede Jogo de palhetas x5 (só há 4) + Filtro de óleo x1 (há de sobra)");
        await sim.ReserveAsync(order, [Part(JogoDePalhetas, 5), Part(FiltroDeOleo, 1)]);
        var insufficient = await sim.WaitForAsync<StockInsufficient>(order, timeout);

        var ok = Step(insufficient is not null, "StockInsufficient");
        var onlyPalhetas = insufficient?.Missing is [{ ItemId: var id }] && id == JogoDePalhetas;
        ok &= Step(onlyPalhetas, "lista só as palhetas como faltantes (o Filtro tem saldo)");
        ok &= Step(sim.RepliesFor(order).All(r => r.Message is not StockReserved),
            "nada foi reservado (tudo ou nada): nenhum StockReserved");
        Console.WriteLine("  confira o e-mail 'Estoque insuficiente' no Mailpit (http://localhost:8025)");
        return ok;
    }

    private static async Task<bool> Compensation(Simulator sim, Guid order, TimeSpan timeout)
    {
        Console.WriteLine("  reserva Filtro de óleo x1 e depois compensa com ReleaseStock");
        await sim.ReserveAsync(order, [Part(FiltroDeOleo, 1)]);
        var ok = Step(await sim.WaitForAsync<StockReserved>(order, timeout) is not null, "StockReserved");

        await sim.ReleaseAsync(order, "pagamento recusado");
        return Step(await sim.WaitForAsync<StockReleased>(order, timeout) is not null, "StockReleased") && ok;
    }

    private static async Task<bool> LowStock(Simulator sim, Guid order, TimeSpan timeout)
    {
        Console.WriteLine("  reserva e consome Água desmineralizada x0,5 L (já está abaixo do mínimo de 5)");
        await sim.ReserveAsync(order, [Consumable(AguaDesmineralizada, 0.5m)]);
        var ok = Step(await sim.WaitForAsync<StockReserved>(order, timeout) is not null, "StockReserved");

        await sim.ConsumeAsync(order);
        ok &= Step(await sim.WaitForAsync<StockConsumed>(order, timeout) is not null, "StockConsumed");

        // LowStockAlert não carrega o CorrelationId da OS: procura pelo item, depois do consumo.
        var alert = await WaitForAlertAsync(sim, AguaDesmineralizada, timeout);
        ok &= Step(alert is not null, "LowStockAlert da água desmineralizada");
        Console.WriteLine("  confira o e-mail 'Estoque baixo: Água desmineralizada (L)' no Mailpit (http://localhost:8025)");
        return ok;
    }

    private static async Task<bool> OutOfOrder(Simulator sim, Guid order, TimeSpan timeout)
    {
        Console.WriteLine("  manda ReleaseStock ANTES do ReserveStock (mensagens fora de ordem)");
        await sim.ReleaseAsync(order, "chegou primeiro");
        var ok = Step(await sim.WaitForAsync<StockReleased>(order, timeout) is not null, "StockReleased");

        await sim.ReserveAsync(order, [Part(FiltroDeOleo, 1)]);
        var late = await sim.WaitForAsync<StockReserved>(order, TimeSpan.FromSeconds(3));
        return Step(late is null, "o ReserveStock atrasado foi ignorado (nenhum StockReserved)") && ok;
    }

    private static async Task<bool> Poison(Simulator sim, Guid order, TimeSpan timeout)
    {
        Console.WriteLine("  manda um ReserveStock SEM itens (mensagem inválida)");
        await sim.ReserveAsync(order, []);
        var reply = await sim.WaitForAsync<StockReserved>(order, TimeSpan.FromSeconds(3));
        var ok = Step(reply is null && sim.RepliesFor(order).Count == 0, "nenhuma resposta (falha permanente não responde)");
        Console.WriteLine("  confira a mensagem na fila 'inventory-reserve-stock_error' (http://localhost:15672, aba Queues)");
        return ok;
    }

    private static async Task<LowStockAlert?> WaitForAlertAsync(Simulator sim, Guid itemId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var alert = sim.AllReplies().Select(r => r.Message).OfType<LowStockAlert>().LastOrDefault(a => a.ItemId == itemId);
            if (alert is not null)
                return alert;
            await Task.Delay(100);
        }

        return null;
    }

    private static StockItemLine Part(Guid id, decimal quantity) => new(InventoryItemType.Part, id, quantity);
    private static StockItemLine Consumable(Guid id, decimal quantity) => new(InventoryItemType.Consumable, id, quantity);

    private static bool Step(bool passed, string description)
    {
        Console.WriteLine($"  [{(passed ? "  OK  " : "FALHA ")}] {description}");
        return passed;
    }
}
