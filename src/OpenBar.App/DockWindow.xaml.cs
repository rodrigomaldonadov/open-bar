using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using OpenBar.Presentation.Dock;
using Windows.UI;

namespace OpenBar.App;

/// <summary>
/// Borderless top-level dock window. All visual content and interactions live
/// in <see cref="DockView"/>; this class only hosts it and provides a fallback
/// background when the Acrylic system backdrop is unavailable.
/// </summary>
public sealed partial class DockWindow : Window
{
    public DockWindow(DockView dockView)
    {
        ArgumentNullException.ThrowIfNull(dockView);

        InitializeComponent();

        DockView = dockView;
        Title = "OpenBar";
        ExtendsContentIntoTitleBar = true;
        Root.Children.Add(dockView);

        try
        {
            SystemBackdrop = new DesktopAcrylicBackdrop();
        }
        catch (Exception)
        {
            // Desktop Acrylic can be unavailable in remote/unsupported sessions.
            Root.Background = new SolidColorBrush(Color.FromArgb(238, 18, 20, 26));
        }
    }

    public DockView DockView { get; }
}
