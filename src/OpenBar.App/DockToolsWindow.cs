using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using OpenBar.Application.Dock;
using OpenBar.Domain.Dock;
using OpenBar.Domain.Layouts;
using OpenBar.Presentation.Dock;
using OpenBar.WidgetRuntime;
using OpenBar.Widgets.Clock;
using Windows.Graphics;
using Windows.UI;

namespace OpenBar.App;

/// <summary>
/// Separate, normally sized window for application discovery and dock
/// appearance. Keeping it out of the tiny dock window avoids clipped dialogs.
/// </summary>
public sealed partial class DockToolsWindow : Window
{
    private readonly DockViewModel _viewModel;
    private readonly IWidgetRuntime _widgets;
    private readonly ListView _applications;
    private readonly TextBox _pathBox;
    private readonly ComboBox _position;
    private readonly Slider _iconSize;
    private readonly Slider _spacing;
    private readonly Slider _magnification;
    private readonly Slider _opacity;
    private readonly Slider _animation;
    private readonly ToggleSwitch _autoHide;
    private readonly TextBlock _status;

    public DockToolsWindow(DockViewModel viewModel, IWidgetRuntime widgets)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(widgets);

        _viewModel = viewModel;
        _widgets = widgets;

        Title = "OpenBar · Ajustes del dock";
        ExtendsContentIntoTitleBar = false;

        _applications = new ListView
        {
            Height = 240,
            SelectionMode = ListViewSelectionMode.Single,
            DisplayMemberPath = nameof(DiscoveredApplication.Name),
            Background = new SolidColorBrush(Color.FromArgb(255, 38, 41, 49)),
            CornerRadius = new CornerRadius(10)
        };

        _position = new ComboBox { MinWidth = 220 };
        AutomationProperties.SetAutomationId(_position, "DockPosition");        foreach (var edge in Enum.GetValues<DockEdge>())
        {
            _position.Items.Add(new ComboBoxItem { Content = EdgeLabel(edge), Tag = edge });
        }

        _position.SelectedIndex = (int)_viewModel.Position;

        _iconSize = CreateSlider("Tamaño de icono", 24, 96, 4, _viewModel.IconSize);
        _spacing = CreateSlider("Espaciado", 0, 40, 1, _viewModel.Spacing);
        _magnification = CreateSlider("Ampliación al pasar el cursor", 1, 1.5, 0.02, _viewModel.Magnification);
        _opacity = CreateSlider("Intensidad del material", 0.05, 1, 0.05, _viewModel.MaterialOpacity);
        _animation = CreateSlider("Duración de animaciones (ms)", 0, 1000, 20, _viewModel.AnimationDurationMs);
        _autoHide = new ToggleSwitch { Header = "Ocultar automáticamente", IsOn = _viewModel.AutoHide };
        _status = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)),
            TextWrapping = TextWrapping.Wrap
        };

        var addButton = new Button { Content = "Añadir seleccionada" };
        addButton.Click += async (_, _) => await AddSelectedAsync();
        AutomationProperties.SetAutomationId(addButton, "AddSelectedApplication");

        _pathBox = new TextBox
        {
            PlaceholderText = @"C:\Windows\System32\notepad.exe",
            MinWidth = 420
        };

        var addPathButton = new Button { Content = "Añadir ruta" };
        addPathButton.Click += async (_, _) => await AddPathAsync();
        AutomationProperties.SetAutomationId(addPathButton, "AddApplicationPath");
        AutomationProperties.SetAutomationId(_pathBox, "ApplicationPath");

        var saveButton = new Button { Content = "Guardar configuración" };
        saveButton.Click += async (_, _) => await SaveSettingsAsync();
        AutomationProperties.SetAutomationId(saveButton, "SaveDockSettings");

        var clockButton = new Button { Content = "Añadir reloj de prueba" };
        clockButton.Click += async (_, _) => await AddClockAsync();
        AutomationProperties.SetAutomationId(clockButton, "AddClockWidget");

        var closeButton = new Button { Content = "Cerrar" };
        closeButton.Click += (_, _) => Close();
        AutomationProperties.SetAutomationId(closeButton, "CloseDockTools");

        var panel = new StackPanel
        {
            Spacing = 12,
            Width = 580,
            HorizontalAlignment = HorizontalAlignment.Left
        };

        panel.Children.Add(Header("Aplicaciones"));
        panel.Children.Add(new TextBlock
        {
            Text = "Aplicaciones reales descubiertas en el menú Inicio. No se inventan rutas.",
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(_applications);
        panel.Children.Add(addButton);
        panel.Children.Add(_pathBox);
        panel.Children.Add(addPathButton);
        panel.Children.Add(Header("Apariencia"));
        panel.Children.Add(_position);
        panel.Children.Add(_iconSize);
        panel.Children.Add(_spacing);
        panel.Children.Add(_magnification);
        panel.Children.Add(_opacity);
        panel.Children.Add(_animation);
        panel.Children.Add(_autoHide);
        panel.Children.Add(saveButton);
        panel.Children.Add(Header("Widgets"));
        panel.Children.Add(clockButton);
        panel.Children.Add(closeButton);
        panel.Children.Add(_status);

        var scroll = new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        Content = new Grid
        {
            Padding = new Thickness(24),
            Background = new SolidColorBrush(Color.FromArgb(255, 25, 27, 33)),
            Children = { scroll }
        };

        AppWindow.Resize(new SizeInt32(660, 780));

        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Move(new PointInt32(
            workArea.X + (workArea.Width - 660) / 2,
            workArea.Y + (workArea.Height - 780) / 2));

        _ = LoadApplicationsAsync();
    }

    private async Task LoadApplicationsAsync()
    {
        try
        {
            _status.Text = "Descubriendo aplicaciones…";
            var applications = await _viewModel.DiscoverApplicationsAsync();
            _applications.ItemsSource = applications;
            _status.Text = $"{applications.Count} aplicaciones encontradas.";
        }
        catch (Exception ex)
        {
            _status.Text = $"No se pudieron descubrir aplicaciones: {ex.Message}";
        }
    }

    private async Task AddSelectedAsync()
    {
        if (_applications.SelectedItem is not DiscoveredApplication application)
        {
            _status.Text = "Selecciona una aplicación de la lista.";
            return;
        }

        try
        {
            await _viewModel.AddPinnedAsync(application);
            _status.Text = $"Añadida: {application.Name}";
        }
        catch (Exception ex)
        {
            _status.Text = $"No se pudo añadir: {ex.Message}";
        }
    }

    private async Task AddPathAsync()
    {
        var path = _pathBox.Text.Trim().Trim('"');

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            _status.Text = "La ruta indicada no existe.";
            return;
        }

        var name = Path.GetFileNameWithoutExtension(path);
        try
        {
            await _viewModel.AddPinnedAsync(new DiscoveredApplication(
                name,
                path,
                PinnedTargetKind.Executable,
                IconPath: path));
            _status.Text = $"Añadida: {name}";
        }
        catch (Exception ex)
        {
            _status.Text = $"No se pudo añadir: {ex.Message}";
        }
    }

    private async Task SaveSettingsAsync()
    {
        var edge = _position.SelectedItem is ComboBoxItem { Tag: DockEdge selected }
            ? selected
            : _viewModel.Position;

        try
        {
            await _viewModel.SaveSettingsAsync(
                edge,
                _iconSize.Value,
                _spacing.Value,
                _magnification.Value,
                _opacity.Value,
                (int)Math.Round(_animation.Value),
                _autoHide.IsOn);

            _status.Text = "Configuración guardada.";
        }
        catch (Exception ex)
        {
            _status.Text = $"No se pudo guardar: {ex.Message}";
        }
    }

    private async Task AddClockAsync()
    {
        try
        {
            var info = await _widgets.CreateAsync(ClockWidgetDefinition.WidgetId);
            _status.Text = $"Reloj creado: {info.InstanceName}";
        }
        catch (Exception ex)
        {
            _status.Text = $"No se pudo crear el reloj: {ex.Message}";
        }
    }

    private static TextBlock Header(string text)
        => new()
        {
            Text = text,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            Margin = new Thickness(0, 8, 0, 0)
        };

    private static Slider CreateSlider(string header, double minimum, double maximum, double step, double value)
        => new()
        {
            Header = header,
            Minimum = minimum,
            Maximum = maximum,
            StepFrequency = step,
            Value = value,
            MinWidth = 380
        };

    private static string EdgeLabel(DockEdge edge)
        => edge switch
        {
            DockEdge.Top => "Arriba",
            DockEdge.Bottom => "Abajo",
            DockEdge.Left => "Izquierda",
            DockEdge.Right => "Derecha",
            _ => edge.ToString()
        };
}
