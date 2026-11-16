using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace OpenBar.Presentation.Widgets;

/// <summary>
/// Stable wrapper for one embedded widget. It owns the widget-created view for
/// the whole surface lifetime, so reordering the dock never reparents the view
/// through a recycled ItemsControl container.
/// </summary>
public sealed class EmbeddedWidgetSurfaceView : UserControl
{
    private readonly EmbeddedWidgetSurfaceViewModel _model;
    private readonly Border _border;

    public EmbeddedWidgetSurfaceView(EmbeddedWidgetSurfaceViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        _model = model;
        DataContext = model;

        _border = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(5),
            Child = model.Content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        Content = _border;
        ToolTipService.SetToolTip(this, model.Title);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, model.Title);
        ActualThemeChanged += (_, _) => ApplyTheme();
        ApplyTheme();
    }

    public void ApplyOrientation(bool vertical, double iconSize, double spacing, bool isLast)
    {
        var verticalThickness = Math.Max(iconSize, 96);
        var horizontalThickness = Math.Max(iconSize, 72);
        Width = vertical ? verticalThickness : _model.CompactWidth;
        Height = vertical ? _model.CompactHeight : horizontalThickness;
        Margin = vertical
            ? new Thickness(0, 0, 0, isLast ? 0 : spacing)
            : new Thickness(0, 0, 0, isLast ? 0 : spacing);
    }

    public void SetVisible(bool visible)
        => Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private void ApplyTheme()
    {
        _border.Background = TryResource("DockActionBackgroundBrush")
            ?? new SolidColorBrush(ActualTheme == ElementTheme.Light
                ? Windows.UI.Color.FromArgb(12, 0, 0, 0)
                : Windows.UI.Color.FromArgb(18, 255, 255, 255));

        _border.BorderBrush = TryResource("DockBorderBrush")
            ?? new SolidColorBrush(ActualTheme == ElementTheme.Light
                ? Windows.UI.Color.FromArgb(30, 0, 0, 0)
                : Windows.UI.Color.FromArgb(45, 255, 255, 255));
    }

    private static Brush? TryResource(string key)
        => Microsoft.UI.Xaml.Application.Current?.Resources.TryGetValue(key, out var value) == true
            ? value as Brush
            : null;
}



