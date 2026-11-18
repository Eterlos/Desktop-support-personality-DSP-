using System.Windows.Media;

namespace dcp_project;

public sealed class BufferItem
{
    public string Kind { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string PreviewText { get; init; } = string.Empty;
    public string? Text { get; init; }
    public string? FilePath { get; init; }
    public ImageSource? PreviewImage { get; init; }
}
