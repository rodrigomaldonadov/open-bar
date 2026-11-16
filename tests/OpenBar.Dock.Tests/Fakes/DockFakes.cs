using OpenBar.Application.Dock;
using OpenBar.Domain.Dock;

namespace OpenBar.Dock.Tests.Fakes;

internal sealed class FakePinnedApplicationService : OpenBar.Application.Persistence.IPinnedApplicationService
{
    private readonly List<PinnedApplication> _items = [];

    public Task<IReadOnlyList<PinnedApplication>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<PinnedApplication>>(
            _items.OrderBy(item => item.SortOrder).ToArray());

    public Task<PinnedApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_items.FirstOrDefault(item => item.Id == id));

    public Task<PinnedApplication> AddAsync(PinnedApplication application, CancellationToken cancellationToken = default)
    {
        if (_items.Any(item => item.Target.Equals(application.Target, StringComparison.OrdinalIgnoreCase)))
        {
            throw new OpenBar.Application.Persistence.PersistenceConflictException("duplicate target");
        }

        if (application.Id == Guid.Empty)
        {
            application.Id = Guid.NewGuid();
        }

        application.CreatedAt = DateTimeOffset.UtcNow;
        application.UpdatedAt = application.CreatedAt;
        _items.Add(application);
        return Task.FromResult(application);
    }

    public Task<bool> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_items.RemoveAll(item => item.Id == id) > 0);

    public Task ReorderAsync(IReadOnlyList<Guid> orderedIds, CancellationToken cancellationToken = default)
    {
        if (orderedIds.Count != _items.Count || orderedIds.Distinct().Count() != orderedIds.Count)
        {
            throw new OpenBar.Application.Persistence.PersistenceValidationException("invalid order");
        }

        for (var index = 0; index < orderedIds.Count; index++)
        {
            var item = _items.Single(candidate => candidate.Id == orderedIds[index]);
            item.SortOrder = index;
        }

        return Task.CompletedTask;
    }
}

internal sealed class FakeDockLayoutService : OpenBar.Application.Persistence.IDockLayoutService
{
    private readonly List<OpenBar.Domain.Layouts.DockLayout> _layouts = [];

    public Task<IReadOnlyList<OpenBar.Domain.Layouts.DockLayout>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<OpenBar.Domain.Layouts.DockLayout>>(_layouts.ToArray());

    public Task<OpenBar.Domain.Layouts.DockLayout?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_layouts.FirstOrDefault(layout => layout.Id == id));

    public Task<OpenBar.Domain.Layouts.DockLayout?> GetDefaultAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_layouts.FirstOrDefault(layout => layout.IsDefault));

    public Task<OpenBar.Domain.Layouts.DockLayout> CreateAsync(
        OpenBar.Domain.Layouts.DockLayout layout,
        CancellationToken cancellationToken = default)
    {
        if (layout.Id == Guid.Empty)
        {
            layout.Id = Guid.NewGuid();
        }

        layout.CreatedAt = DateTimeOffset.UtcNow;
        layout.UpdatedAt = layout.CreatedAt;
        _layouts.Add(layout);
        return Task.FromResult(layout);
    }

    public Task<OpenBar.Domain.Layouts.DockLayout> UpdateAsync(
        OpenBar.Domain.Layouts.DockLayout layout,
        CancellationToken cancellationToken = default)
    {
        var existing = _layouts.Single(candidate => candidate.Id == layout.Id);
        existing.Name = layout.Name;
        existing.Position = layout.Position;
        existing.Alignment = layout.Alignment;
        existing.Size = layout.Size;
        existing.Offset = layout.Offset;
        existing.IconSize = layout.IconSize;
        existing.Spacing = layout.Spacing;
        existing.Magnification = layout.Magnification;
        existing.MaterialOpacity = layout.MaterialOpacity;
        existing.AnimationDurationMs = layout.AnimationDurationMs;
        existing.AutoHide = layout.AutoHide;
        existing.IsDefault = layout.IsDefault;
        existing.MonitorTarget = layout.MonitorTarget;
        existing.UpdatedAt = DateTimeOffset.UtcNow;
        return Task.FromResult(existing);
    }

    public Task SetDefaultAsync(Guid id, CancellationToken cancellationToken = default)
    {
        foreach (var layout in _layouts)
        {
            layout.IsDefault = layout.Id == id;
        }

        return Task.CompletedTask;
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_layouts.RemoveAll(layout => layout.Id == id) > 0);
}

internal sealed class FakeApplicationCatalogService : IApplicationCatalogService
{
    public List<DiscoveredApplication> Applications { get; } = [];

    public Task<IReadOnlyList<DiscoveredApplication>> DiscoverAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DiscoveredApplication>>(Applications.ToArray());
}

internal sealed class FakeShortcutResolver : IShortcutResolver
{
    public ResolvedApplicationTarget Result { get; set; } = new(null, null, null);

    public Task<ResolvedApplicationTarget> ResolveAsync(
        string target,
        PinnedTargetKind targetKind,
        CancellationToken cancellationToken = default)
        => Task.FromResult(Result);
}

internal sealed class FakeApplicationLauncher : IApplicationLauncher
{
    public List<DockItemInfo> Launched { get; } = [];

    public List<IntPtr> Activated { get; } = [];

    public Task LaunchAsync(DockItemInfo item, CancellationToken cancellationToken = default)
    {
        Launched.Add(item);
        return Task.CompletedTask;
    }

    public Task ActivateWindowAsync(IntPtr windowHandle, CancellationToken cancellationToken = default)
    {
        Activated.Add(windowHandle);
        return Task.CompletedTask;
    }
}

internal sealed class FakeAppWindowService : IAppWindowService
{
    public List<AppWindowSnapshot> Windows { get; } = [];

    public Task<IReadOnlyList<AppWindowSnapshot>> GetVisibleWindowsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<AppWindowSnapshot>>(Windows.ToArray());
}

internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _utc = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _utc;

    public void Advance(TimeSpan delta) => _utc += delta;
}
