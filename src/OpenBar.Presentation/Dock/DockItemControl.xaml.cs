using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;

namespace OpenBar.Presentation.Dock;

/// <summary>
/// One dock tile. Keeps layout size stable while a scale transform magnifies the
/// icon on hover, so neighbouring items do not jump.
/// </summary>
public sealed partial class DockItemControl : UserControl
{
    public DockItemControl()
    {
        InitializeComponent();
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        var item = DataContext as DockItemViewModel;
        var magnification = item?.Magnification ?? 1.12;
        Animate(magnification, 1, item?.AnimationDurationMs ?? 180);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        var item = DataContext as DockItemViewModel;
        Animate(1, 0, item?.AnimationDurationMs ?? 180);
    }

    private void Animate(double scale, double hoverOpacity, int durationMs)
    {
        if (durationMs <= 0)
        {
            HoverScale.ScaleX = scale;
            HoverScale.ScaleY = scale;
            HoverBackground.Opacity = hoverOpacity;
            return;
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(durationMs));
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var storyboard = new Storyboard();

        AddAnimation(storyboard, HoverScale, "ScaleX", scale, duration, easing);
        AddAnimation(storyboard, HoverScale, "ScaleY", scale, duration, easing);
        AddAnimation(storyboard, HoverBackground, "Opacity", hoverOpacity, duration, easing);

        storyboard.Begin();
    }

    private static void AddAnimation(
        Storyboard storyboard,
        DependencyObject target,
        string property,
        double value,
        Duration duration,
        EasingFunctionBase easing)
    {
        var animation = new DoubleAnimation
        {
            To = value,
            Duration = duration,
            EasingFunction = easing
        };

        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }
}
