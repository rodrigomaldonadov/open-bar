using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace OpenBar.Widgets.Clock;

/// <summary>
/// Native WinUI 3 view for the clock. Kept code-only so the widget module does
/// not depend on XAML resources from the application project.
/// </summary>
public sealed class ClockWidgetView : UserControl
{
    private readonly TextBlock _timeText;
    private readonly TextBlock _dateText;

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

        var panel = new StackPanel
        {
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        panel.Children.Add(_timeText);
        panel.Children.Add(_dateText);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(215, 18, 20, 26)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(24, 14, 24, 14),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = panel
        };
    }

    public void SetTime(string time, string date)
    {
        _timeText.Text = time;
        _dateText.Text = date;
        _dateText.Visibility = string.IsNullOrEmpty(date) ? Visibility.Collapsed : Visibility.Visible;
    }
}
