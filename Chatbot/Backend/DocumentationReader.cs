using System.Reflection;
using System.Text.Json;

namespace Dsp.Chatbot;

/// <summary>In-memory reference manuals embedded with the application. No code or filesystem access.</summary>
public static class DocumentationReader
{
    public const string CommandName = "dsp.docs.read";
    private const string Prefix = "DspDocs/";
    private static readonly IReadOnlyDictionary<string, string[]> Documents = LoadDocuments();
    public static IReadOnlyList<string> Paths { get; } = Documents.Keys.Order(StringComparer.Ordinal).ToArray();

    public static string Index => string.Join("\n", Documents.SelectMany(document =>
        new[] { document.Key }.Concat(document.Value.Select((text, index) => (text, line: index + 1))
            .Where(item => item.text.StartsWith("## ", StringComparison.Ordinal))
            .Select(item => $"  строка {item.line}: {item.text}"))));

    public static string Overview
    {
        get
        {
            var lines = Documents["Chatbot/TECHNICAL.md"];
            return string.Join("\n", lines.SkipWhile(line => !line.StartsWith("## 1.1.", StringComparison.Ordinal))
                .Skip(1).TakeWhile(line => !line.StartsWith("## ", StringComparison.Ordinal)));
        }
    }

    private static IReadOnlyDictionary<string, string[]> LoadDocuments()
    {
        var assembly = typeof(DocumentationReader).Assembly;
        return assembly.GetManifestResourceNames().Where(name => name.StartsWith(Prefix, StringComparison.Ordinal))
            .ToDictionary(name => name[Prefix.Length..], name =>
            {
                using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
                return reader.ReadToEnd().Replace("\r\n", "\n").Split('\n');
            }, StringComparer.Ordinal);
    }

    public static object Read(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object || arguments.EnumerateObject()
            .Any(p => p.Name is not ("path" or "query" or "startLine" or "lineCount")))
            throw new ChatBackendException("Разрешены только аргументы path, query, startLine и lineCount.");
        if (!arguments.TryGetProperty("path", out var pathValue) || pathValue.ValueKind != JsonValueKind.String ||
            !Documents.TryGetValue(pathValue.GetString()!, out var lines))
            throw new ChatBackendException("Доступен только точный путь из списка документации DSP.");
        var start = Number(arguments, "startLine", 1);
        var count = Number(arguments, "lineCount", 120);
        if (start < 1 || count is < 1 or > 200)
            throw new ChatBackendException("startLine должен быть положительным, lineCount — от 1 до 200.");
        if (arguments.TryGetProperty("query", out var queryValue))
        {
            if (queryValue.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(queryValue.GetString()))
                throw new ChatBackendException("query должен содержать слово или фразу для поиска в справке.");
            var match = Array.FindIndex(lines, line => line.Contains(queryValue.GetString()!, StringComparison.OrdinalIgnoreCase));
            if (match < 0) return new { path = pathValue.GetString(), found = false, query = queryValue.GetString(),
                text = "Совпадение не найдено. Это не означает, что функции нет: уточните поисковое слово по оглавлению." };
            start = Math.Max(1, match - 2);
            count = Math.Min(count, 35);
        }
        return new { path = pathValue.GetString(), snapshot = "documentation-at-build-time", startLine = start,
            totalLines = lines.Length, text = string.Join("\n", lines.Skip(start - 1).Take(count)) };
    }

    private static int Number(JsonElement args, string name, int fallback)
    {
        if (!args.TryGetProperty(name, out var value)) return fallback;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)) return result;
        throw new ChatBackendException($"Аргумент {name} должен быть целым числом.");
    }
}
