using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace dcp_project;

public partial class MainWindow : Window
{
    private string _currentCharacter = "base_normal";
    private ChatWindow? _chatWindow;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        AssistantImage.Source = LoadCharacter(_currentCharacter);

        // Подвязываем меню из ресурсов
        AssistantImage.ContextMenu = FindResource("AssistantMenu") as ContextMenu;

        // Регистрируем события мыши
        AssistantImage.MouseLeftButtonDown += AssistantImage_MouseLeftButtonDown;
        AssistantImage.MouseMove += AssistantImage_MouseMove;
        AssistantImage.MouseLeftButtonUp += AssistantImage_MouseLeftButtonUp;

        // Добавляем обработчик нажатия ПРАВОЙ кнопки мыши для крестового меню
        AssistantImage.MouseRightButtonDown += AssistantImage_MouseRightButtonDown;

        _chatWindow = new ChatWindow { Owner = this };
        _ = _chatWindow.PrepareAsync();
        Closed += (_, _) => _chatWindow.Shutdown();
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
}
