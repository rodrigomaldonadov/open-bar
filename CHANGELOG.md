# Changelog

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
