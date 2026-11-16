using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace OpenBar.Widgets.System;

/// <summary>Compact metric card shared by the CPU and memory widgets.</summary>
public sealed class MetricWidgetView : UserControl
{
    private readonly TextBlock _title;
    private readonly TextBlock _value;
    private readonly TextBlock _detail;
    private readonly ProgressBar _progress;
    private readonly TextBlock _status;

    public MetricWidgetView(string title)
    {
        _title = new TextBlock
        {
            Text = title,
            FontSize = 12,
            Opacity = 0.72
        };

        _value = new TextBlock
        {
            Text = "—",
            FontSize = 30,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };

        _detail = new TextBlock
        {
            FontSize = 12,
            Opacity = 0.8,
            TextWrapping = TextWrapping.Wrap
        };

        _progress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 5,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Value = 0
        };

        _status = new TextBlock
        {
            Text = "Iniciando…",
            FontSize = 11,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap
        };

        var panel = new StackPanel
        {
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center
        };

        panel.Children.Add(_title);
        panel.Children.Add(_value);
        panel.Children.Add(_detail);
        panel.Children.Add(_progress);
        panel.Children.Add(_status);

        var background = TryResource("CardBackgroundFillColorDefaultBrush");
        var border = TryResource("CardStrokeColorDefaultBrush");

        Content = new Border
        {
            Background = background,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(18, 14, 18, 14),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = panel
        };
    }

    public void SetData(string value, string detail, double percent, bool available, string status)
    {
        _value.Text = value;
        _detail.Text = detail;
        _progress.Value = Math.Clamp(percent, 0, 100);
        _progress.Opacity = available ? 1 : 0.35;
        _status.Text = status;
        _status.Visibility = string.IsNullOrWhiteSpace(status) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static Brush? TryResource(string key)
        => Microsoft.UI.Xaml.Application.Current?.Resources.TryGetValue(key, out var value) == true
            ? value as Brush
            : null;
}
