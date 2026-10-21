namespace GearFlow.Inventory.IntegrationTests.Fixtures;

internal static class Eventually
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Repete a condição até ela ser verdadeira ou estourar o tempo. Lança <see cref="TimeoutException"/>.</summary>
    public static async Task UntilAsync(Func<Task<bool>> condition, string because, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return;
            await Task.Delay(100);
        }

        throw new TimeoutException($"Condição não atendida a tempo: {because}");
    }

    /// <summary>Garante que a condição continua FALSA durante <paramref name="window"/> (ex.: "nenhuma resposta chegou").</summary>
    public static async Task NeverAsync(Func<bool> condition, string because, TimeSpan window)
    {
        var deadline = DateTime.UtcNow + window;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                throw new InvalidOperationException($"Aconteceu algo que não devia: {because}");
            await Task.Delay(100);
        }
    }
}
