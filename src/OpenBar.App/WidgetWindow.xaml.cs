using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using OpenBar.Application.Widgets;
using Windows.Graphics;
using WinRT.Interop;

namespace OpenBar.App;

/// <summary>
/// One independent WinUI 3 window per widget instance. The view is created by
/// the widget on the UI thread; the window only hosts it and reports user
/// close requests back to the runtime.
/// </summary>
public sealed partial class WidgetWindow : Window
{
    private readonly Guid _instanceId;
    private readonly Action<Guid> _closeRequested;
    private bool _programmaticClose;

    public WidgetWindow(WidgetWindowRequest request, Action<Guid> closeRequested)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(closeRequested);

        InitializeComponent();

        _instanceId = request.InstanceId;
        _closeRequested = closeRequested;
        Title = request.Title;

        var view = request.Widget.CreateView() as UIElement
            ?? throw new InvalidOperationException(
                $"Widget '{request.WidgetId}' did not return a WinUI UIElement view.");

        Root.Children.Add(view);

        var dpi = GetDpiForWindow(WindowNative.GetWindowHandle(this));
        var scale = dpi > 0 ? dpi / 96.0 : 1.0;

        AppWindow.Resize(new SizeInt32(
            Math.Max(1, (int)Math.Round(request.Width * scale)),
            Math.Max(1, (int)Math.Round(request.Height * scale))));

        if (request.PositionX is double positionX && request.PositionY is double positionY)
        {
            AppWindow.Move(new PointInt32(
                (int)Math.Round(positionX * scale),
                (int)Math.Round(positionY * scale)));
        }

        AppWindow.Closing += OnClosing;
    }

    public void CloseProgrammatically()
    {
        _programmaticClose = true;
        Close();
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_programmaticClose)
        {
            return;
        }

        // The runtime stops the widget; the window is already closing, so the
        // service only removes it from its map and raises the notification.
        _closeRequested(_instanceId);
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
}
