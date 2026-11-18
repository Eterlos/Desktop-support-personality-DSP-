using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace dcp_project;

public partial class BufferWindow : Window
{
    private readonly ObservableCollection<BufferItem> _items;
    private Point _dragStart;
    private bool _dragArmed;

    public BufferWindow(ObservableCollection<BufferItem> items)
    {
        InitializeComponent();
        _items = items;
        ItemsList.ItemsSource = _items;
        _items.CollectionChanged += Items_CollectionChanged;
        UpdateCount();
        Closed += (_, _) => _items.CollectionChanged -= Items_CollectionChanged;
    }

    private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateCount();
    }

    private void UpdateCount()
    {
        CountText.Text = $"{_items.Count}/20";
        EmptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _items.Clear();
    }

    private void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in ItemsList.SelectedItems.Cast<BufferItem>().ToList())
            _items.Remove(item);
    }

    private void ItemsList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(ItemsList);
        // Стандартный ListBox выберет строку после Preview-события мыши.
        _dragArmed = true;
    }

    private void ItemsList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragArmed || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(ItemsList);
        if (Math.Abs(current.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        _dragArmed = false;
        var selected = ItemsList.SelectedItems.Cast<BufferItem>().ToList();
        if (selected.Count == 0) return;

        var data = new DataObject();
        var fileList = new StringCollection();
        foreach (var path in selected.Select(item => item.FilePath).Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path)))
            fileList.Add(path!);
        if (fileList.Count > 0)
            data.SetFileDropList(fileList);

        var text = string.Join(Environment.NewLine + Environment.NewLine,
            selected.Where(item => item.Text is not null).Select(item => item.Text));
        if (!string.IsNullOrEmpty(text))
        {
            data.SetData(DataFormats.UnicodeText, text);
            data.SetData(DataFormats.Text, text);
        }

        if (fileList.Count > 0 || !string.IsNullOrEmpty(text))
            System.Windows.DragDrop.DoDragDrop(ItemsList, data, DragDropEffects.Copy);
    }
}
