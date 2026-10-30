using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI.Text;

namespace Pcl2Swap.Injector.Services;

/// <summary>
/// A deliberately small markdown renderer for release notes: headings, list bullets, quotes,
/// bold, inline code, fenced code and image lines. It produces WinUI elements directly so the
/// injector keeps zero third-party UI dependencies.
/// </summary>
public static class MarkdownRenderer
{
    public static void Fill(Panel host, string? markdown, Func<string, Task<ImageSource?>> imageLoader)
    {
        host.Children.Clear();
        if (string.IsNullOrWhiteSpace(markdown))
        {
            host.Children.Add(new TextBlock
            {
                Text = "（该版本没有 Release Note）",
                Opacity = 0.6,
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var inCode = false;
        var code = new System.Text.StringBuilder();

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();

            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (inCode)
                {
                    host.Children.Add(CodeBlock(code.ToString()));
                    code.Clear();
                    inCode = false;
                }
                else
                {
                    inCode = true;
                }

                continue;
            }

            if (inCode)
            {
                code.AppendLine(raw);
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                host.Children.Add(new Border { Height = 8 });
                continue;
            }

            var imageUrl = TryExtractImageUrl(line);
            if (imageUrl != null)
            {
                host.Children.Add(ImageHost(imageUrl, imageLoader));
                continue;
            }

            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                var level = 0;
                while (level < line.Length && line[level] == '#') level++;
                var text = line[level..].Trim();
                host.Children.Add(new TextBlock
                {
                    Text = StripInline(text),
                    FontSize = level switch { 1 => 22, 2 => 17, _ => 15 },
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 6, 0, 2)
                });
                continue;
            }

            if (line.StartsWith("> ", StringComparison.Ordinal))
            {
                host.Children.Add(new Border
                {
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x55, 0x88, 0x88, 0x88)),
                    Padding = new Thickness(10, 2, 0, 2),
                    Child = Paragraph(StripInline(line[2..]), opacity: 0.75)
                });
                continue;
            }

            if (line.StartsWith("- ", StringComparison.Ordinal) ||
                line.StartsWith("* ", StringComparison.Ordinal))
            {
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var bullet = new TextBlock { Text = "•", Opacity = 0.7, FontSize = 14 };
                Grid.SetColumn(bullet, 0);
                var body = InlineParagraph(line[2..]);
                Grid.SetColumn(body, 1);
                row.Children.Add(bullet);
                row.Children.Add(body);
                host.Children.Add(row);
                continue;
            }

            host.Children.Add(InlineParagraph(line));
        }

        if (inCode && code.Length > 0) host.Children.Add(CodeBlock(code.ToString()));
    }

    private static string? TryExtractImageUrl(string line)
    {
        var start = line.IndexOf("![", StringComparison.Ordinal);
        if (start < 0) return null;
        var close = line.IndexOf("](", start, StringComparison.Ordinal);
        if (close < 0) return null;
        var end = line.IndexOf(')', close);
        if (end < 0) return null;
        var url = line[(close + 2)..end].Trim();
        return url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : null;
    }

    private static Border ImageHost(string url, Func<string, Task<ImageSource?>> imageLoader)
    {
        var image = new Image
        {
            Stretch = Stretch.Uniform,
            MaxHeight = 260,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        var border = new Border
        {
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 4, 0, 4),
            Child = image
        };

        _ = LoadImageAsync(image, url, imageLoader);
        return border;
    }

    private static async Task LoadImageAsync(
        Image image, string url, Func<string, Task<ImageSource?>> imageLoader)
    {
        try
        {
            var source = await imageLoader(url);
            if (source != null) image.Source = source;
        }
        catch
        {
            // note images are decoration; failures are silent
        }
    }

    private static TextBlock Paragraph(string text, double opacity = 1)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Opacity = opacity,
            FontSize = 14
        };
    }

    private static RichTextBlock InlineParagraph(string text)
    {
        var block = new RichTextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14 };
        var paragraph = new Paragraph();
        block.Blocks.Add(paragraph);
        ParseInline(text, paragraph);
        return block;
    }

    private static void ParseInline(string text, Paragraph paragraph)
    {
        var index = 0;
        while (index < text.Length)
        {
            var bold = text.IndexOf("**", index, StringComparison.Ordinal);
            var code = text.IndexOf('`', index);

            if (bold < 0 && code < 0)
            {
                paragraph.Inlines.Add(new Run { Text = text[index..] });
                return;
            }

            var next = bold < 0 ? code : code < 0 ? bold : Math.Min(bold, code);
            if (next > index) paragraph.Inlines.Add(new Run { Text = text[index..next] });

            if (next == bold)
            {
                var end = text.IndexOf("**", bold + 2, StringComparison.Ordinal);
                if (end < 0)
                {
                    paragraph.Inlines.Add(new Run { Text = text[next..] });
                    return;
                }

                paragraph.Inlines.Add(new Run
                {
                    Text = text[(bold + 2)..end],
                    FontWeight = FontWeights.SemiBold
                });
                index = end + 2;
            }
            else
            {
                var end = text.IndexOf('`', code + 1);
                if (end < 0)
                {
                    paragraph.Inlines.Add(new Run { Text = text[next..] });
                    return;
                }

                paragraph.Inlines.Add(new Run
                {
                    Text = text[(code + 1)..end],
                    FontFamily = new FontFamily("Consolas")
                });
                index = end + 1;
            }
        }
    }

    private static string StripInline(string text) =>
        text.Replace("**", string.Empty).Replace("`", string.Empty);

    private static Border CodeBlock(string code)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 4, 0, 4),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x14, 0x80, 0x80, 0x80)),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = new TextBlock
            {
                Text = code.TrimEnd(),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12.5,
                TextWrapping = TextWrapping.NoWrap,
                IsTextSelectionEnabled = true
            }
        };
    }
}
