using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenBar.Widgets.Abstractions;

namespace OpenBar.Widgets.System;

/// <summary>
/// Metric card shared by the system widgets. It keeps the roomy card layout in
/// an independent window and switches to a compact, borderless layout when the
/// dock gives it a narrow embedded surface.
/// </summary>
public sealed class MetricWidgetView : UserControl, IWidgetSurfaceView
{
    private readonly Border _container;
    private readonly TextBlock _title;
    private readonly TextBlock _value;
    private readonly TextBlock _detail;
    private readonly ProgressBar _progress;
    private readonly TextBlock _status;
    private bool _compact;
    private bool _embedded;

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
            FontWeight = FontWeights.SemiBold
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

        _container = new Border
        {
            Background = TryResource("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = TryResource("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(18, 14, 18, 14),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = panel
        };

        Content = _container;
        SizeChanged += (_, _) => ApplyResponsiveLayout();
    }

    public void SetData(
        string value,
        string detail,
        double percent,
        bool available,
        string status,
        bool showProgress = true)
    {
        _value.Text = value;
        _detail.Text = detail;
        _progress.Value = Math.Clamp(percent, 0, 100);
        _progress.Opacity = available ? 1 : 0.35;
        _progress.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
        _status.Text = status;
        ApplyTextVisibility();
    }

    public void SetSurfaceMode(bool embedded)
    {
        _embedded = embedded;
        ApplyResponsiveLayout(force: true);
    }

    private void ApplyResponsiveLayout(bool force = false)
    {
        var compact = _embedded
            || (ActualHeight > 0 && (ActualHeight <= 110 || ActualWidth <= 210));

        if (!force && compact == _compact)
        {
            return;
        }

        _compact = compact;
        _container.Padding = compact ? new Thickness(0) : new Thickness(18, 14, 18, 14);
        _container.CornerRadius = new CornerRadius(compact ? 8 : 18);
        _container.BorderThickness = new Thickness(compact ? 0 : 1);
        _container.Background = compact ? null : TryResource("CardBackgroundFillColorDefaultBrush");

        _title.FontSize = compact ? 8.5 : 12;
        _title.Opacity = compact ? 0.82 : 0.72;
        _value.FontSize = compact ? 15 : 30;
        _detail.FontSize = compact ? 8.5 : 12;
        _detail.TextWrapping = compact ? TextWrapping.NoWrap : TextWrapping.Wrap;
        _detail.TextTrimming = compact ? TextTrimming.CharacterEllipsis : TextTrimming.None;
        _detail.MaxLines = compact ? 1 : 0;
        _status.FontSize = compact ? 8.5 : 11;
        _status.TextWrapping = compact ? TextWrapping.NoWrap : TextWrapping.Wrap;
        _status.TextTrimming = compact ? TextTrimming.CharacterEllipsis : TextTrimming.None;
        _status.MaxLines = compact ? 1 : 0;
        _progress.Height = compact ? 3 : 5;

        if (_container.Child is StackPanel panel)
        {
            panel.Spacing = compact ? 0 : 3;
        }

        ApplyTextVisibility();
    }

    private void ApplyTextVisibility()
    {
        var hasStatus = !string.IsNullOrWhiteSpace(_status.Text);
        _status.Visibility = hasStatus ? Visibility.Visible : Visibility.Collapsed;
        _detail.Visibility = _compact && hasStatus ? Visibility.Collapsed : Visibility.Visible;
    }

    private static Brush? TryResource(string key)
        => Microsoft.UI.Xaml.Application.Current?.Resources.TryGetValue(key, out var value) == true
            ? value as Brush
            : null;
}
