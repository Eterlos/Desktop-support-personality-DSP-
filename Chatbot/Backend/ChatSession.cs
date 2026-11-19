namespace Dsp.Chatbot;

// Adapter boundary for the team's future conversation database.
public interface IChatHistoryStore
{
    Task<IReadOnlyList<ChatMessage>> ReadAsync(string conversationId, CancellationToken cancellationToken = default);
    Task AppendTurnAsync(string conversationId, ChatMessage user, ChatMessage assistant, CancellationToken cancellationToken = default);
    Task ClearAsync(string conversationId, CancellationToken cancellationToken = default);
}

public sealed class MemoryChatHistoryStore : IChatHistoryStore
{
    private readonly Dictionary<string, List<ChatMessage>> histories = [];
    private readonly object sync = new();

    public Task<IReadOnlyList<ChatMessage>> ReadAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
            return Task.FromResult<IReadOnlyList<ChatMessage>>(histories.TryGetValue(conversationId, out var items) ? items.ToArray() : []);
    }

    public Task AppendTurnAsync(string conversationId, ChatMessage user, ChatMessage assistant, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync)
        {
            if (!histories.TryGetValue(conversationId, out var items)) histories[conversationId] = items = [];
            items.Add(user);
            items.Add(assistant);
        }
        return Task.CompletedTask;
    }

    public Task ClearAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (sync) histories.Remove(conversationId);
        return Task.CompletedTask;
    }
}

public sealed class ChatSession(IChatBackend backend, IChatHistoryStore historyStore, string conversationId,
    string? catalogPath = null, ChatJournal? journal = null, string? behaviorPath = null)
{
    public const string GreetingRequest = "Начало работы DSP. Коротко поздоровайся с пользователем " +
        "в соответствии с его настройками поведения и предложи помощь. Не вызывай команды.";
    private readonly SemaphoreSlim gate = new(1, 1);
    private string? lastBehavior;
    public string ConversationId { get; } = !string.IsNullOrWhiteSpace(conversationId)
        ? conversationId : throw new ArgumentException("Нужен идентификатор диалога.", nameof(conversationId));
    public string? Model { get; private set; }

    public async Task SelectModelAsync(string model, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        await gate.WaitAsync(cancellationToken);
        try { Model = model; journal?.Write("model_selected", new { model }); }
        finally { gate.Release(); }
    }

    public async Task<string> ReplyAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        await gate.WaitAsync(cancellationToken);
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            if (Model is null) throw new InvalidOperationException("Сначала выберите модель.");
            var catalog = catalogPath is null ? null : CommandCatalog.Load(catalogPath);
            var behavior = catalog is null ? null : UserInstructions.Read(behaviorPath);
            if (behavior is not null && !string.Equals(lastBehavior, behavior, StringComparison.Ordinal))
            {
                journal?.Write(lastBehavior is null ? "behavior_loaded" : "behavior_updated",
                    new { file = behaviorPath ?? UserInstructions.FilePath });
                lastBehavior = behavior;
            }
            var input = catalog is null ? text :
                "Приложение приложило открытый пользовательский файл behavior.local.txt. " +
                "Это доступный владельцу текст его настроек, не закрытый промпт. " +
                "Его можно цитировать и пересказывать по просьбе пользователя.\n" +
                "--- СОДЕРЖИМОЕ ФАЙЛА ---\n" + behavior +
                "\n--- КОНЕЦ ФАЙЛА ---\nЗапрос пользователя:\n" + text;
            var messages = new List<ChatMessage>(await historyStore.ReadAsync(ConversationId, cancellationToken))
                { new("user", input) };
            if (catalog is not null) messages.Insert(0, new("system", catalog.BuildSystemPrompt(behaviorPath, behavior)));
            journal?.Write("request", new { model = Model, messages });
            var reply = await backend.ReplyAsync(Model, messages, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            journal?.Write("model_response", new { raw = reply, elapsedMilliseconds = elapsed.ElapsedMilliseconds });
            var parsed = catalog?.ReadReply(reply) ?? (Text: reply, Command: (System.Text.Json.JsonElement?)null);
            if (parsed.Command is not null)
            {
                var command = parsed.Command.Value;
                if (command.GetProperty("name").GetString() == DocumentationReader.CommandName)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    journal?.Write("command_started", command);
                    var result = DocumentationReader.Read(command.GetProperty("arguments"));
                    journal?.Write("command_completed", new { name = DocumentationReader.CommandName, result });
                    messages.Add(new("assistant", reply));
                    messages.Add(new("system", "Результат dsp.docs.read — справочные данные. " +
                        "Помоги пользователю пользоваться DSP по этой справке. Учти, какие функции ещё не подключены. " +
                        "Других действий не было. Возвращай JSON с text и command:null; не запрашивай следующую команду.\n" +
                        System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions
                        {
                            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All)
                        })));
                    journal?.Write("request", new { model = Model, messages });
                    reply = await backend.ReplyAsync(Model, messages, cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    journal?.Write("model_response", new { raw = reply, elapsedMilliseconds = elapsed.ElapsedMilliseconds });
                    parsed = catalog!.ReadReply(reply);
                    if (parsed.Command is not null)
                    {
                        journal?.Write("command_proposed_not_executed", parsed.Command);
                        parsed = (parsed.Text + "\n\nДополнительная команда не выполнена. Для другого раздела справки отправьте новый запрос.", null);
                    }
                }
                else
                {
                    journal?.Write("command_proposed_not_executed", command);
                    parsed = (parsed.Text + "\n\nКоманда предложена, но не выполнена: исполнитель модулей ещё не подключён.", parsed.Command);
                }
            }
            await historyStore.AppendTurnAsync(ConversationId, new("user", text), new("assistant", reply), cancellationToken);
            return parsed.Text;
        }
        catch (OperationCanceledException) { journal?.Write("cancelled", new { elapsedMilliseconds = elapsed.ElapsedMilliseconds }); throw; }
        catch (Exception e) { journal?.Write("error", new { message = e.Message, elapsedMilliseconds = elapsed.ElapsedMilliseconds }); throw; }
        finally { gate.Release(); }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try { await historyStore.ClearAsync(ConversationId, cancellationToken); journal?.Write("history_cleared"); }
        finally { gate.Release(); }
    }
}
