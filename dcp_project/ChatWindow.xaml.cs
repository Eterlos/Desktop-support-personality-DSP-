using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using Dsp.Chatbot;

namespace dcp_project;

public partial class ChatWindow : Window
{
    private readonly LocalChatBackend _backend = new(LocalOllama.BaseUrl, jsonResponses: true);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ChatSession _session;
    private readonly ChatJournal _journal;
    private readonly Stopwatch _elapsed = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private bool _updatingModels;
    private CancellationTokenSource? _request;
    private Task _initialization = Task.CompletedTask;
    private Task _sending = Task.CompletedTask;
    private bool _ready;
    private bool _busy;
    private bool _shutdown;
    private bool _greetingAttempted;

    public ChatWindow()
    {
        var conversationId = Guid.NewGuid().ToString();
        _journal = new ChatJournal(conversationId);
        _session = new ChatSession(_backend, new MemoryChatHistoryStore(), conversationId,
            Path.Combine(AppContext.BaseDirectory, "commands.json"), _journal);
        InitializeComponent();
        _timer.Tick += (_, _) => UpdateElapsed();
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
        StatusText.Text = "Получение локальных моделей Ollama…";
        try
        {
            UserInstructions.EnsureExists();
            CommandCatalog.Load(Path.Combine(AppContext.BaseDirectory, "commands.json"));
            await LocalOllama.EnsureRunningAsync(_lifetime.Token);
            var models = await LocalOllama.GetModelsAsync(_lifetime.Token);
            if (_shutdown) return;
            _updatingModels = true;
            try
            {
                ModelPicker.ItemsSource = models;
                ModelPicker.SelectedItem = models.Contains(_session.Model) ? _session.Model : models.Count == 1 ? models[0] : null;
            }
            finally { _updatingModels = false; }
            if (ModelPicker.SelectedItem is string selected)
                await LoadModelAsync(selected);
            else StatusText.Text = models.Count == 0
                ? "Нет локальных моделей. Установите модель в Ollama и обновите список."
                : "Выберите локальную модель. История диалога сохраняется при переключении.";
            if (IsVisible) MessageInput.Focus();
        }
        catch (OperationCanceledException) when (_shutdown) { }
        catch (Exception exception)
        {
            if (!_shutdown) StatusText.Text = $"Не удалось подключиться: {exception.Message}";
        }
        finally { _busy = false; if (!_shutdown) UpdateButtons(); }
    }

    private async Task LoadModelAsync(string model)
    {
        _ready = false;
        StatusText.Text = $"Загрузка {model}…";
        await LocalOllama.PrepareModelAsync(model, _lifetime.Token);
        await _session.SelectModelAsync(model, _lifetime.Token);
        if (_shutdown) return;
        _ready = true;
        StatusText.Text = $"Готово · {model}";
        await GreetOnceAsync();
    }

    private Task GreetOnceAsync()
    {
        if (_greetingAttempted || _shutdown) return Task.CompletedTask;
        _greetingAttempted = true;
        _sending = SendCoreAsync(ChatSession.GreetingRequest, true);
        return _sending;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _shutdown) return;
        _initialization = InitializeAsync();
        await _initialization;
    }

    private async void ModelPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingModels || _busy || _shutdown || ModelPicker.SelectedItem is not string model) return;
        _initialization = SwitchModelAsync(model);
        await _initialization;
    }

    private async Task SwitchModelAsync(string model)
    {
        _busy = true;
        UpdateButtons();
        try { await LoadModelAsync(model); }
        catch (OperationCanceledException) when (_shutdown) { }
        catch (Exception exception)
        {
            if (!_shutdown) StatusText.Text = $"Не удалось выбрать модель: {exception.Message}";
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

    private Task SendAsync(string text) => SendCoreAsync(text, false);

    private async Task SendCoreAsync(string text, bool greeting)
    {
        _busy = true;
        _request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _elapsed.Restart();
        UpdateElapsed();
        _timer.Start();
        UpdateButtons();
        if (!greeting)
        {
            MessageInput.Clear();
            AppendMessage(Environment.MachineName, text, false);
        }
        StatusText.Text = greeting ? "DSP готовит приветствие…" : "Модель отвечает…";
        try
        {
            var reply = await _session.ReplyAsync(text, _request.Token);
            if (_shutdown) return;
            AppendMessage("DSP", reply, true);
            StatusText.Text = $"Готово · {_session.Model}";
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
            _elapsed.Stop();
            _timer.Stop();
            UpdateElapsed();
            _request.Dispose();
            _request = null;
            _busy = false;
            if (!_shutdown)
            {
                UpdateButtons(); MessageInput.Focus();
                if (_journal.LastError is { } error) Append(error);
            }
        }
    }

    private void UpdateElapsed()
    {
        var elapsed = _elapsed.Elapsed;
        ElapsedText.Text = $"Время запроса: {(long)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}.{elapsed.Milliseconds:000}";
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void BehaviorLink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        e.Handled = true;
        BehaviorFileLink.Open();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 1) DragMove();
    }

    private void Append(string text)
    {
        MarkdownChatRenderer.Append(Transcript.Document, "", text, false);
        Transcript.ScrollToEnd();
    }

    private void AppendMessage(string author, string text, bool markdown)
    {
        MarkdownChatRenderer.Append(Transcript.Document, author, text, markdown);
        Transcript.ScrollToEnd();
    }

    private void UpdateButtons()
    {
        SendButton.IsEnabled = _ready && !_busy;
        MessageInput.IsEnabled = !_busy;
        ClearButton.IsEnabled = !_busy;
        ModelPicker.IsEnabled = !_busy;
        RefreshButton.IsEnabled = !_busy;
        CancelButton.IsEnabled = _request is not null;
    }

    private void MessageInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Send_Click(sender, new RoutedEventArgs());
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_request is null) return;
        _journal.Write("stop_requested");
        _request.Cancel();
        CancelButton.IsEnabled = false;
        StatusText.Text = "Остановка запроса…";
    }

    private async void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _busy = true;
        UpdateButtons();
        _sending = ClearHistoryAsync();
        await _sending;
    }

    private async Task ClearHistoryAsync()
    {
        try { await _session.ClearAsync(_lifetime.Token); if (!_shutdown) Transcript.Document.Blocks.Clear(); }
        catch (OperationCanceledException) when (_shutdown) { }
        catch (Exception exception) { if (!_shutdown) StatusText.Text = exception.Message; }
        finally { _busy = false; if (!_shutdown) UpdateButtons(); }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_shutdown)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }

    internal void Shutdown()
    {
        if (_shutdown) return;
        _shutdown = true;
        _timer.Stop();
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
