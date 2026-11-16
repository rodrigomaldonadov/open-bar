# OpenBar — Architecture

## Status
- Phase: **Foundation 0.5 (Visual Polish & Desktop Experience)**
- Date: 2026-11-16
- Repository state: solution restores, builds and tests cleanly (17 projects); the app runs as a single instance with a tray icon, SQLite persistence, the widget runtime and a polished floating dock with themes, proximity magnification, drag reorder, auto-hide, fullscreen awareness, monitor/DPI handling and a settings window. Integrations, automation and the full Liquid Glass design system remain unimplemented (see `docs/ROADMAP.md`).

## Goals
OpenBar is a native Windows 11 desktop application (dock, widgets, system bridge, integrations, automation) designed for long-term evolution, modularity, low resource usage, and stability.

## Architectural style
- **Clean Architecture** with modular boundaries.
- **MVVM** in presentation (`CommunityToolkit.Mvvm`).
- **DI/Hosting** via `Microsoft.Extensions.*`.
- **Shared contracts/events** for cross-module communication; no direct widget-to-widget dependencies.

## Solution layout (planned/initial)
```
src/
  OpenBar.App/                 # Entry point, Host, DI composition, app lifecycle
  OpenBar.Presentation/        # WinUI 3 windows/controls/XAML/ViewModels/themes
  OpenBar.Domain/              # Entities, value objects, domain events, business rules
  OpenBar.Application/         # Use cases, services, abstractions (ports)
  OpenBar.Infrastructure/      # SQLite/EF Core, Windows APIs, hardware, security, networking
  OpenBar.Dock/                # Dock engine and window/behavior management
  OpenBar.WidgetRuntime/       # Widget discovery, registry, lifecycle, scheduler, hosted integration
  OpenBar.Widgets.Abstractions/# Shared widget contracts (definition, IWidget, context, settings)
  OpenBar.Integrations/        # External service connectors (official APIs/OAuth/local)
  OpenBar.Automation/          # Rules engine (events/conditions/actions)
modules/
  System/ Productivity/ Multimedia/ AI/ Developer/ Finance/ Social/  # official widget modules
  System/OpenBar.Widgets.Clock/  # official clock widget (logic + WinUI view)
tests/
  OpenBar.Domain.Tests/ OpenBar.Application.Tests/ OpenBar.Infrastructure.Tests/
  OpenBar.WidgetRuntime.Tests/ OpenBar.Integration.Tests/
tools/
  OpenBar.DesignTime/          # Tooling-only `dotnet ef` startup project (not in OpenBar.sln)
docs/ installers/
```

## Dependency rules
1. `Domain` depends on nothing (no Infrastructure/Presentation/UI frameworks).
2. `Application` depends on `Domain` and the widget contracts it exposes to the host.
3. `Infrastructure` implements `Application` interfaces; may reference `Domain`/`Application`.
4. `Presentation` contains UI/ViewModels only; business logic lives in Application/Domain.
5. Widgets do not depend on each other; communicate via contracts/events.
6. Cross-module communication via contracts/events (no tight coupling).
7. Avoid unnecessary abstractions.
8. `WidgetRuntime` depends on `Application`, `Domain` and `Widgets.Abstractions`, never on WinUI or the App project.
9. Official widget modules depend on `Widgets.Abstractions` plus WinUI for their view; they never reference each other.

`OpenBar.App` is the composition root: it references every layer, owns the generic Host, and contains the WinUI-specific `DockWindowService`. No WinUI or Win32 type appears in `Domain` or `Application`.

## Application lifecycle, single instance and system tray (Foundation 0.1.2)

### Composition and lifetime
- `App.OnLaunched` builds exactly one generic Host, resolves services from it, starts hosted services, and keeps a single `DockWindow` for the process lifetime.
- Hosted services start in registration order (single-instance listener, then tray) and stop in reverse order. Controlled shutdown stops the host, closes the window on the UI thread, marks the lifecycle stopped, disposes the host and calls `Exit()`.
- `IApplicationLifecycle` (`OpenBar.Application.Lifecycle`) is a thread-safe, idempotent state machine (`Starting → Running → ShuttingDown → Stopped`). `RequestShutdown(reason)` raises `ShutdownRequested` at most once; failing handlers are logged and never block the remaining handlers.
- Service resolution and lifecycle transitions are covered by unit tests; the tray and single-instance hosts are exercised by integration tests and by manual runtime verification.

### Single instance
- `ISingleInstanceService` (Application port) is implemented by `WindowsSingleInstanceService` (Infrastructure).
- Mutual exclusion uses a named mutex created with `initiallyOwned: false` as a kernel marker. The primary process keeps the handle open for its lifetime; if it crashes, Windows closes the handle and releases the name automatically, so no stale lock survives.
- Activation uses a per-user named pipe (`<appId>.SingleInstance.<userSID>` with `PipeOptions.CurrentUserOnly`). A second process connects and sends a one-byte activation request; the primary marshals the request to the UI thread and shows/activates the dock, then the second process exits.
- Mutex and pipe names are scoped by the current user SID so different Windows users never collide.

### System tray
- `ISystemTrayService` (Application port) is implemented by `WindowsSystemTrayService` (Infrastructure) using `Shell_NotifyIcon` P/Invoke only (no WinForms `NotifyIcon`).
- The service owns a hidden top-level Win32 window (not message-only, so it receives the shell's `TaskbarCreated` broadcast) with a per-instance generated class name. `StartAsync`/`StopAsync` are synchronous by design so window creation/destruction happens on the UI thread that owns the message loop.
- Tray commands are mapped by `TrayCommandController` (Application) to `IWindowManagementService` and `IApplicationLifecycle`, keeping the mapping unit-testable without a message loop.
- Behaviour: icon and tooltip `OpenBar`; double-click toggles the dock; right-click menu offers `Abrir OpenBar`, `Mostrar u Ocultar Dock`, a separator and `Salir de OpenBar`; the icon is restored on `TaskbarCreated` and removed on shutdown.
- The icon is project-owned (`src/OpenBar.App/Assets/OpenBar.ico`) and currently provisional (see ADR-0013).

### Window lifecycle
- `DockWindowService` (App layer) owns the single `DockWindow` and centralizes visibility policy: show, hide, toggle, show-and-activate, minimize-to-tray and shutdown close.
- Closing the window (X button) cancels the close and hides the window; minimizing hides it to the tray. Only a lifecycle shutdown request is allowed to actually close it.
- The tray, second-instance activation and tests interact through `IWindowManagementService`, never through WinUI types.

## Persistence (Foundation 0.2)

### Database location and options
- The database always lives at `%LOCALAPPDATA%\OpenBar\openbar.db` (resolved from `Environment.SpecialFolder.LocalApplicationData`), never in the repository, the working directory or next to the executable.
- `OpenBarDatabaseOptions` centralizes the directory, file name, busy timeout, initialization-lock timeout, WAL flag, pooling flag and backup retention. Tests replace `DirectoryPath` with an isolated temporary folder.
- The connection string is built once (`SqliteConnectionStringFactory`): `ReadWriteCreate`, foreign keys enabled, default timeout derived from the busy timeout, pooling controlled by options.

### Schema
| Table | Purpose | Key constraints |
|---|---|---|
| `AppSettings` | Global non-secret key/value preferences | `Key` primary key, length checks, value length ≤ 32 768 |
| `WidgetInstances` | One row per persisted widget instance | `Id` GUID key, unique `(WidgetId, InstanceName)`, `SettingsJson` must pass `json_valid`, schema version ≥ 1, optional positive width/height |
| `DockLayouts` | Named dock layouts | unique `Name`, filtered unique index on `IsDefault = 1` (at most one default), positive size, non-negative offset |

- There is no `UserPreference` table: global preferences are `AppSetting` rows. A separate mechanism would duplicate the setting pipeline without adding capability.
- `WidgetInstances` stores position and size as explicit `PositionX`/`PositionY`/`Width`/`Height` columns so the schema stays queryable without JSON parsing.
- `DockLayouts.MonitorTarget` stores a logical selector (`primary`, `index:N`), never a hardware ID, so layouts survive monitor reordering.
- Enums (`DockEdge`, `DockAlignment`) are stored as strings; all timestamps are `DateTimeOffset` in UTC maintained by the services.

### EF Core model
- `OpenBarDbContext` exposes `AppSettings`, `WidgetInstances` and `DockLayouts` and discovers one `IEntityTypeConfiguration` per entity from the Infrastructure assembly.
- `OpenBarDbContextFactory` implements `IDbContextFactory<OpenBarDbContext>` and is registered as a singleton. Contexts are short-lived and created per unit of work; a context is never shared between concurrent operations.
- There is no generic repository: the three application ports map directly to focused EF Core query/save implementations, which keeps queries explicit and avoids a leaky abstraction.
- Migrations live in `src/OpenBar.Infrastructure/Persistence/Migrations`. The initial migration is `20261116070957_InitialPersistence`. `EnsureCreated` is never used.

### Initialization lifecycle
`PersistenceStartupService` (an `IHostedService`) runs on a worker thread before the dock window is shown:
1. Create the database directory if needed.
2. Acquire an exclusive cross-process lock file (`openbar.db.migration.lock`, `FileShare.None`, `DeleteOnClose`); in-process calls are additionally serialized and memoized.
3. Run `PRAGMA quick_check` on an existing non-empty database.
4. When migrations are pending, create an online backup in `backups/` (retention: 5) before applying anything.
5. Apply pending migrations, then enable WAL and `synchronous = NORMAL`.
6. Load all settings into `PersistedConfigurationCache` for fast, read-only access.
- A corrupt database is moved—together with its `-wal`/`-shm`/`-journal` sidecars—into `backups/corrupt/` and preserved byte-for-byte; a fresh database is then created. Only one recovery attempt is made; a second failure stops startup with a controlled `DatabaseInitializationException`. User files are never deleted automatically.
- Migration failures fail startup; they never trigger deletion or silent schema changes.
- `StopAsync` clears SQLite connection pools so the file is released when OpenBar exits.

### Data access services
- `IAppSettingsService` — query, create, update, upsert, transactional batch upsert and delete of settings.
- `IWidgetInstanceService` — create/update/delete instances, enumerate all/enabled/by widget type, update JSON settings, transactional batch save.
- `IDockLayoutService` — CRUD, default-layout lookup, transactional `SetDefaultAsync` that clears the previous default.
- All methods are async and accept a `CancellationToken`. Provider constraint failures are translated into `PersistenceValidationException`, `PersistenceConflictException`, `PersistenceNotFoundException` or a generic `PersistenceException` without leaking SQL or stored values.
- `ApplicationStartupJournal` is the first real consumer: it records `application.startup.count` and `application.startup.last_started_utc`, proving configuration survives restarts without any demo data.

### Secrets boundary
- `SettingKeyPolicy` rejects obviously sensitive keys (`password`, `passwd`, `secret`, `token`, `apikey`/`api_key`, `privatekey`/`private_key`, `credential`). This is a defensive guard, not the secret store.
- Passwords, API keys and OAuth tokens will be stored through a Windows Credential Manager/DPAPI abstraction in a later phase. Nothing sensitive is logged.

## Widget Runtime (Foundation 0.3)

### Contracts (`OpenBar.Widgets.Abstractions`)
- `WidgetDefinition` binds a stable `WidgetId` to `WidgetMetadata` and a `Func<IWidget>` factory. The id is a logical lower-case identifier (`openbar.clock`), never a display name or assembly identity.
- `WidgetMetadata` carries display name, description, category, version, author, min/max/default dimensions, capability flags and `AllowsMultipleInstances`.
- `IWidget` is an explicit async lifecycle: `InitializeAsync(WidgetContext)`, `StartAsync`, `StopAsync`, `CreateView`, plus `IAsyncDisposable`. `WidgetBase` provides no-op defaults so simple widgets stay small.
- `WidgetContext` exposes only the instance id/name, `WidgetSettings`, an `ILogger` and an instance-scoped `IWidgetScheduler`. The application DI container, the database and the window manager are deliberately not exposed.
- `WidgetSettings` validates and owns JSON plus a schema version; widgets deserialize their own strongly typed shape, so they never implement persistence.
- `IWidgetSettingsConsumer` is an optional contract for widgets that react to settings updates.
- `WidgetCapabilities` (`Settings`, `PeriodicUpdates`, `Commands`, `Notifications`) is declarative metadata; the runtime uses it to gate operations such as settings updates.

### Registry and discovery
- `WidgetRegistry` is an in-memory catalog separate from persisted user instances. It validates ids/metadata/dimensions/JSON, rejects duplicates with `WidgetRegistrationException` and exposes lookups.
- `IWidgetDefinitionProvider` supplies definitions. `ConfiguredWidgetDefinitionProvider` wraps the official definitions registered in DI; a future plugin host can add providers without changing the runtime.
- `WidgetDiscoveryService` discovers provider by provider and registers each definition independently. A throwing provider, invalid definition or duplicate is recorded in `WidgetDiscoveryResult.Failures` and never prevents the remaining catalog from loading.
- A widget may exist with zero instances; an instance may survive while its definition is temporarily unavailable. Unknown instances are reported as `Unavailable` and **never deleted automatically**.

### Lifecycle manager
`WidgetRuntimeService` (`IWidgetRuntime`) is the single lifecycle surface. States are explicit:

```
Created → Initializing → Initialized → Starting → Running → Stopping → Stopped → Disposed
                                                    ↘ Faulted ↙
Unavailable (persisted instance without a definition)
```

- Every instance has its own `SemaphoreSlim`, so concurrent operations on the same instance are serialized; `StartAsync`/`StopAsync` are idempotent (`Running`/`Stopped` are no-ops). Invalid transitions throw `WidgetStateException`; starting a `Faulted` instance requires `RestartAsync`.
- Each start creates a fresh `IWidget`, a fresh `WidgetContext` and a fresh scheduler scope; a stop disposes the scope, calls `StopAsync`, then `DisposeAsync`. A crash/stop never leaves timers or views attached to the old object.
- Widget failures are isolated: the instance is marked `Faulted`, its error is recorded, resources are released where possible and the rest of the runtime keeps working. Only instance-store failures are global (`WidgetRuntimeException`).
- Creating an instance validates the definition, the multiple-instance rule, the name and the JSON, persists the row first, then starts the widget and opens its window. If startup or window creation fails, the persisted row is retained for inspection/retry, the instance is `Faulted`, and a controlled `WidgetLifecycleException` is returned.
- Deleting stops the widget first. If stopping fails, the persisted row is **not** deleted. Unknown/unavailable rows can still be deleted explicitly.
- Disabling stops the widget and persists `IsEnabled = false`; enabling persists the flag and starts it again.
- `StartAllAsync` loads all rows in one query, starts enabled instances, reports disabled ones, preserves unknown ones and collects per-instance failures. Calling it twice is idempotent.

### Persistence integration
- The runtime uses `IWidgetInstanceService` from `OpenBar.Application.Persistence`; it never touches `OpenBarDbContext`.
- Recovered geometry and `SettingsJson` come from the existing `WidgetInstances` table. No schema change was required in Foundation 0.3 and no new migration was created.
- Multiple instances of the same widget type share the definition but keep independent JSON and rows.

### Scheduler
- Widgets never create their own timers. `WidgetScheduler` creates a `WidgetSchedulerScope` per instance; `IWidgetScheduler.Schedule(name, interval, callback)` returns a disposable handle.
- Each schedule is a single `PeriodicTimer` loop, so callbacks never overlap. A callback exception is logged and the loop continues.
- `WidgetSchedulerOptions` enforces a minimum interval (default 100 ms) and a maximum number of schedules per instance (default 16). A smaller requested interval is clamped and logged.
- Stopping an instance disposes its scope, which cancels and awaits every loop; the root scheduler disposes all remaining scopes during host shutdown.

### Windows and presentation
- `IWidgetWindowService` (Application) is the host port. The runtime passes a `WidgetWindowRequest` containing the instance id, title, geometry and the running `IWidget`; it never references XAML.
- `WidgetWindowService` (App) marshals every operation to the WinUI `DispatcherQueue`. It creates one `WidgetWindow` per instance, calls `IWidget.CreateView()` on the UI thread, applies size/position (DPI-scaled) and tracks windows by instance id.
- Closing a widget window notifies the runtime, which stops the instance but keeps the persisted configuration (it starts again on the next launch if still enabled).
- Geometry is persisted as device-independent units; multi-monitor placement, drag/resize/snapping remain future work.

### Host ordering
1. Persistence (`PersistenceStartupService`) creates/checks the database, applies migrations and loads settings.
2. Widget runtime (`WidgetRuntimeHostedService`) discovers definitions, recovers enabled instances and opens their windows.
3. Single-instance listener, then the tray icon.
4. On shutdown the order is reversed: tray → single instance → widget runtime (stop widgets, close windows, dispose scheduler) → persistence (clear SQLite pools). The dock window closes after the host stops. This guarantees widgets never use a disposed database or scheduler.

### Security and future plugins
- `AssemblyLoadContext` is **not** a security boundary. Official widgets run in-process because they ship with the application.
- Third-party plugins will require an out-of-process host with a controlled IPC contract; the current contracts do not assume all widgets share the process, and `CreateView` returns an opaque object so a future serialized view descriptor is possible.
- No sandboxing or plugin IPC is implemented in Foundation 0.3.

## Dock UI and desktop experience (Foundation 0.4)

### Window model
- `DockWindow` is a borderless, always-on-top, non-resizable `OverlappedPresenter` window with a transparent WinUI root and `DesktopAcrylicBackdrop`. DWM corner preference is set to round; a solid fallback background is used when Acrylic is unavailable.
- `AppWindow.IsShownInSwitchers = false` removes the dock from Alt+Tab. Windows 11 still shows an unpackaged WinUI window in the taskbar, so `DockWindowService` additionally applies `WS_EX_TOOLWINDOW` and clears `WS_EX_APPWINDOW` with `SetWindowLongPtr`/`SetWindowPos(SWP_FRAMECHANGED)`. This is a documented Windows requirement, not a substitute for the supported property.
- The dock never replaces Explorer or the taskbar. It is a normal top-level application window positioned above the desktop.
- Closing the dock hides it; tray/second-instance activation shows it. Auto-hide uses `AppWindow.Hide/Show` and a 120 ms cursor poll only while auto-hide is enabled, so the hidden dock captures no mouse input.
- `DesktopAcrylicBackdrop` provides a blurred translucent material. The XAML overlay adds a tint/gradient/border; this is **not** claimed to be optical refraction.

### Layout, DPI and monitors
- `DockLayout` now also stores `IconSize`, `Spacing`, `Magnification`, `MaterialOpacity` and `AnimationDurationMs` alongside the existing edge/alignment/size/offset/auto-hide/monitor fields. The `DockUi` migration adds these columns with safe defaults.
- `DockLayoutCalculator` is a pure geometry component: it computes the dock length/thickness in DIPs (including pinned/running sections, add and settings actions), applies the four edges, centering/start/end/stretch alignment, margins and DPI scale, and clamps the result to the work area.
- `DockMonitorResolver` resolves logical `primary`/`index:N` selectors. Missing or disconnected monitors fall back to the primary display; `DisplayAreaWatcher.Updated` re-applies the layout when the topology or work area changes.
- Positions are physical pixels derived from the current window DPI (`GetDpiForWindow`); no fixed resolution coordinates are used.

### Pinned applications and persistence
- `PinnedApplication` (`PinnedApplications` table) stores id, name, launch target, optional arguments, target kind (executable/shortcut/packaged/protocol), resolved executable, AppUserModelId, icon key, sort order and timestamps.
- `IPinnedApplicationService` / `PinnedApplicationService` provide ordered CRUD and a transactional full-order `ReorderAsync`. The `DockUi` migration creates the table and its indexes; the existing `WidgetInstances`/`DockLayouts` data is preserved.
- Applications are **discovered**, not hardcoded: `StartMenuApplicationCatalogService` enumerates real `.lnk`/`.exe` entries from the per-user and all-users Start Menu. `ShortcutResolver` reads the shortcut target/arguments through `IShellLinkW` and extracts `shell:AppsFolder\...` AUMIDs for packaged apps.
- The add flow also accepts an explicit executable path, useful for apps without a Start Menu shortcut.
- Icons are loaded from the shell through `StorageFile.GetThumbnailAsync` (executable or `.lnk`), cached per path/size, with a neutral symbol fallback. Failed thumbnails are evicted from the cache so they can recover on a later refresh; no icons are downloaded.

### Running applications
- `AppWindowService` enumerates visible top-level windows only: it excludes OpenBar, shell surfaces, cloaked windows, tool windows and processes without a visible window. It reads the process path and, best effort, the window's AppUserModelId.
- `DockItemComposer` merges the persisted pinned list with a window snapshot. Matching prefers AppUserModelId, then the resolved executable path, then the executable target/file name (Windows app execution aliases such as Notepad), and groups multiple windows of one process into a single running tile. Pinned matches become running indicators; remaining window groups appear as temporary running tiles.
- `DockViewModel` refreshes the snapshot every 2 seconds only while the dock is visible; no polling occurs while auto-hidden.
- A click activates the first visible window of a running app, or launches the pinned target when it is not running. A 1.5 s per-item launch cooldown prevents accidental duplicate launches.

### Interactions and presentation
- `DockView` (Presentation) renders the glass bar, pinned tiles, add/settings actions, divider and temporary running tiles. `DockItemControl` animates scale and highlight on hover without changing the layout cell, and shows a running dot plus tooltip.
- Drag reorder is an explicit pointer gesture on the pinned list (6 px threshold, pointer capture, hit-testing over item bounds). It moves the `ObservableCollection`, which persists the new order through `IDockController.ReorderPinnedAsync`. Unpinned running tiles are not reorderable.
- The right-click menu offers Abrir, Quitar del dock, Añadir aplicación and Configuración. Application discovery/settings live in a separate normally sized `DockToolsWindow`; a `ContentDialog` hosted by the tiny dock window would be clipped.
- Settings cover edge, icon size, spacing, hover magnification, material opacity, animation duration and auto-hide. Changes persist in the default `DockLayout` and re-apply immediately.
- Widgets remain independent: ClockWidget still opens its own window; the dock settings window offers an `Añadir reloj de prueba` action but the dock does not embed widget surfaces.

### Auto-hide
- `DockAutoHideController` is a deterministic state machine: hidden → show when the cursor reaches the configured edge; visible → hide after a 550 ms delay once the cursor leaves the dock (plus margin). Re-entering cancels the timer, preventing flicker.
- The host polls the cursor only while auto-hide is enabled and delegates show/hide to `AppWindow`; disabling the feature immediately reveals a hidden dock.
- Maximized/fullscreen applications are not special-cased yet; the activation edge remains available and does not modify other windows.

### Lifecycle and limitations
- Startup order remains persistence → dock services/widget runtime → single instance → tray. Shutdown stops the tray/listener, then the widget runtime and dock, then persistence; the tools window is closed on the UI thread.
- Single-monitor behavior, DPI scaling, all four edges and auto-hide were verified manually. Multi-monitor topology changes are implemented through `DisplayAreaWatcher` but could not be exercised with a second physical display.
- The tray menu opens and its commands are unchanged, but native menu items are not exposed to UI Automation; the exit command was not selected programmatically. Graceful shutdown is covered by host/runtime tests.

### Visual polish (Foundation 0.5)
- `DockLayout` now persists `Theme` (System/Light/Dark), `CornerRadius`, `AutoHideShowDelayMs`, `AutoHideHideDelayMs`, `ReduceAnimations` and `HideInFullscreen`; the `VisualPolish` migration adds them with safe defaults.
- `DockView` and `DockItemControl` use theme dictionaries for the shell gradient, border, tint, action buttons, hover highlight, fallback glyph and running dot. `RequestedTheme` follows the selected mode; Windows Acrylic remains the backdrop.
- Appearance and hide/show transitions use a short opacity + scale storyboard on the dock shell. `UISettings.AnimationsEnabled` is observed; the explicit reduced-motion setting overrides the configured duration.
- `DockMagnificationCalculator` computes a smooth proximity falloff around the cursor axis. `DockView` measures item centers and drives every `DockItemControl`; the layout cell never changes, so the dock does not move or jitter.
- Auto-hide separates show dwell and hide delay. The host pauses auto-hide while a context menu or the tools window is open.
- `WindowsFullscreenStateService` reports exclusive fullscreen from `SHQueryUserNotificationState` and a conservative borderless-fullscreen heuristic (foreground window covers the whole monitor and has no caption). `DockWindowService` hides the dock and restores it when fullscreen ends. A foreground owned by OpenBar reports `Unknown`, preserving the previous state so showing the dock cannot mask a still-fullscreen app.
- `DockWindowService` computes DPI from the target display (`GetDpiForMonitor`), not just the window's current monitor, and the `DisplayAreaWatcher` re-applies geometry when displays/DPI change.
- Settings expose theme, position, monitor, icon size, spacing, magnification, material opacity, corner radius, animation duration, auto-hide, show/hide delays, reduced motion and fullscreen behavior. The tools window uses the native theme rather than a hardcoded palette.
- Performance in the verified idle state: about 156 ms CPU per 10 s and 86 MB private memory while visible with auto-hide off; running-window enumeration stays at 2 s and only while visible, fullscreen polling is 450 ms, and auto-hide polling (120 ms) only runs when enabled.

### Remaining visual/runtime limitations
- Two displays were exercised, including moving the dock to the secondary display; mixed DPI scaling between displays was not validated with a second DPI value.
- Exclusive fullscreen relies on the shell notification state; a custom-chrome borderless app can be misclassified. Fullscreen was manually verified with Chrome F11, not with a game.
- Drag/resize/snapping, floating/wide modes, dock-integrated widget surfaces and the final brand/design system remain future work.

## Core subsystems (design intent)

### Widget system
- Implemented in Foundation 0.3: contracts, registry/discovery, lifecycle manager, scheduler, persistence recovery and independent WinUI windows (see the **Widget Runtime** section above).
- Official clock widget lives in `modules/System/OpenBar.Widgets.Clock`; additional widgets follow the same definition + view pattern.
- Third-party plugins: **isolated out-of-process with IPC** (ALC not treated as a security boundary); not implemented yet.

### Dock engine
- Implemented in Foundation 0.4: real floating window, pinned/running application model, shell icons, launch/activate, drag reorder, four edges, DPI-aware geometry, auto-hide and persisted visual settings (see the **Dock UI** section above).
- Independent window (does not replace/remove Explorer/taskbar).
- Still ahead: drag/resize/snap, multi-monitor topology validation, richer app management, animations tuned with reduced-motion support.

### Visual system (Liquid Glass)
- WinUI 3 + XAML + Windows Composition, Acrylic where appropriate, translucent gradients/borders, highlights/shadows/animations/visual states.
- Theme resources under `src/OpenBar.Presentation/Themes/` (`LiquidGlass.xaml`, `Colors.xaml`, `Typography.xaml`, `GlassDock.xaml`, `GlassWidget.xaml`, `GlassFlyout.xaml`, `Animations.xaml`).
- No claim of true optical refraction when using blur/composition only.

### System Bridge
- Centralized Windows/system access: CPU, RAM, temperature, battery, storage, network, processes, volume, mic/audio devices, open apps, monitors.
- Shared services + centralized scheduler to avoid duplicate polling; event-driven updates where possible.
- Hardware/sensor access limited by drivers/permissions (graceful degradation).

### Integrations Hub
- Independent connectors (AI, Developer, Productivity, Multimedia/other) using official APIs/OAuth/local-compatible methods only.
- Tokens stored in Windows Credential Manager or DPAPI (never plaintext/DB).

### Automation engine
- Event/condition/action rules (meetings, AI usage limits, devices, schedules, calendar, system state), configurable enable/disable.

### Persistence
- SQLite + EF Core 10 with controlled migrations (implemented in Foundation 0.2; see the **Persistence** section above).
- Stores widgets, instances, configs, layouts, themes, integrations config, automations and preferences. **No credentials in the database.**

## Performance & stability principles
- No UI-thread blocking; async + `CancellationToken`.
- Shared scheduler/cache, event-driven updates, no unnecessary polling.
- Structured logging, exception handling, correct disposal, leak avoidance.
- A faulty widget must never crash the whole application.

## Notes
- Target stack: C# 14, .NET 10 LTS, WinUI 3, Windows App SDK 2.6.5, Windows SDK 26100 — all verified by restore/build/test/run (see ADR-0006).
- Foundation 0.1.2 lifecycle/tray implementation lives in `OpenBar.Application` (ports + lifecycle state machine + tray command mapping), `OpenBar.Infrastructure` (single instance, shell tray), and `OpenBar.App` (Host composition + `DockWindowService`).
- Foundation 0.2 persistence lives in `OpenBar.Domain` (entities/enums), `OpenBar.Application/Persistence` + `OpenBar.Application/Startup` (ports, exceptions, key policy, cache, startup journal) and `OpenBar.Infrastructure/Persistence` (DbContext, configurations, migrations, initializer, services, hosted service, DI extension). `App.OnLaunched` calls `AddOpenBarPersistence()` before any other hosted service.
- Foundation 0.3 widget runtime lives in `OpenBar.Widgets.Abstractions` (contracts), `OpenBar.WidgetRuntime` (registry, discovery, lifecycle, scheduler, hosted service, DI), `OpenBar.Application/Widgets` (window port) and `OpenBar.App` (`WidgetWindowService`, `WidgetWindow`, dock controls). The clock module lives in `modules/System/OpenBar.Widgets.Clock`.
- Foundation 0.4 dock lives in `OpenBar.Domain/Dock` + `OpenBar.Domain/Layouts` (entities), `OpenBar.Application/Dock` + `Persistence` (ports/DTOs), `OpenBar.Dock` (pure geometry, auto-hide, composition, controller), `OpenBar.Infrastructure/Persistence` + `Windows/Dock` (EF, Start Menu, shortcuts, launcher, window enumeration), `OpenBar.Presentation/Dock` (view model, icon loader, controls) and `OpenBar.App` (dock window, tools window, window/DPI/auto-hide host).
- Foundation 0.5 adds `DockTheme`, the `VisualPolish` migration, proximity magnification (`DockMagnificationCalculator`), theme dictionaries, fade transitions, configurable auto-hide delays, `IFullscreenStateService`/`WindowsFullscreenStateService`, target-display DPI and the expanded settings window. `README.md` and `CHANGELOG.md` document the user-facing state.
- This document will evolve via ADRs in `docs/DECISIONS.md`.
