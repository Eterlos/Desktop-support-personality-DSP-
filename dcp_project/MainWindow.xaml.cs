using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace dcp_project;

public partial class MainWindow : Window
{
    private string _currentCharacter = "base_normal";
    private ChatWindow? _chatWindow;
    private BufferWindow? _bufferWindow;
    private const int MaxBufferedItems = 20;

    public ObservableCollection<BufferItem> BufferedItems { get; } = new();

    // --- Плеер ---
    public class MusicTrack
    {
        public string FilePath { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    public ObservableCollection<MusicTrack> Playlist { get; } = new();
    private int _currentTrackIndex = -1;
    private bool _isPlaying = false;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;

        // Авто-переход к следующему треку по окончании текущего
        MediaElement.MediaEnded += (_, _) =>
        {
            if (Playlist.Count == 0) return;
            _currentTrackIndex = (_currentTrackIndex + 1) % Playlist.Count;
            PlayTrack(_currentTrackIndex);
        };

        // Показ зон при перетаскивании файла над окном (не зависит от элемента под курсором)
        this.DragEnter += MainWindow_DragEnter;
        this.DragOver += MainWindow_DragOver;
        this.DragLeave += MainWindow_DragLeave;
        this.Drop += (_, _) => HideDropZones(); // Скрывать зоны, если бросили мимо

    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        AssistantImage.Source = LoadCharacter(_currentCharacter);

        // Подвязываем меню из ресурсов
        AssistantImage.ContextMenu = FindResource("AssistantMenu") as ContextMenu;
        if (AssistantImage.ContextMenu is { } playerMenu)
        {
            // Шаблон контекстного меню создаёт ListBox отдельно от визуального дерева окна.
            // Привязываем список при открытии меню, а не только при наведении на кнопку.
            playerMenu.Opened += (_, _) => BindPlaylistToListBox(playerMenu);
        }

        // Регистрируем события мыши (перемещение окна)
        AssistantImage.MouseLeftButtonDown += AssistantImage_MouseLeftButtonDown;
        AssistantImage.MouseMove += AssistantImage_MouseMove;
        AssistantImage.MouseLeftButtonUp += AssistantImage_MouseLeftButtonUp;

        // Добавляем обработчик нажатия ПРАВОЙ кнопки мыши для крестового меню
        AssistantImage.MouseRightButtonDown += AssistantImage_MouseRightButtonDown;

        _chatWindow = new ChatWindow { Owner = this };
        _ = _chatWindow.PrepareAsync();
        Closed += (_, _) =>
        {
            _chatWindow.Shutdown();
            _bufferWindow?.Close();
        };
    }

    private void OpenChat_Click(object sender, RoutedEventArgs e)
    {
        if (AssistantImage.ContextMenu is { } menu) menu.IsOpen = false;
        if (_chatWindow is null) return;
        _ = _chatWindow.PrepareAsync();
        _chatWindow.Show();
        if (_chatWindow.WindowState == WindowState.Minimized)
            _chatWindow.WindowState = WindowState.Normal;
        _chatWindow.Activate();
    }

    private void OpenBuffer_Click(object sender, RoutedEventArgs e)
    {
        if (AssistantImage.ContextMenu is { } menu) menu.IsOpen = false;
        ShowBufferWindow();
    }

    private void ShowBufferWindow()
    {
        if (_bufferWindow is null)
        {
            _bufferWindow = new BufferWindow(BufferedItems) { Owner = this };
            _bufferWindow.Closed += (_, _) => _bufferWindow = null;
        }
        _bufferWindow.Show();
        if (_bufferWindow.WindowState == WindowState.Minimized)
            _bufferWindow.WindowState = WindowState.Normal;
        _bufferWindow.Activate();
    }

    private void SetCharacter(string characterName)
    {
        _currentCharacter = characterName;
        AssistantImage.Source = LoadCharacter(characterName);
    }

    private ImageSource LoadCharacter(string characterName)
    {
        // base_normal -> pack://application:,,,/dcp_project;component/assets/base/base_normal.png
        var baseName = characterName.Split('_')[0].ToLowerInvariant();
        var uri = new Uri($"""pack://application:,,,/dcp_project;component/assets/{baseName}/{characterName}.png""", UriKind.Absolute);

        try
        {
            var streamInfo = Application.GetResourceStream(uri);
            if (streamInfo?.Stream != null)
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = streamInfo.Stream;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
        }
        catch
        {
            // Игнорируем ошибку — пойдёт fallback
        }

        // Fallback: цветной квадрат, если картинка не загрузилась
        return CreateSolidSquare(200, Colors.DarkOrchid);
    }

    private static BitmapSource CreateSolidSquare(int size, Color color)
    {
        var bitmap = new WriteableBitmap(size, size, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[size * size * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.B;
            pixels[i + 1] = color.G;
            pixels[i + 2] = color.R;
            pixels[i + 3] = color.A;
        }
        bitmap.WritePixels(new Int32Rect(0, 0, size, size), pixels, size * 4, 0);
        return bitmap;
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var settings = new CharacterSettingsWindow(_currentCharacter);
        if (settings.ShowDialog() == true)
        {
            SetCharacter(settings.SelectedCharacter);
        }
    }

    private void CloseApp_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    // --- Перетаскивание левой кнопкой мыши ---
    private bool _isMouseDown;
    private bool _isDragging;
    private Point _lastPosition;
    private Point _dragStartPoint;
    private const double DragThreshold = 3.0;

    private void AssistantImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _isMouseDown = true;
        _isDragging = false;
        _dragStartPoint = e.GetPosition(this);
        _lastPosition = _dragStartPoint;
        AssistantImage.CaptureMouse();
    }

    private void AssistantImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isMouseDown) return;
        var currentPosition = e.GetPosition(this);

        if (!_isDragging)
        {
            var distance = Math.Sqrt(Math.Pow(currentPosition.X - _dragStartPoint.X, 2) + Math.Pow(currentPosition.Y - _dragStartPoint.Y, 2));
            if (distance < DragThreshold) return;
            _isDragging = true;
            AssistantImage.Cursor = Cursors.SizeAll;
        }

        var deltaX = currentPosition.X - _lastPosition.X;
        var deltaY = currentPosition.Y - _lastPosition.Y;

        ImageTransform.X += deltaX;
        ImageTransform.Y += deltaY;
        _lastPosition = currentPosition;
    }

    private void AssistantImage_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isMouseDown) return;
        _isMouseDown = false;
        AssistantImage.ReleaseMouseCapture();
        _isDragging = false;
        AssistantImage.Cursor = Cursors.Hand;
    }

    // --- Логика КРЕСТОВОГО МЕНЮ по клику/зажатию ПКМ ---
    private void AssistantImage_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (AssistantImage.ContextMenu is { } menu)
        {
            // Задаем выравнивание контекстного меню точно по центру нашего персонажа
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Center;
            menu.PlacementTarget = AssistantImage;

            // Открываем меню
            menu.IsOpen = true;

            // Помечаем событие обработанным, чтобы стандартное меню Windows не пыталось перехватить клик
            e.Handled = true;
        }
    }

    // ================= ПЛЕЕР =================

    /// <summary>Найти дочерний элемент типа T в дереве визуализации</summary>
    private static T? FindVisualChild<T>(DependencyObject obj) where T : DependencyObject
    {
        if (obj == null) return null;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = VisualTreeHelper.GetChild(obj, i);
            if (child is T result) return result;
            var found = FindVisualChild<T>(child);
            if (found != null) return found;
        }
        return null;
    }

    /// <summary>Получить Grid, содержащий кнопку плеера и popup (внутри шаблона ContextMenu)</summary>
    private Grid? GetPlayerGrid(Button btn)
    {
        var parent = VisualTreeHelper.GetParent(btn);
        return parent as Grid;
    }

    /// <summary>Получить ListBox плейлиста из Grid плеера</summary>
    private ListBox? GetTrackList(Grid? grid)
    {
        if (grid == null) return null;
        return FindVisualChild<ListBox>(grid);
    }

    private void BindPlaylistToListBox(ContextMenu? menu)
    {
        if (menu == null) return;
        // Элементы внутри Popup находятся в отдельном визуальном дереве,
        // поэтому FindVisualChild от окна/контекстного меню их не находит.
        var listBox = menu.Template?.FindName("PlayerTrackList", menu) as ListBox;
        if (listBox != null && listBox.ItemsSource != Playlist)
            listBox.ItemsSource = Playlist;
    }

    /// <summary>Получить Popup управления из Grid плеера</summary>
    private Popup? GetPlayerPopup(Grid? grid)
    {
        if (grid == null) return null;
        return FindVisualChild<Popup>(grid);
    }

    /// <summary>Привязать плейлист к ListBox (вызывается при взаимодействии с кнопкой)</summary>
    private void BindPlaylistToListBox(Button btn)
    {
        BindPlaylistToListBox(AssistantImage.ContextMenu);
    }

    /// <summary>Добавить трек в плейлист</summary>
    private void AddTrack(string filePath)
    {
        if (Playlist.Any(t => string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase)))
            return;
        var name = Path.GetFileName(filePath);
        Playlist.Add(new MusicTrack { FilePath = filePath, DisplayName = name });
        BindPlaylistToListBox(AssistantImage.ContextMenu);

        // Если это первый трек — выделяем его (но не начинаем играть автоматически)
        if (_currentTrackIndex < 0)
        {
            _currentTrackIndex = 0;
        }
    }

    /// <summary>Начать воспроизведение трека по индексу</summary>
    private void PlayTrack(int index)
    {
        if (Playlist.Count == 0 || index < 0 || index >= Playlist.Count) return;

        _currentTrackIndex = index;

        // Выделяем трек в списке
        var listBox = FindVisualChild<ListBox>(this);
        if (listBox != null)
            listBox.SelectedIndex = index;

        try
        {
            MediaElement.Source = new Uri(Playlist[index].FilePath, UriKind.Absolute);
            MediaElement.Play();
            _isPlaying = true;
        }
        catch
        {
            // Игнорируем ошибки открытия файла
        }
    }

    // --- Drag & Drop .mp3 на кнопку плеера ---

    private void PlayerButton_DragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Any(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)))
                e.Effects = DragDropEffects.Copy;
            else
                e.Effects = DragDropEffects.None;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;

        // Привязываем плейлист к ListBox, когда пользователь наводит файл над кнопкой
        if (sender is Button btn)
            BindPlaylistToListBox(btn);
    }

    private void PlayerButton_Drop(object sender, DragEventArgs e)
    {
        if (sender is Button btn)
            BindPlaylistToListBox(btn);

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            foreach (var file in files.Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)))
                AddTrack(file);
        }
        e.Handled = true;
    }

    // --- Открытие/закрытие popup при наведении ---

    private void PlayerButton_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is Button btn)
        {
            BindPlaylistToListBox(btn);

            var grid = GetPlayerGrid(btn);
            var popup = GetPlayerPopup(grid);
            if (popup != null)
            {
                popup.PlacementTarget = btn;
                popup.Placement = PlacementMode.Right;
                popup.SetCurrentValue(Popup.IsOpenProperty, true);
            }
        }
    }



    private void PlayerButton_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Button btn)
        {
            var grid = GetPlayerGrid(btn);
            var popup = GetPlayerPopup(grid);
            if (popup != null && !popup.IsMouseOver)
                popup.SetCurrentValue(Popup.IsOpenProperty, false);
        }
    }

    private void PlayerPopup_MouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is Popup popup)
        {
            var grid = VisualTreeHelper.GetParent(popup) as Grid;
            if (grid != null)
            {
                var btn = FindVisualChild<Button>(grid);
                if (btn != null && !btn.IsMouseOver)
                    popup.SetCurrentValue(Popup.IsOpenProperty, false);
            }
        }
    }

    // --- Управление треками ---

    private void TrackList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedIndex >= 0)
            PlayTrack(listBox.SelectedIndex);
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (Playlist.Count == 0) return;

        if (_currentTrackIndex < 0)
        {
            PlayTrack(0);
            return;
        }

        if (_isPlaying)
        {
            MediaElement.Pause();
            _isPlaying = false;
            if (sender is Button btn) btn.Content = "▶";
        }
        else
        {
            MediaElement.Play();
            _isPlaying = true;
            if (sender is Button btn) btn.Content = "⏸";
        }
    }

    private void NextTrack_Click(object sender, RoutedEventArgs e)
    {
        if (Playlist.Count == 0) return;
        _currentTrackIndex = (_currentTrackIndex + 1) % Playlist.Count;
        PlayTrack(_currentTrackIndex);
    }

    private void PrevTrack_Click(object sender, RoutedEventArgs e)
    {
        if (Playlist.Count == 0) return;
        _currentTrackIndex = (_currentTrackIndex - 1 + Playlist.Count) % Playlist.Count;
        PlayTrack(_currentTrackIndex);
    }

    private void SpeedToggle_Click(object sender, RoutedEventArgs e)
    {
        MediaElement.SpeedRatio = MediaElement.SpeedRatio == 2.0 ? 1.0 : 2.0;
        if (sender is Button btn)
            btn.Content = MediaElement.SpeedRatio == 2.0 ? "2×" : "1×";
    }

    private void Rewind10_Click(object sender, RoutedEventArgs e)
    {
        if (MediaElement.Source is null) return;
        var target = TimeSpan.FromSeconds(Math.Max(0, MediaElement.Position.TotalSeconds - 10));
        MediaElement.Position = target;
    }

    private void Forward10_Click(object sender, RoutedEventArgs e)
    {
        if (MediaElement.Source is null) return;
        var target = TimeSpan.FromSeconds(MediaElement.Position.TotalSeconds + 10);
        MediaElement.Position = target;
    }

    // ================= ДВУХЗОННЫЙ ДРОП (плеер + буфер) =================

    private void ShowDropZones()
    {
        // Почти невидимый фон перехватывает мышь над пустым пространством во время Drag&Drop
        this.Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        DropZonePlayer.Visibility = Visibility.Visible;
        DropZoneBuffer.Visibility = Visibility.Visible;
    }

    private void HideDropZones()
    {
        // Возвращаем полную прозрачность для кликов сквозь окно
        this.Background = null;
        DropZonePlayer.Visibility = Visibility.Collapsed;
        DropZoneBuffer.Visibility = Visibility.Collapsed;
    }

    private void MainWindow_DragEnter(object sender, DragEventArgs e)
    {
        if (!IsBufferData(e.Data) && !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        ShowDropZones();
    }

private void MainWindow_DragOver(object sender, DragEventArgs e)
{
    e.Effects = IsBufferData(e.Data) || e.Data.GetDataPresent(DataFormats.FileDrop)
        ? DragDropEffects.Copy
        : DragDropEffects.None;
}


    private void MainWindow_DragLeave(object sender, DragEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (pos.X <= 0 || pos.Y <= 0 || pos.X >= ActualWidth || pos.Y >= ActualHeight)
        {
            HideDropZones();
        }
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        var isPlayerZone = ReferenceEquals(sender, DropZonePlayer);
        var canDrop = isPlayerZone
            ? e.Data.GetDataPresent(DataFormats.FileDrop) &&
              e.Data.GetData(DataFormats.FileDrop) is string[] files &&
              files.Any(file => file.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            : IsBufferData(e.Data) || e.Data.GetDataPresent(DataFormats.FileDrop);
        if (canDrop)
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    /// <summary>Бросание файла в левую зону — добавить в плейлист плеера</summary>
    private void DropZonePlayer_Drop(object sender, DragEventArgs e)
    {
        HideDropZones();

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            foreach (var file in files.Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)))
                AddTrack(file);
        }
        e.Handled = true;
    }

    private static bool IsBufferData(IDataObject data) =>
        data.GetDataPresent(DataFormats.UnicodeText) ||
        data.GetDataPresent(DataFormats.Text) ||
        data.GetDataPresent(DataFormats.Bitmap);

    /// <summary>Сохраняет текст, изображения и файлы в буфере</summary>
    private void DropZoneBuffer_Drop(object sender, DragEventArgs e)
    {
        HideDropZones();
        if (IsBufferData(e.Data) || e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var added = AddDroppedDataToBuffer(e.Data);
            if (added > 0)
                ShowBufferWindow();
        }
        e.Handled = true;
    }

    private int AddDroppedDataToBuffer(IDataObject data)
    {
        var added = 0;
        if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files)
        {
            foreach (var file in files.Where(File.Exists))
            {
                AddBufferFile(file);
                added++;
            }
        }

        if (data.GetDataPresent(DataFormats.Bitmap) && data.GetData(DataFormats.Bitmap) is BitmapSource bitmap)
        {
            var imagePath = Path.Combine(Path.GetTempPath(), $"dcp-buffer-{Guid.NewGuid():N}.png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(imagePath)) encoder.Save(stream);
            AddBufferFile(imagePath, "Изображение");
            added++;
        }

        string? text = null;
        if (data.GetDataPresent(DataFormats.UnicodeText))
            text = data.GetData(DataFormats.UnicodeText) as string;
        else if (data.GetDataPresent(DataFormats.Text))
            text = data.GetData(DataFormats.Text) as string;

        if (!string.IsNullOrWhiteSpace(text))
        {
            AddBufferText(text);
            added++;
        }

        return added;
    }

    private void AddBufferFile(string filePath, string? titleOverride = null)
    {
        var isImage = IsImageFile(filePath);
        BufferedItems.Add(new BufferItem
        {
            Kind = isImage ? "ФОТО" : "ФАЙЛ",
            Title = titleOverride ?? Path.GetFileName(filePath),
            PreviewText = isImage ? "Изображение можно перетащить в Telegram или другое приложение." : filePath,
            FilePath = filePath,
            PreviewImage = isImage ? LoadImagePreview(filePath) : null
        });
        TrimBuffer();
    }

    private void AddBufferText(string text)
    {
        var preview = text.Length > 360 ? text[..360] + "\n… (текст сокращён в превью)" : text;
        BufferedItems.Add(new BufferItem
        {
            Kind = "ТЕКСТ",
            Title = $"Текст · {text.Length:N0} символов",
            PreviewText = preview,
            Text = text
        });
        TrimBuffer();
    }

    private void TrimBuffer()
    {
        while (BufferedItems.Count > MaxBufferedItems)
            BufferedItems.RemoveAt(0);
    }

    private static bool IsImageFile(string path) =>
        new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp" }
            .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static ImageSource? LoadImagePreview(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.DecodePixelWidth = 160;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Бросание файла прямо на персонажа — добавить в плейлист плеера</summary>
    private void AssistantImage_Drop(object sender, DragEventArgs e)
    {
        HideDropZones();

        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            foreach (var file in files.Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase)))
                AddTrack(file);
        }
        e.Handled = true;
    }
}
