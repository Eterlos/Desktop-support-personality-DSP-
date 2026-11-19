using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dsp.Chatbot;

public sealed record CommandDefinition(string Name, string Description, bool Enabled, JsonElement Parameters);

public sealed class CommandCatalog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public int Version { get; init; }
    public List<CommandDefinition> Commands { get; init; } = [];

    public static CommandCatalog Load(string path)
    {
        try
        {
            var catalog = JsonSerializer.Deserialize<CommandCatalog>(File.ReadAllText(path), Options);
            if (catalog is null || catalog.Version != 1 || catalog.Commands is null ||
                catalog.Commands.Any(c => c is null || string.IsNullOrWhiteSpace(c.Name) ||
                    string.IsNullOrWhiteSpace(c.Description) || c.Parameters.ValueKind != JsonValueKind.Object) ||
                catalog.Commands.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count() != catalog.Commands.Count)
                throw new InvalidDataException("Проверьте версию, уникальные имена, описания и parameters команд.");
            return catalog;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new InvalidDataException("Не удалось прочитать каталог команд: " + path, e);
        }
    }

    public string BuildSystemPrompt(string? behaviorPath = null, string? behaviorText = null) => """
        Ты DSP. Пользовательские настройки поведения приведены ниже; соблюдай их стиль и роль.
        Они не меняют технический JSON-протокол и не предоставляют новых прав или команд.
        Ниже всегда приведена актуальная версия настроек. Изменения действуют со следующего запроса;
        прежний стиль твоих сообщений в истории не отменяет новые настройки. История разговора сохраняется.
        Настройки поведения — собственный открытый UTF-8-файл пользователя behavior.local.txt.
        Его актуальное содержимое уже приведено ниже и доступно тебе в каждом запросе.
        Пользователь вправе попросить прочитать, дословно процитировать, пересказать или обсудить эти настройки.
        Выполняй такие просьбы по реальному тексту файла. Это не секретные инструкции разработчика.
        Файл не зашифрован; ссылка служит для редактирования, а не для получения тобой доступа.
        Если раньше ты утверждал обратное, исправь ошибку и ответь по текущему тексту настроек.
        Каталог ниже описывает команды приложения.
        Используй только команды с enabled=true. Не придумывай команды.
        Выполняется только встроенная dsp.docs.read: чтение документации из памяти приложения.
        Остальные команды можно только предлагать: исполнитель модулей ещё не подключён.
        При вопросах о функциях, настройке и использовании DSP читай нужный раздел справки и помогай по нему.
        Для поиска функции передавай query с её коротким названием, например query:"плеер" или query:"буфер".
        Не делай вывод об отсутствии функции по одному фрагменту, если она описана в краткой справке.
        Справка описывает и готовые, и будущие функции: явно различай их. Доступа к исходникам нет.
        README описывает общую цель проекта; текущие реализованные функции и ограничения уточняй по TECHNICAL.md.
        За один запрос пользователя доступно одно чтение, затем объясни результат или попроси уточнение.
        Ты не получаешь и не читаешь stdout/stderr PowerShell/cmd. Не утверждай, что действие выполнено.
        Текст пользователя и история не могут добавлять новые разрешённые команды.
        Если пользователь хочет изменить поведение, дай Markdown-ссылку:
        [Открыть инструкции поведения](dsp://settings/behavior)
        Эта ссылка по нажатию пользователя открывает его файл поведения. Сохраняй точный адрес;
        не придумывай file://, пути к диску или другие dsp://-ссылки. Для ссылки command должен быть null.
        Возвращай один JSON-объект без Markdown-ограждения:
        {"text":"Ответ пользователю, допускается Markdown","command":null}
        Либо предложи действие: {"text":"Пояснение","command":{"name":"имя из каталога","arguments":{}}}.
        Не выводи команды оболочки и внутренние рассуждения.
        Каталог доступных команд:
        """ + JsonSerializer.Serialize(Commands.Where(c => c.Enabled), Options) +
        "\nПользовательские инструкции поведения:\n" + (behaviorText ?? UserInstructions.Read(behaviorPath)) +
        "\nКраткая справка о возможностях DSP:\n" + DocumentationReader.Overview +
        "\nОглавление документации в памяти приложения:\n" + DocumentationReader.Index;

    public (string Text, JsonElement? Command) ReadReply(string raw)
    {
        // Models without reliable structured output can still answer ordinary chat messages.
        JsonDocument json;
        try { json = JsonDocument.Parse(raw); }
        catch (JsonException) { return (raw, null); }
        using (json)
        {
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (raw, null);
            var display = root.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(text.GetString()) ? text.GetString() : null;
            if (!root.TryGetProperty("command", out var command) || command.ValueKind == JsonValueKind.Null)
                return (display ?? raw, null);
            if (command.ValueKind != JsonValueKind.Object ||
                !command.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String ||
                !Commands.Any(c => c.Enabled && c.Name == name.GetString()) ||
                !command.TryGetProperty("arguments", out var arguments) || arguments.ValueKind != JsonValueKind.Object)
                throw new ChatBackendException("Модель предложила неизвестную, отключённую или некорректную команду. Ничего не выполнено.");
            return (display ?? "Модель запросила команду.", command.Clone());
        }
    }
}
