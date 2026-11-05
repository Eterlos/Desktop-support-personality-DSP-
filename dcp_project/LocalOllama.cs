using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;

namespace dcp_project;

internal static class LocalOllama
{
    internal const string Model = "qwen3:4b-instruct";
    internal const string BaseUrl = "http://localhost:11434/v1/";

    internal static async Task PrepareAsync(CancellationToken cancellationToken)
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

        // Empty prompt loads the installed model without adding a conversation turn.
        using var response = await client.PostAsJsonAsync("api/generate",
            new { model = Model, prompt = "", stream = false, keep_alive = "5m" }, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Не удалось загрузить {Model} (HTTP {(int)response.StatusCode}). Проверьте ollama list.");
    }

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
