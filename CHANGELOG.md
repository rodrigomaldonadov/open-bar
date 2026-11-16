# Changelog

## 0.7.0 - Integrated widget surfaces and advanced widgets (2026-11-16)

### Added
- Per-instance `WidgetPresentationMode` (`DockEmbedded` / `FloatingWindow`) persisted in `WidgetInstances`; existing rows migrate to `FloatingWindow`.
- `IntegratedWidgetSurfaces` migration with safe defaults and `PresentationOrder`.
- Dock-owned embedded surfaces with stable view hosting, theme-aware chrome, hover-friendly layout and a context menu (configure, move to window, disable, remove).
- Live presentation switching without overlapping lifecycles: the current instance is stopped and released before a fresh start in the new host.
- Ordering through settings and the overflow menu, with optimistic collection/panel reordering that never reparents a widget view through a recycled template.
- `+N` overflow menu, soft 85% dock-length cap and hidden-surface reveal by moving the selected widget to the front.
- True vertical dock layout: icons, widget surfaces, divider, actions and magnification adapt to Left/Right edges.
- Battery widget (`openbar.battery`) using `GetSystemPowerStatus`.
- Network widget (`openbar.network`) with cumulative byte deltas, first-sample "measuring" state, connectivity and adapter selection.
- Storage widget (`openbar.storage`) using `DriveInfo` over ready volumes without filesystem scans, plus volume selection.
- `IBatteryMetrics`, `INetworkMetrics` and `IStorageMetrics` with cached services and `WidgetContext` injection.
- Widget settings UI for installed instances: add, delete, enable/disable, mode, order, interval, memory unit, network adapter and storage volume.

### Changed
- `WidgetRuntimeService` owns presentation selection: embedded instances go to the surface host, floating ones to the window host.
- `WidgetInstanceInfo` exposes presentation mode/order; `IWidgetInstanceService` persists them.
- Compact metric views implement `IWidgetSurfaceView` and remove their window card chrome when embedded.
- Dock geometry accounts for per-surface lengths/thicknesses, overflow and all four orientations.

### Verified
- `dotnet build OpenBar.sln -c Debug` -> 0 warnings, 0 errors (19 projects).
- `dotnet test OpenBar.sln -c Debug` -> 277 passed, 0 failed, 0 skipped.
- `dotnet list OpenBar.sln package --vulnerable --include-transitive` -> no vulnerable packages.
- Existing 0.6 database migrated from 3 to 4 migrations without losing CPU/memory rows or settings.
- Runtime: CPU and memory embedded, then moved back to floating windows via settings; no duplicate schedules or windows.
- Runtime: five embedded widgets rendered in the dock; vertical Left layout produced readable surfaces and `+4`/`+1` overflow.
- Runtime: the clock widget also rendered as a generic embedded surface (compact time/date) and was removed through settings.
- Runtime: context menu moved a network widget to an independent window; the surface disappeared and the dock shrank.
- Runtime: disable/enable, add/delete and order up/down persisted in SQLite.
- Runtime: auto-hide hid the dock and edge reveal restored it; restart recovered all embedded instances, order, overflow and settings.
- Runtime: battery 96% / connected AC, network Ethernet with real KB/s, storage C: 63% (302.4/476.3 GB), CPU and RAM updating.
- Performance (5 widgets + chip, embedded): 453-688 ms CPU / 10 s, 74.8-90.8 MB private. Same widgets as floating windows: 703 ms / 10 s, 104.6 MB private. Disabled widgets (chip only): 219 ms / 10 s, 71.4 MB private.
- Foundation 0.6 baseline: 562 ms / 10 s and 89.9 MB with two floating widgets + chip.

### Known limitations
- Embedded surfaces are capped and hidden behind `+N`; hidden instances keep updating.
- Direct pointer drag reorder for embedded surfaces is not implemented; settings/overflow ordering is.
- Mixed-DPI, exclusive-game fullscreen and monitor-disconnect validation remain BLOCKED.
- Temperature and per-core CPU are intentionally not implemented.

## 0.6.0 - System Bridge and native widgets (2026-11-16)

### Added
- `OpenBar.SystemBridge`: shared `ISystemMetrics` contracts, `SystemMetricsSnapshot`, configurable `SystemMetricsOptions`, a native-source abstraction and a `NullSystemMetrics` fallback.
- Windows source for CPU (`GetSystemTimes`, idle/kernel/user deltas) and physical memory (`GlobalMemoryStatusEx`: total, available, used, percentage).
- Demand-driven `SystemMetricsService`: cache window plus single-flight refresh, shared `PeriodicTimer` subscription loop only while subscribers exist, cancellation, exception isolation and correct disposal.
- Official `openbar.cpu` widget with a compact activity card, configurable 1-10 s interval and honest unavailable state on the first sample.
- Official `openbar.memory` widget showing used/total RAM and percentage, with `auto`/`MB`/`GB` units and a configurable interval.
- `WidgetContext.Metrics`, supplied by the runtime, so widgets consume the shared bridge instead of native APIs or their own timers.
- Compact CPU/RAM chip at the end of the dock, visible on all four edges, with a tooltip and a shortcut to settings.
- System-widget settings in the tools window: select a CPU/memory instance, change its interval and memory unit, and persist through `IWidgetInstanceService`/`IWidgetRuntime.UpdateSettingsAsync`.

### Changed
- `DockMetrics`/`DockViewModel` include the system chip in item counts and geometry.
- The tools window accepts `IWidgetInstanceService` to read and write widget settings without accessing the database directly.
- No new migration: system widget settings live in the existing `WidgetInstances.SettingsJson`.

### Verified
- `dotnet build OpenBar.sln -c Debug` -> 0 warnings, 0 errors (19 projects).
- `dotnet test OpenBar.sln -c Debug` -> 254 passed, 0 failed, 0 skipped (Domain 2, Application 47, Infrastructure 86, Dock 43, WidgetRuntime 75, Integration 1).
- `dotnet list OpenBar.sln package --vulnerable --include-transitive` -> no vulnerable packages.
- Runtime: CPU showed real values (18-23%), memory showed 87-90% and 13.3-13.8 / 15.4 GB; the dock chip showed `C18 / M90`.
- Runtime: changing the memory widget to `2 s`/`MB` updated the open widget live to `13772.3 / 15741.3 MB`; restoring `3 s`/`GB` persisted `{"intervalSeconds":3,"unit":"gb"}` and survived a restart.
- Runtime: closing both widget windows stopped their schedules (CPU cost fell from 562 ms to 125 ms per 10 s); restart recovered both widgets and the dock chip.
- Runtime: moving the dock to the left edge produced a `90x624` vertical bar with the chip still visible; restoring `Bottom` returned `624x90`.
- Measured idle cost while visible with CPU+memory widgets and chip: **562 ms CPU / 10 s** (5.62% of one core), **89.9 MB private memory**, 224.9 MB working set. With widget windows closed (chip only): **125 ms / 10 s**, 90.2 MB private.
- Foundation 0.5 pending validations: Chrome F11 fullscreen, themes, magnification, auto-hide delays, monitor move and restart persistence remain verified. Mixed-DPI, exclusive-game fullscreen and monitor-disconnect validation remain BLOCKED.

## 0.5.0 - Visual polish and desktop experience (2026-11-16)

### Added
- Light, dark and system-adaptive dock themes with theme dictionaries, glass tint, borders, hover and running-dot colors.
- Configurable corner radius, persisted in `DockLayouts` (`VisualPolish` migration).
- Proximity magnification driven by the real cursor position; neighbours scale proportionally on horizontal and vertical docks.
- Fade/scale appearance for showing and hiding the dock.
- Windows reduced-animation preference detection plus an explicit "reduce animations" setting.
- Configurable auto-hide show/hide delays; menus and the settings window pause auto-hide.
- Native fullscreen detection (`SHQueryUserNotificationState` + conservative borderless-window check) and a "hide in fullscreen" setting.
- Monitor selection in settings, target-display DPI scaling, and display watcher re-application.
- Separate theme-aware settings sections for applications, appearance, behavior and screens.
- README and this changelog.

### Changed
- `DockLayout` gained theme, corner radius, auto-hide delays, reduced-motion and fullscreen options.
- `DockWindowService` now coordinates fade animation, fullscreen state and interaction suppression.
- Dock icon magnification is driven by `DockMagnificationCalculator` instead of per-item hover scaling.
- Settings window no longer hardcodes a dark palette; it follows the Windows theme.

### Verified
- `dotnet build OpenBar.sln -c Debug` → 0 warnings, 0 errors.
- `dotnet test OpenBar.sln -c Debug` → 238 passed, 0 failed.
- Real runtime: theme switching, proximity magnification, corner radius persistence, auto-hide show/hide, Chrome fullscreen hide/restore, docking on the primary and secondary display, restart recovery, tray/single-instance and ClockWidget.
- Displayed two monitors; mixed-DPI hardware validation remains pending.

## 0.4.0 — Dock UI and desktop experience (2026-11-16)

- Real floating dock window, pinned/running applications, shell icons, launch/activate, context menu, drag reorder, four edges, auto-hide, settings window and SQLite persistence.
- 17 projects and 230 tests.

## 0.3.0 — Widget runtime (2026-11-16)

- Widget contracts, registry/discovery, lifecycle manager, scheduler, independent windows, ClockWidget.
- 16 projects and 186 tests.

## 0.2.0 — Persistence (2026-11-16)

- SQLite + EF Core, migrations, settings/widget/layout persistence, backups and corruption quarantine.
- 114 tests.

## 0.1.0 / 0.1.2 — Foundation (2026-11-16)

- WinUI 3 shell, Generic Host/DI, single instance, system tray, controlled lifecycle and tests.
