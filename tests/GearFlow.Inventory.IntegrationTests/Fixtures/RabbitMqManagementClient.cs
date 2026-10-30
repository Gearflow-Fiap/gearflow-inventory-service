using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GearFlow.Inventory.IntegrationTests.Fixtures;

/// <summary>API HTTP de gerenciamento do RabbitMQ (imagem <c>-management</c>): usada para olhar as filas de erro (DLQ).</summary>
internal sealed class RabbitMqManagementClient : IDisposable
{
    private readonly HttpClient _http;

    public RabbitMqManagementClient(string host, int managementPort, string username, string password)
    {
        _http = new HttpClient { BaseAddress = new Uri($"http://{host}:{managementPort}/") };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}")));
    }

    /// <summary>Quantidade de mensagens na fila, ou 0 se a fila ainda não existe.</summary>
    public async Task<int> MessageCountAsync(string queue)
    {
        using var response = await _http.GetAsync($"api/queues/%2F/{queue}");
        if (!response.IsSuccessStatusCode)
            return 0;

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.TryGetProperty("messages", out var messages) ? messages.GetInt32() : 0;
    }

    public async Task<bool> ExchangeExistsAsync(string exchange)
    {
        using var response = await _http.GetAsync($"api/exchanges/%2F/{Uri.EscapeDataString(exchange)}");
        return response.IsSuccessStatusCode;
    }

    /// <summary>Lê (sem consumir) as mensagens da fila e devolve os headers de cada uma.</summary>
    public async Task<IReadOnlyList<Dictionary<string, JsonElement>>> PeekHeadersAsync(string queue, int count = 10)
    {
        var body = new StringContent(
            JsonSerializer.Serialize(new { count, ackmode = "ack_requeue_true", encoding = "auto" }),
            Encoding.UTF8, "application/json");

        using var response = await _http.PostAsync($"api/queues/%2F/{queue}/get", body);
        if (!response.IsSuccessStatusCode)
            return [];

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var result = new List<Dictionary<string, JsonElement>>();
        foreach (var message in json.RootElement.EnumerateArray())
        {
            var headers = new Dictionary<string, JsonElement>();
            if (message.TryGetProperty("properties", out var props) && props.TryGetProperty("headers", out var h))
                foreach (var header in h.EnumerateObject())
                    headers[header.Name] = header.Value.Clone();
            result.Add(headers);
        }

        return result;
    }

    public void Dispose() => _http.Dispose();
}
