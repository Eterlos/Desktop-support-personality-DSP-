using System.Net.Http.Json;
using System.Text.Json;

namespace Dsp.Chatbot;

public static class OllamaModelCatalog
{
    public static bool IsLocalModel(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object ||
            !item.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(name.GetString()) ||
            name.GetString()!.EndsWith(":cloud", StringComparison.OrdinalIgnoreCase) ||
            name.GetString()!.EndsWith("-cloud", StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var field in new[] { "remote_host", "remote_model" })
            if (item.TryGetProperty(field, out var remote) && remote.ValueKind != JsonValueKind.Null &&
                (remote.ValueKind != JsonValueKind.String || !string.IsNullOrWhiteSpace(remote.GetString()))) return false;
        return item.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Object &&
            details.TryGetProperty("format", out var format) && format.ValueKind == JsonValueKind.String && format.GetString() == "gguf";
    }

    public static async Task<IReadOnlyList<string>> GetModelsAsync(string baseUrl, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || !uri.IsLoopback ||
            uri.Scheme is not ("http" or "https") || uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Нужен локальный адрес Ollama.", nameof(baseUrl));
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(2) };
        try
        {
            using var response = await client.GetAsync(new Uri(uri, "/api/tags"), cancellationToken);
            if (!response.IsSuccessStatusCode) throw new ChatBackendException($"Ollama вернула HTTP {(int)response.StatusCode} при получении локальных моделей.");
            using var json = await response.Content.ReadFromJsonAsync<JsonDocument>(cancellationToken);
            if (json is null || json.RootElement.ValueKind != JsonValueKind.Object ||
                !json.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
                throw new ChatBackendException("Ollama вернула некорректный список локальных моделей.");
            return models.EnumerateArray().Where(IsLocalModel).Select(item => item.GetProperty("name").GetString()!)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (HttpRequestException e) { throw new ChatBackendException("Нет соединения с локальной Ollama.", e); }
        catch (JsonException e) { throw new ChatBackendException("Ollama вернула некорректный JSON списка моделей.", e); }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        { throw new ChatBackendException("Истекло время получения моделей Ollama.", e); }
    }
}
