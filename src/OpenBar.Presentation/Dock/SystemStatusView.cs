using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenBar.SystemBridge;

namespace OpenBar.Presentation.Dock;

/// <summary>
/// Compact CPU/RAM summary shown inside the dock. It is a read-only surface;
/// the full CPU and memory widgets remain independent windows.
/// </summary>
public sealed class SystemStatusView : UserControl
{
    private readonly TextBlock _cpu;
    private readonly TextBlock _memory;

    public SystemStatusView()
    {
        _cpu = new TextBlock { Text = "C --", FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center };
        _memory = new TextBlock { Text = "M --", FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center };

        var panel = new StackPanel
        {
            Spacing = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        panel.Children.Add(_cpu);
        panel.Children.Add(_memory);

        var background = TryResource("CardBackgroundFillColorDefaultBrush");
        var border = TryResource("CardStrokeColorDefaultBrush");

        Content = new Border
        {
            Background = background,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(4, 3, 4, 3),
            Child = panel
        };

        ToolTipService.SetToolTip(this, "Sistema: CPU -- · RAM --");
    }

    public void SetSnapshot(SystemMetricsSnapshot snapshot)
    {
        var cpu = snapshot.CpuAvailable && snapshot.CpuUsagePercent is { } cpuPercent
            ? $"{cpuPercent:0}%"
            : "--";
        var memory = snapshot.MemoryAvailable && snapshot.MemoryUsagePercent is { } memoryPercent
            ? $"{memoryPercent:0}%"
            : "--";

        _cpu.Text = $"C {cpu}";
        _memory.Text = $"M {memory}";
        ToolTipService.SetToolTip(this, $"Sistema: CPU {cpu} · RAM {memory}");
    }

    private static Brush? TryResource(string key)
        => Microsoft.UI.Xaml.Application.Current?.Resources.TryGetValue(key, out var value) == true
            ? value as Brush
            : null;
}
