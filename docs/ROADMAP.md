# OpenBar — Roadmap

## Guiding rules
- Work in stages: inspect → design → implement → build/test → fix → document.
- Do not claim completion without verification.
- Prioritize solid foundation, clean code, performance, maintainability.

## Foundation 0.1 — complete (0.1.2 verified 2026-11-16)
Goal: a functional, compilable base that restores, builds, tests, opens, stays in background, shows its dock window, and closes correctly.

- [x] **Environment & tooling verified**: Windows 11 26H2; .NET SDK **10.0.404** (resolved by `global.json`) + 11.0.100; VS 2026 Insiders 18.13; Windows SDK 26100; Windows App SDK **2.6.5** builds and runs. *(VERIFIED 2026-11-16)*
- [x] Solution `OpenBar.sln` regenerated and all 14 projects restore via CLI. *(VERIFIED)*
- [x] Centralized dependency/configuration management verified by restore/build (`Directory.Build.props`, `Directory.Packages.props`). *(VERIFIED — 0 warnings, 0 errors)*
- [x] **Hosting + dependency injection composition**: one generic Host built in `App.OnLaunched`, resolves all services, starts hosted services in order, stops/disposes them on shutdown. *(VERIFIED — lifecycle/disposal covered by tests)*
- [x] **WinUI 3 application shell**: launches and loads XAML resource dictionaries at runtime. *(VERIFIED)*
- [x] **Main window `DockWindow`**: displays as `WinUIDesktopWin32WindowClass` titled `OpenBar`, responsive. Close hides to tray; shutdown only through the tray. *(VERIFIED — UI is still the Foundation 0.1 scaffold)*
- [~] Initial Liquid Glass visual design/resources: 7 theme dictionaries created and loaded; container/title styles applied. *(PARTIAL — not the full Liquid Glass design system yet)*
- [x] **System tray integration**: `Shell_NotifyIcon` with a hidden top-level HWND; project-owned icon, tooltip `OpenBar`; double-click toggles the dock; right-click menu (`Abrir OpenBar`, `Mostrar u Ocultar Dock`, `Salir de OpenBar`); icon restored on `TaskbarCreated` and removed on exit. *(VERIFIED 2026-11-16)*
- [x] **Controlled startup and shutdown**: single-instance mutex + named-pipe activation, host Start/Stop/Dispose, tray-driven exit, close-to-tray and minimize-to-tray. *(VERIFIED 2026-11-16)*
- [x] **Persistent configuration via SQLite + EF Core** (DbContext, migrations, settings). *(VERIFIED — Foundation 0.2)*
- [x] **Widget runtime + clock widget** (registry, lifecycle, scheduler, independent windows, SQLite recovery). *(VERIFIED — Foundation 0.3)*
- [ ] Structured logging (production providers/configuration).
- [ ] CPU & RAM widget.
- [ ] Basic multi-monitor detection.

### Verified build/run evidence (2026-11-16, Foundation 0.1.2)
- `dotnet restore OpenBar.sln` → success.
- `dotnet build OpenBar.sln -c Debug` → **0 warnings, 0 errors** (full rebuild).
- `dotnet test OpenBar.sln -c Debug` → **25 passed / 0 failed / 0 skipped** across 4 test projects.
- Runtime: one instance starts; dock window visible; tray window `OpenBar.TrayWindow.*` created; `Shell_NotifyIconGetRect` returns `S_OK` (icon registered); tray button/tooltip `OpenBar` found in the hidden-icons flyout.
- Runtime: closing the dock hides it, process stays alive, tray remains registered; second launch exits with code 0 and re-shows the existing window (exactly one process); tray double-click toggles the dock; all three menu commands work; `Salir de OpenBar` terminates the process.

## Foundation 0.2 — persistence (complete 2026-11-16)
Goal: OpenBar stores and retrieves its own configuration across restarts using a real SQLite database, with controlled migrations and no secrets in the database.

- [x] **Database location**: `%LOCALAPPDATA%\OpenBar\openbar.db` via `Environment.SpecialFolder.LocalApplicationData`; directory created automatically; never in the repo or next to the exe. *(VERIFIED)*
- [x] **Schema**: `AppSettings` (key/value/timestamps), `WidgetInstances` (id, widget type, name, enabled, position, size, JSON settings, schema version, timestamps), `DockLayouts` (name, position, alignment, size, offset, auto-hide, default flag, logical monitor target). *(VERIFIED — migration `20261116070957_InitialPersistence` applied to a real database)*
- [x] **EF Core model**: `OpenBarDbContext`, one `IEntityTypeConfiguration` per entity, enums stored as strings, `DateTimeOffset` timestamps, check constraints and filtered unique indexes. *(VERIFIED)*
- [x] **Migrations**: generated with local `dotnet-ef` 10.0.0 through the tooling-only `tools/OpenBar.DesignTime` project; no `EnsureCreated`. *(VERIFIED)*
- [x] **Data access services**: `IAppSettingsService`, `IWidgetInstanceService`, `IDockLayoutService` (async, transactional where multiple rows change, no shared `DbContext`). *(VERIFIED)*
- [x] **Host integration**: database initialization + settings load run through `PersistenceStartupService` on a worker thread; shutdown clears SQLite pools. *(VERIFIED)*
- [x] **Safety**: integrity check, pre-migration backup with retention, WAL, busy timeout, foreign keys, cross-process initialization lock, corrupt-database quarantine without deleting user data. *(VERIFIED)*
- [x] **Tests**: real SQLite temp databases covering creation, idempotency, schema/indexes/WAL, backup, corruption, settings/widget/layout CRUD, JSON independence, restart persistence, transaction rollback and host lifecycle. *(VERIFIED)*
- [x] **Secrets boundary**: obvious credential keys are rejected from `AppSettings`; credentials will use Credential Manager/DPAPI in a later phase. *(PARTIAL — contract/policy only; no secret store yet)*

### Verified persistence evidence (2026-11-16)
- `dotnet build OpenBar.sln -c Debug` → **0 warnings, 0 errors**.
- `dotnet test OpenBar.sln -c Debug` → **114 passed / 0 failed / 0 skipped** across 4 test projects.
- Runtime: first launch created `C:\Users\<user>\AppData\Local\OpenBar\openbar.db` in WAL mode, applied the initial migration and recorded `application.startup.count = 1`.
- Runtime: second launch against the existing database applied **no** migrations and recorded `application.startup.count = 2`, proving settings survive a restart.
- Runtime: `PRAGMA quick_check` → `ok`; no widget/layout rows were seeded (no fake data); no `.migration.lock` file remained after the process exited; the database could be opened exclusively once the app stopped.

## Foundation 0.3 — Widget Runtime (complete 2026-11-16)
Goal: a functional runtime that discovers widgets, manages their lifecycle, recovers persisted instances and hosts official widgets in independent WinUI 3 windows.

- [x] **Contracts**: `WidgetDefinition`, `WidgetMetadata`, `IWidget`, `WidgetContext`, `WidgetSettings`, `IWidgetScheduler`, `IWidgetSettingsConsumer`, `WidgetBase`; stable logical ids. *(VERIFIED)*
- [x] **Registry/discovery**: validation, duplicate rejection, provider-based discovery and per-definition error isolation. *(VERIFIED — unit tests)*
- [x] **Lifecycle**: explicit states, per-instance concurrency gate, idempotent start/stop, restart, fault isolation, controlled errors, delete-only-after-clean-stop. *(VERIFIED — unit tests)*
- [x] **Persistence recovery**: enabled instances recovered from `WidgetInstances`, disabled/unknown preserved, no schema change or new migration. *(VERIFIED — real SQLite tests and runtime restart)*
- [x] **Scheduler**: shared `PeriodicTimer` scopes, minimum interval, schedule limit, non-overlap, exception isolation, deterministic disposal. *(VERIFIED — unit tests)*
- [x] **Windows**: one WinUI 3 window per instance via `IWidgetWindowService`; runtime has no XAML dependency; DPI-scaled geometry and close-to-stop behavior. *(VERIFIED — manual runtime)*
- [x] **Official ClockWidget**: local time with seconds, periodic updates through the scheduler, multiple instances, JSON settings, independent window. *(VERIFIED — manual runtime: 10:25:05 → 10:25:08)*
- [x] **Host integration**: persistence → runtime → single instance → tray startup order; runtime stops before persistence; tray/single-instance behavior preserved. *(VERIFIED — host tests and manual runtime)*
- [x] **Tests**: registry, discovery, lifecycle, scheduler, SQLite persistence, host start/stop/restart and clock logic. *(VERIFIED — 71 runtime tests; 186 total)*

### Verified widget-runtime evidence (2026-11-16)
- `dotnet build OpenBar.sln -c Debug` → **0 warnings, 0 errors** (16 projects).
- `dotnet test OpenBar.sln -c Debug` → **186 passed / 0 failed / 0 skipped** (Domain 2, Application 47, Infrastructure 65, WidgetRuntime 71, Integration 1).
- `dotnet list OpenBar.sln package --vulnerable --include-transitive` → no vulnerable packages.
- Runtime: `Añadir reloj` created a persisted instance, opened the `Reloj · Reloj` window and updated the clock from `10:25:05` to `10:25:08`; a second click created `Reloj 2 · Reloj` (2 rows).
- Runtime: disabling `Reloj 2` closed its window, persisted `IsEnabled = 0`; after process restart only `Reloj` reopened, `Reloj 2` stayed stopped/disabled.
- Runtime: deleting both test instances removed their rows and windows; `PRAGMA quick_check` → `ok`, WAL remained active, no migration lock remained and the database opened exclusively after exit.
- Runtime: a second process exited while exactly one `OpenBar.App` remained; the tray icon `OpenBar` was still present in the hidden-icons flyout.

## Foundation 0.4 — Dock UI & Desktop Experience (complete 2026-11-16)
Goal: replace the provisional window with a real floating dock that launches/activates applications, shows running indicators, persists pinned apps/settings and behaves well on the desktop.

- [x] **Window**: borderless, topmost, acrylic, rounded, excluded from taskbar/Alt+Tab; no Explorer/taskbar replacement. *(VERIFIED — manual)*
- [x] **Geometry/DPI**: bottom/top/left/right, centered/start/end/stretch, offsets, icon size/spacing, DPI scale, work-area clamping, monitor fallback. *(VERIFIED — manual four edges + pure tests)*
- [x] **Pinned applications**: `PinnedApplication` table + migration, ordered CRUD, transactional reorder, Start Menu discovery and explicit path add. *(VERIFIED — real SQLite + UI)*
- [x] **Icons**: real shell thumbnails for exe/lnk, cache, eviction/retry, fallback symbol. *(VERIFIED — manual)*
- [x] **Launch/activate**: click launches when not running, activates when running, 1.5 s duplicate-launch cooldown. *(VERIFIED — manual Notepad launch + indicators)*
- [x] **Running apps**: visible top-level window enumeration, AUMID/exe matching, pinned indicators, temporary unpinned running tiles, 2 s refresh only while visible. *(VERIFIED — manual Explorer/Chrome/Terminal/Notepad)*
- [x] **Interactions**: hover magnification/tooltip, context menu (open/remove/add/settings), drag reorder persisted. *(VERIFIED — manual + DB)*
- [x] **Auto-hide**: configurable, edge activation, leave delay, no flicker, no input capture while hidden. *(VERIFIED — manual)*
- [x] **Settings**: separate window, persisted edge/icon/spacing/magnification/opacity/animation/auto-hide; test-widget action preserved. *(VERIFIED — manual restart)*
- [x] **Compatibility**: ClockWidget still opens/recovers; tray/single-instance unchanged; dock absent from taskbar. *(VERIFIED)*

### Verified dock evidence (2026-11-16)
- `dotnet build OpenBar.sln -c Debug` → **0 warnings, 0 errors** (17 projects).
- `dotnet test OpenBar.sln -c Debug` → **230 passed / 0 failed / 0 skipped** (Domain 2, Application 47, Infrastructure 74, Dock 35, WidgetRuntime 71, Integration 1).
- Runtime: dock appeared centered at the bottom with real Notepad/Explorer/Chrome/Terminal icons, hover magnification and running dots.
- Runtime: adding Notepad from the Start Menu/path created a persisted pinned tile; clicking launched Notepad and the running indicator merged with the pinned tile.
- Runtime: a second pinned app (Command Prompt) was dragged over Notepad; `PinnedApplications.SortOrder` changed from notepad/cmd to cmd/notepad.
- Runtime: the context menu removed Command Prompt from the dock and the row disappeared.
- Runtime: auto-hide hid the dock when the cursor left and revealed it at the bottom edge; positions Bottom/Top/Left/Right produced valid centered rectangles.
- Runtime: restart restored the bottom layout, pinned Notepad, disabled auto-hide setting and the ClockWidget window; startup journal incremented.
- Runtime: dock is absent from the taskbar; a second OpenBar process exits and one instance remains.

## Foundation 0.5 — Visual Polish & Desktop Experience (complete 2026-11-16)
Goal: refine the real dock into a fluid, theme-aware desktop experience without adding unrelated features.

- [x] **Theme/material**: System/Light/Dark themes, theme dictionaries, glass tint/border/action colors, configurable corner radius. *(VERIFIED — manual light/dark + persisted)*
- [x] **Proximity magnification**: cursor-driven smooth falloff, neighbor reaction, horizontal/vertical axes, fixed layout cells. *(VERIFIED — manual + unit tests)*
- [x] **Animations**: fade/scale show/hide, reduced-motion support from Windows and a persisted setting. *(VERIFIED — code/tests; transitions visually exercised)*
- [x] **Auto-hide**: configurable show/hide delays, edge activation, menu/tools suppression, no polling when disabled. *(VERIFIED — manual hide/reveal + tests)*
- [x] **Fullscreen**: shell exclusive state + conservative borderless heuristic, dock hide/restore, `Unknown` state while OpenBar owns the foreground. *(VERIFIED — Chrome F11 manual; games not launched)*
- [x] **Monitors/DPI**: monitor selector, target-display DPI, work-area clamping, display watcher, persistence. *(VERIFIED on two displays, move primary↔secondary; mixed DPI pending)*
- [x] **Settings**: theme, radius, delays, animations, fullscreen, monitor and all prior options; native themed tools window. *(VERIFIED — persisted)*
- [x] **Performance**: measured idle CPU/memory; refresh only while visible, fullscreen polling at 450 ms, auto-hide polling only when enabled. *(VERIFIED — measurements below)*
- [x] **Compatibility**: ClockWidget, tray, single-instance, pinned/running apps, persistence and reorder unchanged. *(VERIFIED)*

### Verified Foundation 0.5 evidence
- `dotnet build OpenBar.sln -c Debug` → **0 warnings, 0 errors**; `dotnet test OpenBar.sln -c Debug` → **238/238 passed**.
- Runtime: light theme rendered a light glass bar with dark controls; System/Dark restored the dark treatment.
- Runtime: corner radius 30 and magnification 1.2 persisted in SQLite and were visible after restart.
- Runtime: proximity magnification enlarged Chrome and its immediate neighbors on hover without changing the dock bounds.
- Runtime: auto-hide hid the dock after the configured delay and revealed it at the edge; context menus/settings pause it.
- Runtime: Chrome F11 fullscreen hid the dock and restoring normal mode brought it back; the dock stayed hidden while the fullscreen app remained behind OpenBar.
- Runtime: the dock moved from the primary display to the second display (`rect 3324,1185,551,90`) and back to the primary (`684,915,551,90`).
- Runtime: ClockWidget and pinned apps survived restart; tray icon and single-instance behavior remained intact.
- Measured idle cost while visible: **156 ms CPU / 10 s**, **86.3 MB private memory**, 217 MB working set.

## Foundation 0.6 - System Bridge & Native Widgets (complete 2026-11-16)
Goal: reusable, demand-driven Windows system metrics plus official CPU/memory widgets that reuse the existing runtime and dock.

- [x] **System Bridge contracts**: `ISystemMetrics`, `SystemMetricsSnapshot`, `ISystemMetricsSource`, `SystemMetricsOptions` and `NullSystemMetrics`, isolated from WinUI and replaceable in tests. *(VERIFIED - unit tests)*
- [x] **Native sources**: CPU from `GetSystemTimes` idle/kernel/user deltas and physical memory from `GlobalMemoryStatusEx`; values clamped to 0-100 and 64-bit byte counts. *(VERIFIED - runtime + tests)*
- [x] **Shared polling**: one cached/single-flight sample reused by all consumers, one shared subscription loop only while subscribers exist, interval clamping (250 ms-10 s), cancellation and exception isolation. *(VERIFIED - unit tests + runtime)*
- [x] **Runtime integration**: `WidgetContext.Metrics` supplied by `WidgetRuntimeService`; widgets schedule through `IWidgetScheduler` and never query Win32 or create timers. *(VERIFIED - tests)*
- [x] **CPU widget** (`openbar.cpu`): real percentage, activity bar, loading/unavailable state, 1-10 s interval, persisted settings. *(VERIFIED - runtime)*
- [x] **Memory widget** (`openbar.memory`): used/total RAM, percentage, activity bar, `auto`/`MB`/`GB` units, 1-10 s interval, persisted settings. *(VERIFIED - runtime + DB)*
- [x] **Dock integration**: compact CPU/RAM chip on all four orientations, visible only while the dock is active, read-only, does not replace independent widget windows. *(VERIFIED - runtime + geometry tests)*
- [x] **Settings**: tools window lists CPU/memory instances and updates interval/unit through the runtime. *(VERIFIED - UIA + SQLite)*
- [x] **Performance**: measured with widgets and without; polling frequency has negligible impact compared with the two extra WinUI windows. *(VERIFIED - measurements below)*
- [x] **Compatibility**: clock widget, pinned/running apps, themes, auto-hide, fullscreen, tray, single instance and persistence still work. *(VERIFIED - full suite + runtime)*

### Verified Foundation 0.6 evidence (2026-11-16)
- `dotnet build OpenBar.sln -c Debug` -> **0 warnings, 0 errors** (19 projects).
- `dotnet test OpenBar.sln -c Debug` -> **254 passed / 0 failed / 0 skipped** (Domain 2, Application 47, Infrastructure 86, Dock 43, WidgetRuntime 75, Integration 1).
- `dotnet list OpenBar.sln package --vulnerable --include-transitive` -> no vulnerable packages.
- Runtime: `openbar.cpu` showed 18-23% and `openbar.memory` showed 87-90%, `13.3-13.8 / 15.4 GB`; the dock chip showed `C18 / M90`.
- Runtime: settings `2 s` + `MB` changed the live memory widget to `13772.3 / 15741.3 MB`; `3 s` + `GB` persisted as `{"intervalSeconds":3,"unit":"gb"}` and reopened on restart.
- Runtime: closing both widget windows stopped their schedules (CPU 562 -> 125 ms per 10 s); restart recovered both instances.
- Runtime: moving the dock to the left edge produced a `90x624` vertical bar with the chip visible; restoring `Bottom` returned `624x90`.
- Runtime: database `PRAGMA quick_check` -> `ok`; no lock remained after exit and the database opened read-write.
- Idle with CPU+memory widgets and chip: **562 ms CPU / 10 s**, **89.9 MB private**, 224.9 MB working set. Chip only: **125 ms / 10 s**, 90.2 MB private.
- At 10 s intervals the two widgets measured 688 ms / 10 s, confirming the cost is dominated by WinUI window composition, not polling.

### Foundation 0.5 carry-over validation
- VERIFIED: themes, proximity magnification, auto-hide delays, Chrome F11 fullscreen hide/restore, primary<->secondary monitor move, restart persistence, tray/single-instance.
- BLOCKED: mixed-DPI across two displays, exclusive fullscreen game, monitor disconnect/repositioning with hardware change, isolated measurement of fade/reduced-motion visuals.

## Near-term (post 0.6)
- Validate mixed-DPI multi-monitor setups and per-monitor dock layouts.
- Dock drag/resize/snapping, floating/wide modes and auto-hide fullscreen refinements.
- Full MSIX/AUMID application picker and arbitrary Start Menu/AppsFolder pinning.
- Additional official widgets and richer System Bridge sources (battery, storage, network, temperature where hardware allows).
- Final brand artwork and the complete Liquid Glass design system.
- Credential Manager/DPAPI secret store for integrations.
- Out-of-process third-party plugin host + IPC (ALC is not a security boundary).

## Mid-term
- Integrations Hub connectors (official APIs/OAuth, credential storage via Credential Manager/DPAPI).
- Automation engine (events/conditions/actions).
- Additional official widgets and module packaging.
- Out-of-process third-party plugin host + IPC.

## Long-term
- Full Lumabar-feature-parity targets via independent implementations.
- Installer/distribution (`installers/`), telemetry-free diagnostics, accessibility and performance hardening.
- Comprehensive automated and integration test coverage.
