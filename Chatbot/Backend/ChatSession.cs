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

public sealed class ChatSession(IChatBackend backend, IChatHistoryStore historyStore, string conversationId)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public string ConversationId { get; } = !string.IsNullOrWhiteSpace(conversationId)
        ? conversationId : throw new ArgumentException("Нужен идентификатор диалога.", nameof(conversationId));
    public string? Model { get; private set; }

    public async Task SelectModelAsync(string model, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        await gate.WaitAsync(cancellationToken);
        try { Model = model; } // The conversation belongs to the user, not to the model.
        finally { gate.Release(); }
    }

    public async Task<string> ReplyAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (Model is null) throw new InvalidOperationException("Сначала выберите модель.");
            var messages = new List<ChatMessage>(await historyStore.ReadAsync(ConversationId, cancellationToken))
                { new("user", text) };
            var reply = await backend.ReplyAsync(Model, messages, cancellationToken);
            await historyStore.AppendTurnAsync(ConversationId, new("user", text), new("assistant", reply), cancellationToken);
            return reply;
        }
        finally { gate.Release(); }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try { await historyStore.ClearAsync(ConversationId, cancellationToken); }
        finally { gate.Release(); }
    }
}
