using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenBar.Widgets.Abstractions;
using Windows.UI;

namespace OpenBar.Widgets.Clock;

/// <summary>
/// Native WinUI 3 view for the clock. Kept code-only so the widget module does
/// not depend on XAML resources from the application project.
/// </summary>
public sealed class ClockWidgetView : UserControl, IWidgetSurfaceView
{
    private readonly TextBlock _timeText;
    private readonly TextBlock _dateText;
    private readonly Border _border;
    private readonly StackPanel _panel;

    public ClockWidgetView()
    {
        _timeText = new TextBlock
        {
            FontSize = 38,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            Text = "--:--"
        };

        _dateText = new TextBlock
        {
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        _panel = new StackPanel
        {
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        _panel.Children.Add(_timeText);
        _panel.Children.Add(_dateText);

        _border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(215, 18, 20, 26)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(24, 14, 24, 14),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = _panel
        };

        Content = _border;
    }

    public void SetSurfaceMode(bool embedded)
    {
        _timeText.FontSize = embedded ? 22 : 38;
        _dateText.FontSize = embedded ? 9 : 14;
        _panel.Spacing = embedded ? 0 : 2;
        _border.Padding = embedded ? new Thickness(2) : new Thickness(24, 14, 24, 14);
        _border.CornerRadius = new CornerRadius(embedded ? 8 : 18);
        _border.BorderThickness = new Thickness(embedded ? 0 : 1);
        _border.Background = embedded ? null : new SolidColorBrush(Color.FromArgb(215, 18, 20, 26));

        var primary = embedded
            ? Microsoft.UI.Xaml.Application.Current?.Resources.TryGetValue("TextFillColorPrimaryBrush", out var brush) == true
                ? brush as Brush
                : null
            : null;

        _timeText.Foreground = primary ?? new SolidColorBrush(Colors.White);
        _dateText.Foreground = primary ?? new SolidColorBrush(Color.FromArgb(190, 255, 255, 255));
        _dateText.Opacity = embedded ? 0.85 : 1;
    }

    public void SetTime(string time, string date)
    {
        _timeText.Text = time;
        _dateText.Text = date;
        _dateText.Visibility = string.IsNullOrEmpty(date) ? Visibility.Collapsed : Visibility.Visible;
    }
}
