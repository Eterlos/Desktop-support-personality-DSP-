using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Dsp.Chatbot;

/// <summary>HTTP text client for a user-managed local Ollama server.</summary>
public sealed class LocalChatBackend : IChatBackend, IDisposable
{
    private readonly HttpClient client;
    private readonly Uri baseUri;
    private readonly string? apiKey;

    public LocalChatBackend(string baseUrl, string? apiKey = null, HttpMessageHandler? handler = null)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !uri.IsLoopback || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException("Укажите локальный адрес Ollama, например http://localhost:11434/v1/.", nameof(baseUrl));
        }

        if (apiKey is not null && apiKey.Any(char.IsControl))
        {
            throw new ArgumentException(
                "Ключ API содержит перенос строки или другой управляющий символ. Проверьте DSP_CHAT_API_KEY.",
                nameof(apiKey));
        }

        baseUri = new Uri(uri.AbsoluteUri.TrimEnd('/') + "/");
        this.apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey;
        client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromMinutes(2)
        };
    }

    public async Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, "models");
        using var response = await SendAsync(request, cancellationToken);
        using var json = await ReadJsonAsync(response, cancellationToken);
        if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new ChatBackendException("Сервер вернул неожиданный формат списка моделей.");

        return data.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object &&
                item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            .Select(item => item.GetProperty("id").GetString()!)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToArray();
    }

    public async Task<string> ReplyAsync(string model, IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count == 0 || messages.Any(message => message is null ||
            message.Role is not ("system" or "user" or "assistant") || string.IsNullOrWhiteSpace(message.Content)))
            throw new ArgumentException("Нужен непустой список текстовых сообщений с ролями system, user или assistant.", nameof(messages));

        using var request = CreateRequest(HttpMethod.Post, "chat/completions");
        request.Content = JsonContent.Create(new
        {
            model,
            messages = messages.Select(message => new { role = message.Role, content = message.Content }),
            stream = false
        });
        using var response = await SendAsync(request, cancellationToken);
        using var json = await ReadJsonAsync(response, cancellationToken);
        if (json.RootElement.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0 &&
            choices[0].ValueKind == JsonValueKind.Object &&
            choices[0].TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object &&
            message.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(content.GetString()))
            return content.GetString()!;

        throw new ChatBackendException("Сервер не вернул текстовый ответ. Проверьте модель и поддержку Chat Completions.");
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(baseUri, path));
        if (apiKey is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode) return response;
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new ChatBackendException($"Сервер вернул HTTP {status}. Проверьте адрес API, название модели и настройки доступа.");
        }
        catch (HttpRequestException exception)
        {
            throw new ChatBackendException("Нет соединения с локальным сервером модели. Убедитесь, что он запущен.", exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ChatBackendException("Сервер не ответил за 2 минуты.", exception);
        }
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (json.RootElement.ValueKind == JsonValueKind.Object) return json;
            json.Dispose();
            throw new ChatBackendException("Сервер вернул JSON без объекта ответа.");
        }
        catch (JsonException exception)
        {
            throw new ChatBackendException("Ответ сервера не является корректным JSON.", exception);
        }
    }

    public void Dispose() => client.Dispose();
}
