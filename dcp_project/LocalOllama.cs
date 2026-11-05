using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace dcp_project;

internal static class LocalOllama
{
    internal const string BaseUrl = "http://localhost:11434/v1/";

    internal static async Task EnsureRunningAsync(CancellationToken cancellationToken)
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri("http://localhost:11434/"),
            Timeout = TimeSpan.FromMinutes(2)
        };
        if (!await IsRunningAsync(client, cancellationToken))
        {
            var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "Ollama", "ollama.exe");
            if (!File.Exists(executable))
                throw new InvalidOperationException("Ollama не найдена на этом компьютере.");
            using var process = Process.Start(new ProcessStartInfo(executable, "serve")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            var running = false;
            for (var attempt = 0; attempt < 30; attempt++)
            {
                await Task.Delay(500, cancellationToken);
                if (await IsRunningAsync(client, cancellationToken)) { running = true; break; }
            }
            if (!running) throw new InvalidOperationException("Ollama не запустилась. Запустите её вручную и повторите подключение.");
        }

    }

    internal static bool IsLocalModel(JsonElement item)
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
            details.TryGetProperty("format", out var format) && format.GetString() == "gguf";
    }

    internal static async Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync("api/tags", cancellationToken);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!json.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Ollama вернула некорректный список моделей.");
        return models.EnumerateArray().Where(IsLocalModel).Select(item => item.GetProperty("name").GetString()!)
            .Distinct().OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static async Task PrepareModelAsync(string model, CancellationToken cancellationToken)
    {
        if (!(await GetModelsAsync(cancellationToken)).Contains(model))
            throw new InvalidOperationException("Выберите установленную локальную модель Ollama.");
        using var client = CreateClient();
        using var response = await client.PostAsJsonAsync("api/generate",
            new { model, prompt = "", stream = false, keep_alive = "5m" }, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Не удалось загрузить {model} (HTTP {(int)response.StatusCode}).");
    }

    private static HttpClient CreateClient() => new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        BaseAddress = new Uri("http://localhost:11434/"), Timeout = TimeSpan.FromMinutes(2)
    };

    private static async Task<bool> IsRunningAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var probe = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probe.CancelAfter(TimeSpan.FromSeconds(1));
        try
        {
            using var response = await client.GetAsync("api/version", probe.Token);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException) { return false; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return false; }
    }
}
