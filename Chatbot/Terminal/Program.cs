using System.Text;
using Dsp.Chatbot;

Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    Console.WriteLine("DSP: терминал локального чат-бота");
    Console.WriteLine("Сначала запустите Ollama с установленной локальной моделью.");
    UserInstructions.EnsureExists();
    Console.WriteLine("Инструкции поведения: " + UserInstructions.FilePath);
    var url = Environment.GetEnvironmentVariable("DSP_CHAT_BASE_URL");
    if (string.IsNullOrWhiteSpace(url))
    {
        Console.Write("Адрес Ollama [http://localhost:11434/v1/]: ");
        var input = Console.ReadLine();
        if (input is null) return;
        url = string.IsNullOrWhiteSpace(input) ? "http://localhost:11434/v1/" : input.Trim();
    }

    using var backend = new LocalChatBackend(url, jsonResponses: true);
    await PrintModelsAsync();
    var model = Environment.GetEnvironmentVariable("DSP_CHAT_MODEL")?.Trim();
    if (string.IsNullOrWhiteSpace(model))
    {
        Console.Write("Название модели: ");
        model = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(model)) return;
    }

    var conversationId = Guid.NewGuid().ToString();
    var journal = new ChatJournal(conversationId);
    var catalogPath = Path.Combine(AppContext.BaseDirectory, "commands.json");
    CommandCatalog.Load(catalogPath);
    var session = new ChatSession(backend, new MemoryChatHistoryStore(), conversationId, catalogPath, journal);
    await session.SelectModelAsync(model, cancellation.Token);
    Console.WriteLine("Локальный журнал: " + journal.FilePath);
    try
    {
        await EnsureLocalModelAsync(model);
        var greeting = await session.ReplyAsync(ChatSession.GreetingRequest, cancellation.Token);
        Console.WriteLine($"DSP: {greeting}");
    }
    catch (ChatBackendException exception) { Console.WriteLine($"Не удалось получить приветствие: {exception.Message}"); }
    Console.WriteLine("Команды: /models, /model <название>, /clear, /exit. Ctrl+C — выход.");
    while (!cancellation.IsCancellationRequested)
    {
        Console.Write($"{Environment.MachineName}: ");
        var input = Console.ReadLine();
        if (input is null || input.Trim() == "/exit") break;
        input = input.Trim();
        if (input.Length == 0) continue;
        if (input == "/models") { await PrintModelsAsync(); continue; }
        if (input == "/clear") { await session.ClearAsync(cancellation.Token); Console.WriteLine("Диалог очищен."); continue; }
        if (input.StartsWith("/model ", StringComparison.Ordinal))
        {
            var selected = input[7..].Trim();
            if (selected.Length == 0) { Console.WriteLine("Укажите название модели."); continue; }
            try
            {
                await EnsureLocalModelAsync(selected);
                await session.SelectModelAsync(selected, cancellation.Token);
                model = selected;
                Console.WriteLine($"Выбрана модель: {model}. История диалога сохранена.");
            }
            catch (ChatBackendException exception) { Console.WriteLine($"Ошибка: {exception.Message}"); }
            continue;
        }

        try
        {
            await EnsureLocalModelAsync(model);
            var reply = await session.ReplyAsync(input, cancellation.Token);
            Console.WriteLine($"DSP: {reply}");
            if (journal.LastError is { } error) Console.WriteLine(error);
        }
        catch (ChatBackendException exception) { Console.WriteLine($"Ошибка: {exception.Message}"); }
    }

    async Task PrintModelsAsync()
    {
        try
        {
            var models = await OllamaModelCatalog.GetModelsAsync(url, cancellation.Token);
            Console.WriteLine(models.Count == 0 ? "Сервер не вернул доступных моделей." : "Модели: " + string.Join(", ", models));
        }
        catch (ChatBackendException exception) { Console.WriteLine(exception.Message); }
    }

    async Task EnsureLocalModelAsync(string selected)
    {
        var models = await OllamaModelCatalog.GetModelsAsync(url, cancellation.Token);
        if (!models.Contains(selected, StringComparer.Ordinal))
            throw new ChatBackendException("Выберите установленную локальную модель Ollama из списка /models. Облачные модели не поддерживаются.");
    }
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
    Console.WriteLine("Запрос отменён.");
}
catch (ArgumentException exception)
{
    Console.Error.WriteLine(exception.Message);
    Environment.ExitCode = 1;
}
catch (InvalidDataException exception)
{
    Console.Error.WriteLine(exception.Message);
    Environment.ExitCode = 1;
}
catch (ChatBackendException exception)
{
    Console.Error.WriteLine(exception.Message);
    Environment.ExitCode = 1;
}
