namespace Dsp.Chatbot;

public sealed record ChatMessage(string Role, string Content);

public interface IChatBackend
{
    Task<IReadOnlyList<string>> GetModelsAsync(CancellationToken cancellationToken = default);

    Task<string> ReplyAsync(
        string model,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken = default);
}

public sealed class ChatBackendException(string message, Exception? innerException = null)
    : Exception(message, innerException);
