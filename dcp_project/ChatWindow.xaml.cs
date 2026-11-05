using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using Dsp.Chatbot;

namespace dcp_project;

public partial class ChatWindow : Window
{
    private readonly LocalChatBackend _backend = new(LocalOllama.BaseUrl);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly ChatSession _session;
    private bool _updatingModels;
    private CancellationTokenSource? _request;
    private Task _initialization = Task.CompletedTask;
    private Task _sending = Task.CompletedTask;
    private bool _ready;
    private bool _busy;
    private bool _shutdown;

    public ChatWindow()
    {
        _session = new ChatSession(_backend, new MemoryChatHistoryStore(), Guid.NewGuid().ToString());
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
        StatusText.Text = "Получение локальных моделей Ollama…";
        try
        {
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

    private async Task SendAsync(string text)
    {
        _busy = true;
        _request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        UpdateButtons();
        MessageInput.Clear();
        AppendMessage(Environment.MachineName, text, false);
        StatusText.Text = "Модель отвечает…";
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
            _request.Dispose();
            _request = null;
            _busy = false;
            if (!_shutdown) { UpdateButtons(); MessageInput.Focus(); }
        }
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

    private void Cancel_Click(object sender, RoutedEventArgs e) => _request?.Cancel();

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
