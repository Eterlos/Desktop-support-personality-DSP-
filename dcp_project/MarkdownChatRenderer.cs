using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MdBlock = Markdig.Syntax.Block;
using WpfBlock = System.Windows.Documents.Block;
using WpfInline = System.Windows.Documents.Inline;
using WpfList = System.Windows.Documents.List;

namespace dcp_project;

internal static class MarkdownChatRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml().Build();

    internal static void Append(FlowDocument document, string author, string text, bool markdown)
    {
        var section = new Section { Margin = new Thickness(0, 0, 0, 16) };
        if (!string.IsNullOrEmpty(author))
            section.Blocks.Add(new Paragraph(new Bold(new Run(author + ":"))) { Margin = new Thickness(0, 0, 0, 4) });
        if (markdown)
        {
            foreach (var block in Markdown.Parse(text, Pipeline)) section.Blocks.Add(RenderBlock(block, text));
        }
        else section.Blocks.Add(new Paragraph(new Run(text)) { Margin = new Thickness(0) });
        document.Blocks.Add(section);
    }

    private static WpfBlock RenderBlock(MdBlock block, string source)
    {
        switch (block)
        {
            case HeadingBlock heading:
                var title = Paragraph(heading.Inline, source);
                title.FontWeight = FontWeights.Bold;
                title.FontSize = heading.Level switch { 1 => 24, 2 => 21, 3 => 18, _ => 16 };
                return title;
            case ParagraphBlock paragraph:
                return Paragraph(paragraph.Inline, source);
            case CodeBlock code:
                return new Paragraph(new Run(code.Lines.ToString()))
                {
                    FontFamily = new FontFamily("Consolas"), Background = Brushes.WhiteSmoke,
                    Padding = new Thickness(8), Margin = new Thickness(0, 4, 0, 8)
                };
            case ListBlock list:
                var result = new WpfList { MarkerStyle = list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                    Padding = new Thickness(24, 0, 0, 0), Margin = new Thickness(0, 0, 0, 8) };
                if (list.IsOrdered && int.TryParse(list.OrderedStart, out var start) && start > 0) result.StartIndex = start;
                foreach (var child in list)
                {
                    var item = new ListItem();
                    if (child is ContainerBlock children)
                        foreach (var nested in children) item.Blocks.Add(RenderBlock(nested, source));
                    result.ListItems.Add(item);
                }
                return result;
            case QuoteBlock quote:
                var section = new Section { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(8, 0, 0, 0), Margin = new Thickness(0, 4, 0, 8) };
                foreach (var child in quote) section.Blocks.Add(RenderBlock(child, source));
                return section;
            case ThematicBreakBlock:
                return new Paragraph(new Run("────────────")) { Foreground = Brushes.Gray };
            default:
                return new Paragraph(new Run(SourceText(block, source)));
        }
    }

    private static Paragraph Paragraph(ContainerInline? inline, string source)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
        AddChildren(paragraph.Inlines, inline, source);
        return paragraph;
    }

    private static void AddChildren(InlineCollection output, ContainerInline? container, string source)
    {
        for (var child = container?.FirstChild; child is not null; child = child.NextSibling)
            output.Add(RenderInline(child, source));
    }

    private static WpfInline RenderInline(Markdig.Syntax.Inlines.Inline inline, string source)
    {
        switch (inline)
        {
            case LiteralInline literal:
                return new Run(literal.Content.ToString());
            case CodeInline code:
                return new Run(code.Content) { FontFamily = new FontFamily("Consolas"), Background = Brushes.WhiteSmoke };
            case LineBreakInline:
                return new LineBreak();
            case EmphasisInline emphasis:
                Span styled = emphasis.DelimiterCount >= 2 ? new Bold() : new Italic();
                AddChildren(styled.Inlines, emphasis, source);
                return styled;
            case LinkInline link when !link.IsImage:
                var span = CreateLink(link.Url);
                AddChildren(span.Inlines, link, source);
                return span;
            case AutolinkInline link:
                var autoLink = CreateLink(link.Url);
                autoLink.Inlines.Add(new Run(link.Url));
                return autoLink;
            case LinkInline image when image.IsImage:
                // Do not download images or load local files from a model response.
                return new Run(SourceText(image, source));
            case ContainerInline children:
                var group = new Span();
                AddChildren(group.Inlines, children, source);
                return group;
            default:
                return new Run(SourceText(inline, source));
        }
    }

    private static Span CreateLink(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return new Span();
        var link = new Hyperlink { NavigateUri = uri, ToolTip = uri.AbsoluteUri };
        link.RequestNavigate += (_, args) =>
        {
            args.Handled = true;
            try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
            catch (Exception) { MessageBox.Show("Не удалось открыть ссылку в браузере.", "Чат"); }
        };
        return link;
    }

    private static string SourceText(MarkdownObject item, string source)
    {
        var start = item.Span.Start;
        var end = item.Span.End;
        return start >= 0 && end >= start && end < source.Length ? source[start..(end + 1)] : "";
    }
}
