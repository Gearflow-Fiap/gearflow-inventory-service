using System.Text;
using GearFlow.Contracts;
using GearFlow.Inventory.SagaSimulator;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length == 0 || args[0] is "help" or "--help" or "-h")
{
    Console.WriteLine(Cli.Usage);
    return 0;
}

try
{
    var command = args[0];
    var options = Cli.Parse(args.Skip(1).ToArray());

    await using var simulator = await Simulator.StartAsync(options.Connection);

    return command switch
    {
        "listen" => await Commands.ListenAsync(simulator, options),
        "reserve" => await Commands.ReserveAsync(simulator, options),
        "release" => await Commands.ReleaseAsync(simulator, options),
        "consume" => await Commands.ConsumeAsync(simulator, options),
        "scenario" => await Scenarios.RunAsync(simulator, options),
        _ => Cli.Fail($"Comando desconhecido: '{command}'. Use 'help'."),
    };
}
catch (ArgumentException ex)
{
    return Cli.Fail(ex.Message);
}
catch (InvalidOperationException ex)
{
    return Cli.Fail(ex.Message);
}

/// <summary>Comandos avulsos: cada um publica e espera a resposta daquela OS.</summary>
internal static class Commands
{
    public static async Task<int> ListenAsync(Simulator simulator, Options options)
    {
        simulator.OnReply += reply => Console.WriteLine(Simulator.Describe(reply));

        Console.WriteLine($"Ouvindo os eventos do Inventory em {options.Connection.Host}:{options.Connection.Port}. Ctrl+C para sair.");
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
        try { await Task.Delay(Timeout.Infinite, stop.Token); } catch (OperationCanceledException) { }
        return 0;
    }

    public static async Task<int> ReserveAsync(Simulator simulator, Options options)
    {
        var order = options.Order ?? Guid.NewGuid();
        Console.WriteLine($"-> ReserveStock  OS {order}  {options.Items.Count} item(ns)");
        await simulator.ReserveAsync(order, options.Items);
        return await Cli.PrintRepliesAsync(simulator, order, options.Wait, expectReply: options.Items.Count > 0);
    }

    public static async Task<int> ReleaseAsync(Simulator simulator, Options options)
    {
        var order = options.Order ?? throw new ArgumentException("Informe a OS: --order <guid>");
        Console.WriteLine($"-> ReleaseStock  OS {order}  motivo: {options.Reason}");
        await simulator.ReleaseAsync(order, options.Reason);
        return await Cli.PrintRepliesAsync(simulator, order, options.Wait, expectReply: true);
    }

    public static async Task<int> ConsumeAsync(Simulator simulator, Options options)
    {
        var order = options.Order ?? throw new ArgumentException("Informe a OS: --order <guid>");
        Console.WriteLine($"-> ConsumeStock  OS {order}");
        await simulator.ConsumeAsync(order);
        return await Cli.PrintRepliesAsync(simulator, order, options.Wait, expectReply: true);
    }
}
