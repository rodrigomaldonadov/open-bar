using OpenBar.Domain.Widgets;

namespace OpenBar.Application.Persistence;

/// <summary>
/// Stores widget instances and their independent JSON configurations.
/// Multiple instances of the same widget type are supported.
/// </summary>
public interface IWidgetInstanceService
{
    Task<IReadOnlyList<WidgetInstance>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WidgetInstance>> GetByWidgetIdAsync(
        string widgetId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WidgetInstance>> GetEnabledAsync(CancellationToken cancellationToken = default);

    Task<WidgetInstance?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Creates an instance. A new id is generated when <paramref name="instance"/>.Id is empty.</summary>
    Task<WidgetInstance> CreateAsync(WidgetInstance instance, CancellationToken cancellationToken = default);

    Task<WidgetInstance> UpdateAsync(WidgetInstance instance, CancellationToken cancellationToken = default);

    Task<WidgetInstance> UpdateSettingsAsync(
        Guid id,
        string settingsJson,
        int settingsSchemaVersion,
        CancellationToken cancellationToken = default);

    /// <summary>Persists how the instance is presented and its dock order.</summary>
    Task<WidgetInstance> UpdatePresentationAsync(
        Guid id,
        WidgetPresentationMode presentationMode,
        int presentationOrder,
        CancellationToken cancellationToken = default);

    Task<bool> SetEnabledAsync(Guid id, bool isEnabled, CancellationToken cancellationToken = default);

    /// <summary>Inserts or updates several instances inside one transaction.</summary>
    Task SaveManyAsync(
        IReadOnlyList<WidgetInstance> instances,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
