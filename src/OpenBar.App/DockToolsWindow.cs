using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using OpenBar.Application.Dock;
using OpenBar.Application.Persistence;
using OpenBar.Domain.Dock;
using OpenBar.Domain.Layouts;
using OpenBar.Domain.Widgets;
using OpenBar.Presentation.Dock;
using OpenBar.SystemBridge;
using OpenBar.WidgetRuntime;
using OpenBar.Widgets.Clock;
using OpenBar.Widgets.System;
using System.Text.Json;
using Windows.Graphics;

namespace OpenBar.App;

/// <summary>
/// Separate, normally sized window for application discovery and dock
/// appearance. Keeping it out of the tiny dock window avoids clipped dialogs.
/// </summary>
public sealed partial class DockToolsWindow : Window
{
    private readonly DockViewModel _viewModel;
    private readonly IWidgetRuntime _widgets;
    private readonly IWidgetInstanceService _widgetStore;
    private readonly INetworkMetrics _network;
    private readonly IStorageMetrics _storage;
    private readonly ListView _applications;
    private readonly TextBox _pathBox;
    private readonly ComboBox _position;
    private readonly ComboBox _theme;
    private readonly ComboBox _monitor;
    private readonly Slider _iconSize;
    private readonly Slider _spacing;
    private readonly Slider _magnification;
    private readonly Slider _opacity;
    private readonly Slider _cornerRadius;
    private readonly Slider _animation;
    private readonly Slider _showDelay;
    private readonly Slider _hideDelay;
    private readonly ToggleSwitch _autoHide;
    private readonly ToggleSwitch _reduceAnimations;
    private readonly ToggleSwitch _hideInFullscreen;
    private readonly ComboBox _widgetInstances;
    private readonly ComboBox _widgetMode;
    private readonly ToggleSwitch _widgetEnabled;
    private readonly NumberBox _widgetInterval;
    private readonly ComboBox _widgetUnit;
    private readonly ComboBox _widgetVolume;
    private readonly ComboBox _widgetAdapter;
    private readonly TextBlock _status;
    private int _selectedOrder;

    public DockToolsWindow(
        DockViewModel viewModel,
        IWidgetRuntime widgets,
        IWidgetInstanceService widgetStore,
        INetworkMetrics network,
        IStorageMetrics storage)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(widgets);
        ArgumentNullException.ThrowIfNull(widgetStore);
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(storage);

        _viewModel = viewModel;
        _widgets = widgets;
        _widgetStore = widgetStore;
        _network = network;
        _storage = storage;

        Title = "OpenBar · Ajustes del dock";
        ExtendsContentIntoTitleBar = false;

        _applications = new ListView
        {
            Height = 220,
            SelectionMode = ListViewSelectionMode.Single,
            DisplayMemberPath = nameof(DiscoveredApplication.Name),
            CornerRadius = new CornerRadius(10)
        };
        AutomationProperties.SetAutomationId(_applications, "ApplicationList");

        _position = new ComboBox { MinWidth = 220 };
        AutomationProperties.SetAutomationId(_position, "DockPosition");
        foreach (var edge in Enum.GetValues<DockEdge>())
        {
            _position.Items.Add(new ComboBoxItem { Content = EdgeLabel(edge), Tag = edge });
        }

        _position.SelectedIndex = (int)_viewModel.Position;

        _theme = new ComboBox { MinWidth = 220 };
        AutomationProperties.SetAutomationId(_theme, "DockTheme");
        _theme.Items.Add(new ComboBoxItem { Content = "Automático (Windows)", Tag = DockTheme.System });
        _theme.Items.Add(new ComboBoxItem { Content = "Claro", Tag = DockTheme.Light });
        _theme.Items.Add(new ComboBoxItem { Content = "Oscuro", Tag = DockTheme.Dark });
        _theme.SelectedIndex = (int)_viewModel.Theme;

        _monitor = new ComboBox { MinWidth = 260 };
        AutomationProperties.SetAutomationId(_monitor, "DockMonitor");
        LoadMonitors();

        _iconSize = CreateSlider("Tamaño de icono", 24, 96, 4, _viewModel.IconSize);
        _spacing = CreateSlider("Espaciado", 0, 40, 1, _viewModel.Spacing);
        _magnification = CreateSlider("Ampliación máxima al pasar el cursor", 1, 1.5, 0.02, _viewModel.Magnification);
        _opacity = CreateSlider("Intensidad del material", 0.05, 1, 0.05, _viewModel.MaterialOpacity);
        _cornerRadius = CreateSlider("Radio de las esquinas", 0, 48, 1, _viewModel.CornerRadius);
        _animation = CreateSlider("Duración de animaciones (ms)", 0, 1000, 20, _viewModel.AnimationDurationMs);
        _showDelay = CreateSlider("Retardo de aparición del auto-hide (ms)", 0, 2000, 50, _viewModel.AutoHideShowDelayMs);
        _hideDelay = CreateSlider("Retardo de ocultación del auto-hide (ms)", 0, 5000, 50, _viewModel.AutoHideHideDelayMs);

        _autoHide = new ToggleSwitch { Header = "Ocultar automáticamente", IsOn = _viewModel.AutoHide };
        _reduceAnimations = new ToggleSwitch { Header = "Reducir animaciones", IsOn = _viewModel.ReduceAnimations };
        _hideInFullscreen = new ToggleSwitch
        {
            Header = "Ocultar durante aplicaciones a pantalla completa",
            IsOn = _viewModel.HideInFullscreen
        };

        _widgetInstances = new ComboBox { Header = "Instancia del widget", MinWidth = 300 };
        AutomationProperties.SetAutomationId(_widgetInstances, "SystemWidgetInstance");
        _widgetInstances.SelectionChanged += (_, _) => LoadSelectedWidgetSettings();

        _widgetMode = new ComboBox { Header = "Presentación", MinWidth = 260 };
        AutomationProperties.SetAutomationId(_widgetMode, "SystemWidgetMode");
        _widgetMode.Items.Add(new ComboBoxItem
        {
            Content = "Integrado en el dock",
            Tag = WidgetPresentationMode.DockEmbedded
        });
        _widgetMode.Items.Add(new ComboBoxItem
        {
            Content = "Ventana independiente",
            Tag = WidgetPresentationMode.FloatingWindow
        });

        _widgetEnabled = new ToggleSwitch { Header = "Activo" };
        AutomationProperties.SetAutomationId(_widgetEnabled, "SystemWidgetEnabled");

        _widgetInterval = new NumberBox
        {
            Header = "Intervalo de actualización (segundos)",
            Minimum = 1,
            Maximum = 10,
            SmallChange = 1,
            LargeChange = 1,
            Value = 1,
            MinWidth = 220
        };
        AutomationProperties.SetAutomationId(_widgetInterval, "SystemWidgetInterval");

        _widgetUnit = new ComboBox { Header = "Unidad de memoria", MinWidth = 220 };
        AutomationProperties.SetAutomationId(_widgetUnit, "SystemWidgetUnit");
        _widgetUnit.Items.Add(new ComboBoxItem { Content = "Automática", Tag = "auto" });
        _widgetUnit.Items.Add(new ComboBoxItem { Content = "MB", Tag = "mb" });
        _widgetUnit.Items.Add(new ComboBoxItem { Content = "GB", Tag = "gb" });
        _widgetUnit.SelectedIndex = 0;

        _widgetVolume = new ComboBox { Header = "Volumen", MinWidth = 260 };
        AutomationProperties.SetAutomationId(_widgetVolume, "SystemWidgetVolume");
        LoadVolumes();

        _widgetAdapter = new ComboBox { Header = "Adaptador de red", MinWidth = 300 };
        AutomationProperties.SetAutomationId(_widgetAdapter, "SystemWidgetAdapter");
        LoadAdapters();

        _status = new TextBlock { TextWrapping = TextWrapping.Wrap };

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

        var cpuButton = new Button { Content = "Añadir CPU" };
        cpuButton.Click += async (_, _) => await AddSystemWidgetAsync(CpuWidgetDefinition.WidgetId);
        AutomationProperties.SetAutomationId(cpuButton, "AddCpuWidget");

        var memoryButton = new Button { Content = "Añadir memoria" };
        memoryButton.Click += async (_, _) => await AddSystemWidgetAsync(MemoryWidgetDefinition.WidgetId);
        AutomationProperties.SetAutomationId(memoryButton, "AddMemoryWidget");

        var batteryButton = new Button { Content = "Añadir batería" };
        batteryButton.Click += async (_, _) => await AddSystemWidgetAsync(BatteryWidgetDefinition.WidgetId);
        AutomationProperties.SetAutomationId(batteryButton, "AddBatteryWidget");

        var networkButton = new Button { Content = "Añadir red" };
        networkButton.Click += async (_, _) => await AddSystemWidgetAsync(NetworkWidgetDefinition.WidgetId);
        AutomationProperties.SetAutomationId(networkButton, "AddNetworkWidget");

        var storageButton = new Button { Content = "Añadir almacenamiento" };
        storageButton.Click += async (_, _) => await AddSystemWidgetAsync(StorageWidgetDefinition.WidgetId);
        AutomationProperties.SetAutomationId(storageButton, "AddStorageWidget");

        var moveUpButton = new Button { Content = "Subir" };
        moveUpButton.Click += async (_, _) => await MoveSelectedWidgetAsync(-1);
        AutomationProperties.SetAutomationId(moveUpButton, "MoveSystemWidgetUp");

        var moveDownButton = new Button { Content = "Bajar" };
        moveDownButton.Click += async (_, _) => await MoveSelectedWidgetAsync(1);
        AutomationProperties.SetAutomationId(moveDownButton, "MoveSystemWidgetDown");

        var deleteWidgetButton = new Button { Content = "Eliminar widget" };
        deleteWidgetButton.Click += async (_, _) => await DeleteSelectedWidgetAsync();
        AutomationProperties.SetAutomationId(deleteWidgetButton, "DeleteSystemWidget");

        var saveWidgetButton = new Button { Content = "Guardar configuración del widget" };
        saveWidgetButton.Click += async (_, _) => await SaveWidgetSettingsAsync();
        AutomationProperties.SetAutomationId(saveWidgetButton, "SaveSystemWidgetSettings");

        var closeButton = new Button { Content = "Cerrar" };
        closeButton.Click += (_, _) => Close();
        AutomationProperties.SetAutomationId(closeButton, "CloseDockTools");

        var panel = new StackPanel
        {
            Spacing = 12,
            Width = 600,
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
        panel.Children.Add(_theme);
        panel.Children.Add(_position);
        panel.Children.Add(_monitor);
        panel.Children.Add(_iconSize);
        panel.Children.Add(_spacing);
        panel.Children.Add(_magnification);
        panel.Children.Add(_opacity);
        panel.Children.Add(_cornerRadius);
        panel.Children.Add(_animation);

        panel.Children.Add(Header("Comportamiento"));
        panel.Children.Add(_autoHide);
        panel.Children.Add(_showDelay);
        panel.Children.Add(_hideDelay);
        panel.Children.Add(_reduceAnimations);
        panel.Children.Add(_hideInFullscreen);
        panel.Children.Add(saveButton);

        panel.Children.Add(Header("Widgets"));
        panel.Children.Add(clockButton);
        panel.Children.Add(cpuButton);
        panel.Children.Add(memoryButton);
        panel.Children.Add(batteryButton);
        panel.Children.Add(networkButton);
        panel.Children.Add(storageButton);
        panel.Children.Add(Header("Widgets instalados"));
        panel.Children.Add(_widgetInstances);
        panel.Children.Add(_widgetMode);
        panel.Children.Add(_widgetEnabled);
        panel.Children.Add(_widgetInterval);
        panel.Children.Add(_widgetUnit);
        panel.Children.Add(_widgetVolume);
        panel.Children.Add(_widgetAdapter);
        var orderPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        orderPanel.Children.Add(moveUpButton);
        orderPanel.Children.Add(moveDownButton);
        orderPanel.Children.Add(deleteWidgetButton);
        panel.Children.Add(orderPanel);
        panel.Children.Add(saveWidgetButton);
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
            Children = { scroll }
        };

        AppWindow.Resize(new SizeInt32(700, 900));

        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Move(new PointInt32(
            workArea.X + Math.Max(0, (workArea.Width - 700) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - 900) / 2)));

        _ = LoadApplicationsAsync();
        RefreshWidgetInstances();
    }

    private void LoadMonitors()
    {
        var displays = DisplayArea.FindAll();
        var selectedIndex = 0;

        for (var index = 0; index < displays.Count; index++)
        {
            var display = displays[index];
            var target = display.IsPrimary ? DockLayout.PrimaryMonitorTarget : $"index:{index}";
            var label = display.IsPrimary
                ? "Principal"
                : $"Pantalla {index + 1} ({display.WorkArea.Width}×{display.WorkArea.Height})";

            var item = new ComboBoxItem { Content = label, Tag = target };
            _monitor.Items.Add(item);

            if (_viewModel.MonitorTarget.Equals(target, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = index;
            }
        }

        _monitor.SelectedIndex = _monitor.Items.Count == 0 ? -1 : selectedIndex;
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

    private bool _suppressWidgetSelection;

    private void RefreshWidgetInstances()
    {
        var previousId = (_widgetInstances.SelectedItem as ComboBoxItem)?.Tag is WidgetInstanceInfo previous
            ? previous.Id
            : (Guid?)null;

        _suppressWidgetSelection = true;
        try
        {
            _widgetInstances.Items.Clear();

            foreach (var info in _widgets.GetInstances()
                         .OrderBy(info => info.PresentationOrder)
                         .ThenBy(info => info.WidgetId, StringComparer.Ordinal)
                         .ThenBy(info => info.InstanceName, StringComparer.Ordinal))
            {
                var placement = info.PresentationMode == WidgetPresentationMode.DockEmbedded
                    ? "dock"
                    : "ventana";
                var item = new ComboBoxItem
                {
                    Content = $"{info.InstanceName} · {info.WidgetId} · {placement}",
                    Tag = info
                };

                _widgetInstances.Items.Add(item);

                if (previousId == info.Id)
                {
                    _widgetInstances.SelectedItem = item;
                }
            }

            if (_widgetInstances.SelectedItem is null && _widgetInstances.Items.Count > 0)
            {
                _widgetInstances.SelectedIndex = 0;
            }
        }
        finally
        {
            _suppressWidgetSelection = false;
        }

        LoadSelectedWidgetSettings();
    }

    private async void LoadSelectedWidgetSettings()
    {
        if (_suppressWidgetSelection
            || (_widgetInstances.SelectedItem as ComboBoxItem)?.Tag is not WidgetInstanceInfo info)
        {
            return;
        }

        try
        {
            var entity = await _widgetStore.GetAsync(info.Id);
            if (entity is null)
            {
                return;
            }

            using var document = JsonDocument.Parse(entity.SettingsJson);
            var root = document.RootElement;
            var interval = root.TryGetProperty("intervalSeconds", out var intervalElement)
                ? intervalElement.GetInt32()
                : DefaultInterval(info.WidgetId);

            _widgetInterval.Value = Math.Clamp(interval, 1, 10);
            _widgetEnabled.IsOn = info.IsEnabled;
            _widgetMode.SelectedIndex = info.PresentationMode == WidgetPresentationMode.DockEmbedded ? 0 : 1;
            _selectedOrder = info.PresentationOrder;

            var unit = root.TryGetProperty("unit", out var unitElement)
                ? unitElement.GetString() ?? "auto"
                : "auto";
            _widgetUnit.SelectedIndex = unit.ToLowerInvariant() switch
            {
                "mb" => 1,
                "gb" => 2,
                _ => 0
            };

            var volumeId = root.TryGetProperty("volumeId", out var volumeElement)
                ? volumeElement.GetString() ?? string.Empty
                : string.Empty;
            SelectComboTag(_widgetVolume, volumeId);

            var adapterId = root.TryGetProperty("adapterId", out var adapterElement)
                ? adapterElement.GetString() ?? string.Empty
                : string.Empty;
            SelectComboTag(_widgetAdapter, adapterId);

            var memory = info.WidgetId == MemoryWidgetDefinition.WidgetId;
            var storage = info.WidgetId == StorageWidgetDefinition.WidgetId;
            var network = info.WidgetId == NetworkWidgetDefinition.WidgetId;
            var intervalSupported = info.WidgetId is not ClockWidgetDefinition.WidgetId;

            _widgetInterval.Visibility = intervalSupported ? Visibility.Visible : Visibility.Collapsed;
            _widgetUnit.Visibility = memory ? Visibility.Visible : Visibility.Collapsed;
            _widgetVolume.Visibility = storage ? Visibility.Visible : Visibility.Collapsed;
            _widgetAdapter.Visibility = network ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _status.Text = $"No se pudo leer la configuración del widget: {ex.Message}";
        }
    }

    private async Task SaveWidgetSettingsAsync()
    {
        if ((_widgetInstances.SelectedItem as ComboBoxItem)?.Tag is not WidgetInstanceInfo info)
        {
            _status.Text = "Selecciona un widget instalado.";
            return;
        }

        try
        {
            var interval = (int)Math.Clamp(Math.Round(_widgetInterval.Value), 1, 10);
            var unit = (_widgetUnit.SelectedItem as ComboBoxItem)?.Tag as string ?? "auto";
            var volumeId = (_widgetVolume.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;
            var adapterId = (_widgetAdapter.SelectedItem as ComboBoxItem)?.Tag as string ?? string.Empty;

            var json = info.WidgetId switch
            {
                CpuWidgetDefinition.WidgetId or BatteryWidgetDefinition.WidgetId =>
                    JsonSerializer.Serialize(new { intervalSeconds = interval }),
                MemoryWidgetDefinition.WidgetId =>
                    JsonSerializer.Serialize(new { intervalSeconds = interval, unit }),
                NetworkWidgetDefinition.WidgetId =>
                    JsonSerializer.Serialize(new { intervalSeconds = interval, adapterId }),
                StorageWidgetDefinition.WidgetId =>
                    JsonSerializer.Serialize(new { intervalSeconds = interval, volumeId }),
                _ => null
            };

            if (json is not null)
            {
                await _widgets.UpdateSettingsAsync(info.Id, json);
            }

            var mode = _widgetMode.SelectedItem is ComboBoxItem { Tag: WidgetPresentationMode selectedMode }
                ? selectedMode
                : info.PresentationMode;

            await _widgets.SetPresentationAsync(info.Id, mode, _selectedOrder);
            if (info.IsEnabled != _widgetEnabled.IsOn)
            {
                await _widgets.SetEnabledAsync(info.Id, _widgetEnabled.IsOn);
            }

            _status.Text = $"Configuración guardada para {info.InstanceName}.";
            RefreshWidgetInstances();
        }
        catch (Exception ex)
        {
            _status.Text = $"No se pudo guardar el widget: {ex.Message}";
        }
    }

    private async Task MoveSelectedWidgetAsync(int delta)
    {
        if ((_widgetInstances.SelectedItem as ComboBoxItem)?.Tag is not WidgetInstanceInfo info)
        {
            return;
        }

        var ordered = _widgets.GetInstances()
            .OrderBy(item => item.PresentationOrder)
            .ThenBy(item => item.WidgetId, StringComparer.Ordinal)
            .ThenBy(item => item.InstanceName, StringComparer.Ordinal)
            .ToArray();
        var index = Array.FindIndex(ordered, item => item.Id == info.Id);
        var target = index + delta;

        if (index < 0 || target < 0 || target >= ordered.Length)
        {
            return;
        }

        try
        {
            var current = ordered[index];
            var other = ordered[target];
            await _widgets.SetPresentationAsync(current.Id, current.PresentationMode, target);
            await _widgets.SetPresentationAsync(other.Id, other.PresentationMode, index);
            RefreshWidgetInstances();
            _status.Text = $"Orden actualizado para {info.InstanceName}.";
        }
        catch (Exception ex)
        {
            _status.Text = $"No se pudo cambiar el orden: {ex.Message}";
        }
    }

    private async Task DeleteSelectedWidgetAsync()
    {
        if ((_widgetInstances.SelectedItem as ComboBoxItem)?.Tag is not WidgetInstanceInfo info)
        {
            return;
        }

        try
        {
            await _widgets.DeleteAsync(info.Id);
            _status.Text = $"Widget eliminado: {info.InstanceName}.";
            RefreshWidgetInstances();
        }
        catch (Exception ex)
        {
            _status.Text = $"No se pudo eliminar el widget: {ex.Message}";
        }
    }

    private void LoadVolumes()
    {
        _widgetVolume.Items.Clear();
        _widgetVolume.Items.Add(new ComboBoxItem { Content = "Automático", Tag = string.Empty });

        foreach (var volume in _storage.GetVolumes())
        {
            _widgetVolume.Items.Add(new ComboBoxItem
            {
                Content = $"{volume.Name} ({volume.Id})",
                Tag = volume.Id
            });
        }

        _widgetVolume.SelectedIndex = 0;
    }

    private void LoadAdapters()
    {
        _widgetAdapter.Items.Clear();
        _widgetAdapter.Items.Add(new ComboBoxItem { Content = "Automático", Tag = string.Empty });

        foreach (var adapter in _network.GetAdapters())
        {
            _widgetAdapter.Items.Add(new ComboBoxItem
            {
                Content = adapter.Name,
                Tag = adapter.Id
            });
        }

        _widgetAdapter.SelectedIndex = 0;
    }

    private static void SelectComboTag(ComboBox combo, string tag)
    {
        combo.SelectedIndex = 0;

        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
    }

    private static int DefaultInterval(string widgetId)
        => widgetId switch
        {
            CpuWidgetDefinition.WidgetId => 1,
            MemoryWidgetDefinition.WidgetId => 2,
            NetworkWidgetDefinition.WidgetId => 2,
            _ => 5
        };

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
        var theme = _theme.SelectedItem is ComboBoxItem { Tag: DockTheme selectedTheme }
            ? selectedTheme
            : _viewModel.Theme;
        var monitor = _monitor.SelectedItem is ComboBoxItem { Tag: string selectedMonitor }
            ? selectedMonitor
            : _viewModel.MonitorTarget;

        try
        {
            await _viewModel.SaveSettingsAsync(
                edge,
                _iconSize.Value,
                _spacing.Value,
                _magnification.Value,
                _opacity.Value,
                (int)Math.Round(_animation.Value),
                _autoHide.IsOn,
                theme,
                _cornerRadius.Value,
                (int)Math.Round(_showDelay.Value),
                (int)Math.Round(_hideDelay.Value),
                _reduceAnimations.IsOn,
                _hideInFullscreen.IsOn,
                monitor);

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
            RefreshWidgetInstances();
        }
        catch (Exception ex)
        {
            _status.Text = $"No se pudo crear el reloj: {ex.Message}";
        }
    }

    private async Task AddSystemWidgetAsync(string widgetId)
    {
        try
        {
            var info = await _widgets.CreateAsync(widgetId);
            _status.Text = $"Widget creado: {info.InstanceName}";
            RefreshWidgetInstances();
        }
        catch (Exception ex)
        {
            _status.Text = $"No se pudo crear el widget: {ex.Message}";
        }
    }

    private static TextBlock Header(string text)
        => new()
        {
            Text = text,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
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
