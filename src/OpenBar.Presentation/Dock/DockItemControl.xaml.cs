using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace OpenBar.Presentation.Dock;

/// <summary>
/// One dock tile. The layout cell stays constant; a render transform provides
/// proximity magnification driven by the dock view so neighbours react
/// proportionally to the pointer position.
/// </summary>
public sealed partial class DockItemControl : UserControl
{
    private double _targetScale = 1;

    public DockItemControl()
    {
        InitializeComponent();
    }

    public void SetMagnification(double scale, int durationMs)
    {
        if (Math.Abs(scale - _targetScale) < 0.001)
        {
            return;
        }

        _targetScale = scale;
        Animate(scale, scale > 1.02 ? 1 : 0, durationMs <= 0 ? 0 : Math.Clamp(durationMs, 40, 160));
    }

    public void ResetMagnification(int durationMs)
    {
        _targetScale = 1;
        Animate(1, 0, durationMs <= 0 ? 0 : Math.Clamp(durationMs, 40, 160));
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
