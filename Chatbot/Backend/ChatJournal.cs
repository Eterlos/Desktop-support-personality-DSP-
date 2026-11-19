using System.Text;
using System.Text.Json;

namespace Dsp.Chatbot;

public sealed class ChatJournal
{
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All)
    };
    private readonly string conversationId;
    private bool initialized;
    public string FilePath { get; }
    public string? LastError { get; private set; }

    public ChatJournal(string conversationId, string? directory = null)
    {
        this.conversationId = conversationId;
        FilePath = Path.Combine(directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DesktopSupportPersonality"), "dsp-work.txt");
        lock (Sync) Reset();
    }

    private void Reset()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, $"DSP — журнал текущего запуска\nНачало: {DateTimeOffset.UtcNow:O}\n" +
                $"Диалог: {conversationId}\n\n", Encoding.UTF8);
            initialized = true;
            LastError = null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { LastError = "Не удалось очистить локальный журнал: " + e.Message; }
    }

    public void Write(string kind, object? data = null)
    {
        lock (Sync)
        {
            if (!initialized) Reset();
            if (!initialized) return;
            try
            {
                var entry = $"[{DateTimeOffset.UtcNow:O}] {kind}\n" + JsonSerializer.Serialize(data, Options) + "\n\n";
                File.AppendAllText(FilePath, entry, Encoding.UTF8);
                LastError = null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { LastError = "Не удалось записать локальный журнал: " + e.Message; }
        }
    }
}
