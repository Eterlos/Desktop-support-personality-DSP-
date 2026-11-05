using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Dsp.Chatbot;

namespace dcp_project;

public partial class ChatWindow : Window
{
    private readonly LocalChatBackend _backend = new(LocalOllama.BaseUrl);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<ChatMessage> _history = [];
    private CancellationTokenSource? _request;
    private Task _initialization = Task.CompletedTask;
    private Task _sending = Task.CompletedTask;
    private bool _ready;
    private bool _busy;
    private bool _shutdown;

    public ChatWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await PrepareAsync();
    }

    internal Task PrepareAsync()
    {
        if (_shutdown || _busy || _ready) return _initialization;
        _initialization = InitializeAsync();
        return _initialization;
    }

    private async Task InitializeAsync()
    {
        _ready = false;
        _busy = true;
        UpdateButtons();
        StatusText.Text = $"Загрузка {LocalOllama.Model}…";
        try
        {
            await LocalOllama.PrepareAsync(_lifetime.Token);
            if (_shutdown) return;
            _ready = true;
            StatusText.Text = $"Готово · {LocalOllama.Model}";
            if (IsVisible) MessageInput.Focus();
        }
        catch (OperationCanceledException) when (_shutdown) { }
        catch (Exception exception)
        {
            if (!_shutdown) StatusText.Text = $"Не удалось подключиться: {exception.Message}";
        }
        finally { _busy = false; if (!_shutdown) UpdateButtons(); }
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready || _busy || _shutdown || string.IsNullOrWhiteSpace(MessageInput.Text)) return;
        var text = MessageInput.Text.Trim();
        _sending = SendAsync(text);
        await _sending;
    }

    private async Task SendAsync(string text)
    {
        _busy = true;
        _request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        UpdateButtons();
        MessageInput.Clear();
        Append($"Вы: {text}");
        StatusText.Text = "Модель отвечает…";
        try
        {
            var messages = new List<ChatMessage>(_history) { new("user", text) };
            var reply = await _backend.ReplyAsync(LocalOllama.Model, messages, _request.Token);
            if (_shutdown) return;
            _history.Add(new("user", text));
            _history.Add(new("assistant", reply));
            if (_history.Count > 20) _history.RemoveRange(0, _history.Count - 20);
            Append($"Бот: {reply}");
            StatusText.Text = $"Готово · {LocalOllama.Model}";
        }
        catch (OperationCanceledException)
        {
            if (!_shutdown) { Append("Запрос отменён."); StatusText.Text = "Запрос отменён. Можно написать снова."; }
        }
        catch (Exception exception)
        {
            if (!_shutdown) { Append($"Ошибка: {exception.Message}"); StatusText.Text = "Ошибка запроса. Можно повторить."; }
        }
        finally
        {
            _request.Dispose();
            _request = null;
            _busy = false;
            if (!_shutdown) { UpdateButtons(); MessageInput.Focus(); }
        }
    }

    private void Append(string text)
    {
        Transcript.AppendText(text + Environment.NewLine + Environment.NewLine);
        Transcript.ScrollToEnd();
    }

    private void UpdateButtons()
    {
        SendButton.IsEnabled = _ready && !_busy;
        MessageInput.IsEnabled = !_busy;
        ClearButton.IsEnabled = !_busy;
        CancelButton.IsEnabled = _request is not null;
    }

    private void MessageInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Send_Click(sender, new RoutedEventArgs());
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _request?.Cancel();

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _history.Clear();
        Transcript.Clear();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_shutdown)
        {
            e.Cancel = true;
            _request?.Cancel();
            Hide();
        }
        base.OnClosing(e);
    }

    internal void Shutdown()
    {
        if (_shutdown) return;
        _shutdown = true;
        _lifetime.Cancel();
        Close();
        _ = DisposeAfterRequestsAsync();
    }

    private async Task DisposeAfterRequestsAsync()
    {
        try { await Task.WhenAll(_initialization, _sending); }
        finally { _backend.Dispose(); _lifetime.Dispose(); }
    }
}
