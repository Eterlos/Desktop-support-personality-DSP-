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
    Console.WriteLine("Сначала запустите сервер модели в Ollama или LM Studio.");
    var url = Environment.GetEnvironmentVariable("DSP_CHAT_BASE_URL");
    if (string.IsNullOrWhiteSpace(url))
    {
        Console.Write("Адрес API [http://localhost:1234/v1/]: ");
        var input = Console.ReadLine();
        if (input is null) return;
        url = string.IsNullOrWhiteSpace(input) ? "http://localhost:1234/v1/" : input.Trim();
    }

    using var backend = new LocalChatBackend(url, Environment.GetEnvironmentVariable("DSP_CHAT_API_KEY"));
    await PrintModelsAsync();
    var model = Environment.GetEnvironmentVariable("DSP_CHAT_MODEL")?.Trim();
    if (string.IsNullOrWhiteSpace(model))
    {
        Console.Write("Название модели: ");
        model = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(model)) return;
    }

    var history = new List<ChatMessage>();
    Console.WriteLine("Команды: /models, /model <название>, /clear, /exit. Ctrl+C — выход.");
    while (!cancellation.IsCancellationRequested)
    {
        Console.Write("Вы: ");
        var input = Console.ReadLine();
        if (input is null || input.Trim() == "/exit") break;
        input = input.Trim();
        if (input.Length == 0) continue;
        if (input == "/models") { await PrintModelsAsync(); continue; }
        if (input == "/clear") { history.Clear(); Console.WriteLine("Диалог очищен."); continue; }
        if (input.StartsWith("/model ", StringComparison.Ordinal))
        {
            var selected = input[7..].Trim();
            if (selected.Length == 0) { Console.WriteLine("Укажите название модели."); continue; }
            model = selected;
            history.Clear();
            Console.WriteLine($"Выбрана модель: {model}. Диалог очищен.");
            continue;
        }

        var pending = new List<ChatMessage>(history) { new("user", input) };
        try
        {
            var reply = await backend.ReplyAsync(model, pending, cancellation.Token);
            history.Add(new ChatMessage("user", input));
            history.Add(new ChatMessage("assistant", reply));
            if (history.Count > 20) history.RemoveRange(0, history.Count - 20);
            Console.WriteLine($"Бот: {reply}");
        }
        catch (ChatBackendException exception) { Console.WriteLine($"Ошибка: {exception.Message}"); }
    }

    async Task PrintModelsAsync()
    {
        try
        {
            var models = await backend.GetModelsAsync(cancellation.Token);
            Console.WriteLine(models.Count == 0 ? "Сервер не вернул доступных моделей." : "Модели: " + string.Join(", ", models));
        }
        catch (ChatBackendException exception) { Console.WriteLine(exception.Message); }
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
