namespace OpenBar.Widgets.Abstractions;

/// <summary>
/// Optional contract for widgets that support configuration updates. Widgets
/// without settings simply do not implement it, so the base lifecycle contract
/// stays small.
/// </summary>
public interface IWidgetSettingsConsumer
{
    Task OnSettingsChangedAsync(WidgetSettings settings, CancellationToken cancellationToken = default);
}
