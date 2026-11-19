using System.Text;

namespace Dsp.Chatbot;

public static class UserInstructions
{
    public const string LinkUrl = "dsp://settings/behavior";
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DesktopSupportPersonality", "behavior.local.txt");

    public static string Read(string? path = null)
    {
        path ??= FilePath;
        try { return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : ReadDefault(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw new ChatBackendException("Не удалось прочитать файл поведения: " + path, e); }
    }

    public static void EnsureExists(string? path = null)
    {
        path ??= FilePath;
        if (File.Exists(path)) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            using var writer = new StreamWriter(stream, Encoding.UTF8);
            writer.Write(ReadDefault());
        }
        catch (IOException) when (File.Exists(path)) { /* Another instance created the file. */ }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { throw new ChatBackendException("Не удалось создать файл поведения: " + path, e); }
    }

    private static string ReadDefault()
    {
        using var reader = new StreamReader(typeof(UserInstructions).Assembly.GetManifestResourceStream("DspDefaults/behavior.txt")!);
        return reader.ReadToEnd();
    }
}
