# OpenBar — Development Guide

## 1. Verified environment (2026-11-16, final validation)

Working directory: `C:\Users\rodri\OneDrive\Desktop\dock-win` (git repository initialized during Foundation 0.2 with a .NET/WinUI `.gitignore`; no commits are created automatically).

| Check | Verified result |
|---|---|
| OS | Windows NT 10.0.26300.0, DisplayVersion 26H2, Build 26300 |
| `dotnet` host / location | Host 11.0.0 (x64, win-x64) at `C:\Program Files\dotnet\dotnet.exe` |
| .NET SDKs installed | **10.0.404** and **11.0.100** (`C:\Program Files\dotnet\sdk`) — `global.json` resolves to 10.0.404 via `rollForward: latestFeature` |
| .NET runtimes installed | AspNetCore/NETCore/WindowsDesktop: 8.0.34, 10.0.15, 11.0.0 (x64) |
| `global.json` | Requests SDK `10.0.100`, rollForward `latestFeature`; resolved to 10.0.404 |
| Target framework | `net10.0-windows10.0.26100.0` (C# `LangVersion=preview`) — unchanged, no .NET 11 retarget |
| Visual Studio | VS Community 2026 Insiders 18.13.12409.143 (`C:\Program Files\Microsoft Visual Studio\18\Insiders`); `.NET desktop`, WindowsAppSDK and Universal probes returned the instance (full inventory not exhaustively enumerated) |
| Windows SDK | Registry ProductVersion `10.0.26100`, `C:\Program Files (x86)\Windows Kits\10\`; include `10.0.26100.0` present |
| Windows App SDK | `Microsoft.WindowsAppSDK` **2.6.5** restored and built successfully |

## 2. Build/test/run commands (verified outcomes)

Commands are run from the repo root. `Directory.Build.props` defaults `Platform` to `x64` when no platform is supplied (WinUI + `WindowsAppSDKSelfContained` requires a concrete Windows architecture), so the plain CLI commands below work.

```powershell
dotnet restore OpenBar.sln            # SUCCESS (all 19 projects)
dotnet build OpenBar.sln -c Debug     # SUCCESS: 0 warnings, 0 errors
dotnet test OpenBar.sln -c Debug      # SUCCESS: 277/277 tests passed, 0 failed
```

Explicit platform is still supported: `-p:Platform=x64` (default for CLI); `ARM64`/`x86` are declared but not yet validated.

Run the app:

```powershell
.\src\OpenBar.App\bin\x64\Debug\net10.0-windows10.0.26100.0\OpenBar.App.exe
```

**Verified run (2026-11-16):** the process starts once, shows the `OpenBar` dock window and registers a tray icon. Closing the window hides it (process keeps running, tray stays registered); a second launch activates the existing instance and exits with code 0; the tray menu can open/toggle the dock and exit the process. Foundation 0.3 adds the widget controls described in §2.3.

## 2.1 Lifecycle & tray runtime verification (Foundation 0.1.2)

All checks below were executed against the built `OpenBar.App.exe` and observed with Win32/UIAutomation probes (no assumptions):

| Check | Observed result |
|---|---|
| Startup window | `class=WinUIDesktopWin32WindowClass`, `title=OpenBar`, visible |
| Tray host window | `class=OpenBar.TrayWindow.*`, `title=OpenBar.TrayWindow`, hidden top-level |
| Shell registration | `Shell_NotifyIconGetRect` → `S_OK` (HRESULT 0) with a valid rect |
| Tooltip | tray button named `OpenBar` (`SystemTray.NormalButton`) in the hidden-icons flyout |
| Close button (`WM_CLOSE`) | window becomes hidden, process stays alive, tray still registered |
| Second instance | exits with code 0; exactly one process remains; existing dock re-shown/activated |
| Tray double-click | toggles dock hidden → visible → hidden |
| `Mostrar u Ocultar Dock` | window hidden by close is shown again |
| `Abrir OpenBar` | hidden window is shown again |
| `Salir de OpenBar` | controlled shutdown; process terminates |

Manual reproduction: run the exe, close the dock window (it should disappear while the tray icon remains), double-click the tray icon, launch the exe a second time, then use `Salir de OpenBar`. Note that Windows 11 may place the icon in the hidden-icons flyout; pin it if desired.

The tray/exe icon is **provisional** project-owned artwork (`src/OpenBar.App/Assets/OpenBar.ico`, generated locally, no third-party assets). Replace it with final brand artwork before release (ADR-0013).

## 2.2 Persistence workflow (Foundation 0.2)

### Database location
The runtime database is created automatically on first start:

```
%LOCALAPPDATA%\OpenBar\
  openbar.db            # SQLite database (WAL mode)
  openbar.db-wal        # WAL journal (normal for WAL databases)
  openbar.db-shm        # shared-memory index (normal for WAL databases)
  backups\
    openbar-<utc>-<guid>.db   # pre-migration backups (retention 5)
    corrupt\
      openbar.db.<utc>.<guid>.corrupt   # quarantined corrupt files
```

- It is never stored in the repository, the working directory or the app folder.
- Delete the folder only during development when you explicitly want a clean state; production code never deletes user data.
- Open the file read-only in any SQLite client for inspection. With EF Core migrations, the `__EFMigrationsHistory` table lists applied migrations.

### Creating a migration
`dotnet ef` is pinned as a local tool (repo-root `dotnet-tools.json`, 10.0.0, `rollForward: false`). The EF Core Design package is isolated in `tools/OpenBar.DesignTime` (an executable `win-x64` startup project that is **not** part of `OpenBar.sln`) to keep vulnerability warnings out of the production build.

```powershell
dotnet tool restore
dotnet ef migrations add <MigrationName> `
  --project src\OpenBar.Infrastructure `
  --startup-project tools\OpenBar.DesignTime `
  --output-dir Persistence\Migrations `
  --namespace OpenBar.Infrastructure.Persistence.Migrations
```

The app applies pending migrations at startup (`PersistenceStartupService`), after taking a safety backup. Do **not** call `EnsureCreated()` and do not apply `dotnet ef database update` against a user's real database during normal operation.

### Runtime evidence (Foundation 0.2, 2026-11-16)
| Check | Observed result |
|---|---|
| First launch | `%LOCALAPPDATA%\OpenBar\openbar.db` created; `20261116070957_InitialPersistence` applied; `PRAGMA journal_mode` → `wal` |
| Startup journal | `application.startup.count = 1`, `application.startup.last_started_utc = <UTC>` |
| Data integrity | `PRAGMA quick_check` → `ok` |
| Restart | second launch kept the file, applied no migrations, no backup, `application.startup.count = 2` |
| No demo data | `WidgetInstances` = 0 rows, `DockLayouts` = 0 rows |
| Lock cleanup | no `openbar.db.migration.lock` file after the process exited |
| Connection release | the database could only be opened exclusively once the app had stopped; `PersistenceHostTests` asserts the same after `host.StopAsync()` |

## 2.3 Widget runtime workflow (Foundation 0.3)

### Creating and managing instances
The dock window exposes a minimal widget panel: `Añadir reloj`, `Detener último`, `Deshabilitar último`, `Eliminar último`, plus a status line listing each instance and its runtime state. Every action goes through `IWidgetRuntime`; no widget logic lives in the window.

- `Añadir reloj` calls `CreateAsync("openbar.clock")`. The runtime validates the definition, persists a `WidgetInstances` row, creates a fresh `ClockWidget`, starts it, schedules its 1-second update through `WidgetScheduler` and asks `IWidgetWindowService` to open an independent `WidgetWindow`.
- A second click auto-generates `Reloj 2`; multiple instances of the same widget type keep independent rows and JSON.
- `Deshabilitar último` stops the instance and persists `IsEnabled = false`, so it does not start on the next launch.
- `Eliminar último` stops the widget, closes its window and deletes the row. If stopping fails, the row is preserved.
- Closing a widget window yourself stops that instance but keeps its configuration (it reopens on the next launch while enabled).

### Scheduler and UI thread
`ClockWidget` never creates a timer. It schedules `clock.tick` on the runtime's `IWidgetScheduler`; the callback computes local time and marshals the text update to the view's `DispatcherQueue`. The scheduler enforces a 100 ms minimum interval, prevents overlapping callbacks, logs callback exceptions and cancels/awaits every loop when the instance stops.

### Persistence and restart
The runtime reuses `IWidgetInstanceService` from Foundation 0.2. At startup `WidgetRuntimeHostedService` discovers `openbar.clock`, loads all rows once, starts enabled instances and reports disabled/unavailable ones. No schema change or migration was needed.

### Runtime evidence (2026-11-16)
| Check | Observed result |
|---|---|
| Create clock | `Reloj · Reloj` window appeared; `WidgetInstances` gained one row |
| Clock updates | screenshot at 10:25:05 and another at 10:25:08 (seconds advanced) |
| Second instance | `Reloj 2 · Reloj` window appeared; 2 rows |
| Disable | `Reloj 2` window closed; row persisted with `IsEnabled = 0`; status `Stopped · deshabilitado` |
| Restart (process terminated) | only `Reloj` reopened; `Reloj 2` stayed `Created · deshabilitado`; `application.startup.count` incremented |
| Delete | rows removed (back to 0 widgets) and windows gone |
| Single instance | second process exited; exactly one `OpenBar.App` remained |
| Tray | `OpenBar` button still present in the Windows 11 hidden-icons flyout |
| Resource release | no `openbar.db.migration.lock`; exclusive DB open succeeded after exit; `PRAGMA quick_check` → `ok` |

> The tray context menu could not be driven reliably through Win32/UIAutomation (the popup does not expose its items), so the restart test used process termination. Graceful host shutdown is covered by `PersistenceHostTests` and `WidgetRuntimeHostTests`, and the tray/single-instance behaviour was verified in Foundation 0.1.2 and re-confirmed here.

## 2.4 Dock workflow (Foundation 0.4)

### Using the dock
- The dock is a borderless acrylic bar at the configured edge (default bottom center). It is excluded from the taskbar and Alt+Tab.
- `+` opens `OpenBar · Ajustes del dock`, a separate window with real Start Menu applications and an explicit executable path field. Select an app and press `Añadir seleccionada`, or type a path and press `Añadir ruta`.
- Icons are shell thumbnails; hover magnifies the icon and shows the name. A dot marks a running pinned app; unpinned running windows appear after the divider.
- Left click launches the app or activates its first visible window. Right click offers Abrir / Quitar del dock / Añadir / Configuración.
- Drag a pinned icon over another pinned icon to reorder; the new order is persisted immediately.
- The settings window controls edge, icon size, spacing, hover magnification, material opacity, animation duration and auto-hide. `Añadir reloj de prueba` keeps the Foundation 0.3 widget smoke path accessible.

### Runtime evidence (2026-11-16)
| Check | Observed result |
|---|---|
| Window/taskbar | Dock visible at bottom center; no OpenBar taskbar button; topmost over windows |
| Real icons | Notepad, Explorer, Chrome and Terminal shell thumbnails rendered |
| Hover | Notepad icon scaled up and remained inside its layout cell |
| Pin/add | Notepad pinned via path; a Start Menu Blend shortcut also pinned via the discovery list |
| Launch | Clicking Notepad launched `notepad`; the running dot merged with the pinned tile |
| Reorder | Real mouse drag moved Notepad → Command Prompt; SQLite order changed to cmd/notepad |
| Context menu | `Quitar del dock` removed Command Prompt and its row |
| Auto-hide | Cursor away hid the dock; cursor at the work-area edge revealed it |
| Edges | Bottom `684,915`; Top `648,15`; Left `15,198`; Right `1815,198` (dock-relative rects) |
| Restart | Layout (Bottom, auto-hide off), pinned Notepad and ClockWidget survived; startup count incremented |
| Single instance | Second process exited; one OpenBar process remained |
| Cleanup | Test clock/widget rows removed; no migration lock; DB quick_check `ok` |

> The native tray context menu opens, but its items are not exposed to UI Automation; the `Salir de OpenBar` item could not be selected programmatically. Graceful shutdown is covered by `PersistenceHostTests`/`WidgetRuntimeHostTests`; the dock/tray behavior itself was verified manually and in Foundation 0.1.2.

## 2.5 Visual polish workflow (Foundation 0.5)

### Appearance and behavior
- Open the gear window. **Apariencia** controls theme (Automático/Claro/Oscuro), edge, monitor, icon size, spacing, maximum magnification, material intensity, corner radius and animation duration.
- **Comportamiento** controls auto-hide, show delay, hide delay, reduced motion and fullscreen hiding. All values persist in the default `DockLayout`.
- Light/dark themes use theme dictionaries for the glass gradient, border, tint, buttons, hover highlight and running dot. Acrylic remains the native backdrop; no optical refraction is claimed.
- Hover magnification is proximity-based: the cursor scales the hovered icon and its neighbours on the dock axis, while every layout cell keeps its size so the dock does not jump.
- Show/hide uses a short opacity/scale transition. Windows `AnimationsEnabled` is observed; the explicit reduced-motion setting forces instant transitions.

### Auto-hide and fullscreen
- Auto-hide uses a 120 ms cursor poll only when enabled, with independent show/hide delays and a 4 px activation edge. Context menus and the settings window pause it.
- Fullscreen detection reports shell D3D/presentation state and a conservative borderless window (full monitor, no caption). Chrome F11 was used for manual validation; exclusive games were not launched.
- When the dock itself owns the foreground, the detector returns `Unknown` and keeps the previous state, preventing the dock from appearing over an app that is still fullscreen behind it.

### Monitors and DPI
- The settings window lists `DisplayArea` monitors; `primary`/`index:N` is persisted. DPI is obtained from the target display with `GetDpiForMonitor`; `DisplayAreaWatcher` re-applies geometry on topology changes.
- Verified with two displays: the dock moved to the second display and back. Mixed-DPI scaling between the displays was not exercised.

### Runtime evidence (2026-11-16)
| Check | Observed result |
|---|---|
| Theme | Light bar with dark controls; System/Dark restored the dark glass |
| Corner/magnification | `CornerRadius=30`, `Magnification=1.2` persisted and survived restart |
| Proximity | Chrome and immediate neighbours enlarged; dock bounds unchanged |
| Auto-hide | Hidden after the delay; revealed at the edge; settings/menu interaction pauses |
| Fullscreen | Chrome F11 hid the dock; normal mode restored it |
| Second display | Dock rect `3324,1185,551,90`; primary `684,915,551,90` |
| Persistence | Pinned apps, layout, theme, monitor, delays and ClockWidget recovered |
| Idle cost | 156 ms CPU over 10 s, 86.3 MB private, 217 MB working set |

> Fullscreen testing used Chrome F11, not a fullscreen game. The shell exclusive-state path is implemented but not hardware-validated with a game. Mixed-DPI behavior on two displays remains pending.

## 2.6 System Bridge workflow (Foundation 0.6)

### Creating and configuring widgets
- Open the gear window and use **Añadir CPU** / **Añadir memoria** (automation ids `AddCpuWidget` / `AddMemoryWidget`). Each creates a normal persisted `WidgetInstances` row and an independent window.
- The CPU window shows `Midiendo…` until the second cumulative sample exists, then a real 0-100%. Memory is available immediately.
- In **Widgets del sistema** choose an instance (`SystemWidgetInstance`), set the interval 1-10 s (`SystemWidgetInterval`), choose the memory unit for `openbar.memory` (`SystemWidgetUnit`) and press **Guardar configuración del widget** (`SaveSystemWidgetSettings`).
- Updates go through `IWidgetRuntime.UpdateSettingsAsync`, so the open widget reschedules immediately through `IWidgetSettingsConsumer` and the JSON is persisted without restarting.

### Shared service and native APIs
- `SystemMetricsService.RefreshAsync` caches one sample for 350 ms and serializes concurrent refreshes with a semaphore; widgets and the dock chip therefore share a single `GetSystemTimes`/`GlobalMemoryStatusEx` capture.
- `Subscribe` runs one shared `PeriodicTimer` only while subscribers exist; the dock chip subscribes at 1 s while the dock is active and disposes on hide.
- CPU is `(1 - idleDelta / (kernelDelta + userDelta)) * 100`; the first capture has no delta and reports unavailable. Memory derives used/percentage from `ullTotalPhys` and `ullAvailPhys`.

### Runtime evidence (2026-11-16)
| Check | Observed result |
|---|---|
| CPU widget | Real values 18-23% after the first sample |
| Memory widget | 87-90%, `13.3 / 15.4 GB`; unit switch to MB showed `13772.3 / 15741.3 MB` |
| Dock chip | `C18 / M90` with tooltip; tapping opens settings |
| Persistence | Memory row `{"intervalSeconds":3,"unit":"gb"}`; reopened with the same values after restart |
| Close window | Widget stopped; CPU cost fell from 562 ms to 125 ms per 10 s |
| Vertical dock | `Left` produced `90x624`; chip remained visible; `Bottom` restored `624x90` |
| Restart | Both enabled widgets recovered and resumed updating |
| Database | `PRAGMA quick_check` -> `ok`; no lock after exit |
| Idle cost (widgets+chip) | 562 ms CPU / 10 s, 89.9 MB private, 224.9 MB working set |
| Idle cost (chip only) | 125 ms CPU / 10 s, 90.2 MB private |

> The 10 s interval run measured 688 ms / 10 s, so the two extra WinUI windows dominate cost; polling was not the driver. Mixed-DPI, exclusive-game fullscreen and monitor-disconnect validation remain BLOCKED.

## 2.7 Integrated surfaces workflow (Foundation 0.7)

### Presentation and surface management
- Open the gear window. **Widgets instalados** lists every persisted instance with its mode (`dock`/`ventana`).
- Select an instance to edit mode, enabled state, interval, unit/adapter/volume, order and deletion.
- **Integrar en el dock** changes the mode; the runtime stops the old host, disposes its schedule, closes the window/surface and starts one fresh widget in the new host. **Ventana independiente** reverses it.
- Right-click an embedded surface for configure, move to window, disable and remove. The dock `+N` button lists hidden surfaces and moves the selected one to the front.
- Order changes do not restart the widget; they persist `PresentationOrder` and reorder the existing stable wrapper.

### Space, orientation and lifecycle
- The dock caps its length at 85% of the work area and hides surplus surfaces behind `+N`; icons are never shrunk.
- Left/Right edges switch the real panels to vertical; this affects icons, surfaces, dividers and magnification.
- A widget has exactly one `IWidget`/scheduler scope. Mode changes use stop-then-start; no two schedules overlap. Closing the app stops all widgets and surfaces.
- Battery uses `GetSystemPowerStatus`, network uses cumulative `NetworkInterface` counters (first sample has no speed), storage uses `DriveInfo`.

### Runtime evidence (2026-11-16)
| Check | Observed result |
|---|---|
| Embedded CPU/RAM | Both rendered in the dock; their windows disappeared |
| Mode switch | Embedded -> floating opened a window, closed the surface and shrank the dock |
| Context menu | Configure, move, disable and remove were present; move worked |
| Settings | Add/delete, enable/disable, order up/down, adapter/volume and interval persisted |
| Overflow | 5 surfaces produced `+1`; reveal moved the hidden widget to the front |
| Generic surface | Clock widget embedded with compact time/date and deleted cleanly |
| Vertical | Left dock `150x841`, readable network surface, `+4` overflow; Bottom restored |
| Auto-hide | Dock hid and edge reveal restored it |
| Restart | All embedded instances, order, mode, overflow and settings recovered |
| Battery/network/storage | 96% AC, Ethernet KB/s, C: 63% (302.4/476.3 GB) |
| Windows source comparison | `Win32_Battery` 96%/AC; `Get-Volume C` 511.4 GB / 186.7 GB free = 476.3/173.9 GiB; `Get-NetAdapterStatistics` showed live Ethernet deltas |
| DB migration | 0.6 database migrated from 3 to 4 migrations; rows/JSON preserved |
| Performance | Embedded 453-688 ms/10 s, 74.8-90.8 MB; floating 703 ms, 104.6 MB; disabled 219 ms |

> Hidden overflow surfaces continue updating. Direct drag reorder of surfaces is not implemented; use settings/overflow. Mixed-DPI, game fullscreen and monitor-disconnect remain BLOCKED.

## 3. Fixes applied during validation

1. **Broken solution file for CLI restore**
   - Symptom: `dotnet restore OpenBar.sln` → “No project found to restore”.
   - Cause: hand-written `.sln` had no `ProjectConfigurationPlatforms` mappings, so the build engine could not derive project configurations.
   - Fix: regenerated `OpenBar.sln` with `dotnet new sln --format sln` and re-added all projects via `dotnet sln add`.

2. **NuGet downgrade error (NU1605)**
   - Symptom: `Microsoft.Windows.SDK.BuildTools` 10.0.26100.0 downgraded from 10.0.26100.4654 required by Windows App SDK 2.6.5.
   - Fix: `Directory.Packages.props` → `Microsoft.Windows.SDK.BuildTools` **10.0.26100.4654**.

3. **WindowsAppSDKSelfContained architecture error**
   - Symptom: `error : WindowsAppSDKSelfContained requires a supported Windows architecture`.
   - Cause: no platform specified for a self-contained Windows App SDK app; the default (AnyCPU) is unsupported.
   - Fix: `Directory.Build.props` now defaults `Platform` to `x64` (and `PlatformTarget`) when no platform is provided, so plain `dotnet restore/build/test OpenBar.sln` works. ARM64/x86 remain in `Platforms` for future validation. Verified: `dotnet build OpenBar.sln -c Debug` → 0 warnings, 0 errors.

4. **High-severity SQLite vulnerability (NU1903 / GHSA-2m69-gcr7-jv3q)**
   - Symptom: `SQLitePCLRaw.lib.e_sqlite3 2.1.11` flagged (transitive via EF Core Sqlite).
   - Fix: direct `SQLitePCLRaw.bundle_e_sqlite3` reference in `OpenBar.Infrastructure`, centrally pinned to **2.1.14** (patched 2.1.x line; avoids jumping to 3.x, which changes assembly identity). `dotnet list src/OpenBar.App package --vulnerable --include-transitive` now reports no vulnerable packages.

5. **Tests added (real, executed — 238 total across 6 projects)**
   - `tests/OpenBar.Domain.Tests/WidgetIdentifierTests.cs` — widget contract metadata/refresh invariant.
   - `tests/OpenBar.Application.Tests/HostCompositionTests.cs` — Host/DI builds and resolves services.
   - `tests/OpenBar.Application.Tests/HostLifecycleTests.cs` — hosted services start in registration order, stop in reverse, container-created singletons are disposed, a throwing `StopAsync` does not prevent remaining services from stopping.
   - `tests/OpenBar.Application.Tests/ApplicationLifecycleServiceTests.cs` — lifecycle transitions, idempotent shutdown, handler error isolation, disposal.
   - `tests/OpenBar.Application.Tests/TrayCommandControllerTests.cs` — open/toggle/exit tray commands map to the window and lifecycle abstractions.
   - `tests/OpenBar.Application.Tests/PersistedConfigurationCacheTests.cs` — immutable snapshot semantics and ordinal keys.
   - `tests/OpenBar.Application.Tests/ApplicationStartupJournalTests.cs` — start-count increment, previous-start reporting, UTC normalization and graceful handling of corrupt values.
   - `tests/OpenBar.Application.Tests/SettingKeyPolicyTests.cs` — credential-key rejection policy.
   - `tests/OpenBar.Infrastructure.Tests/SqliteRuntimeTests.cs` — bundled SQLite opens an in-memory DB and reports version ≥ 3.50.2 (validates the patched native runtime actually loads).
   - `tests/OpenBar.Infrastructure.Tests/WindowsSingleInstanceServiceTests.cs` — mutex acquire/idempotence/release, second-instance rejection, pipe activation end-to-end, no-listener failure, disposal.
   - `tests/OpenBar.Infrastructure.Tests/Persistence/DatabaseInitializationTests.cs` — real temp SQLite: creation, idempotency, schema/indexes/WAL, pre-migration backup, corrupt quarantine, existing data, concurrent init, cancellation.
   - `tests/OpenBar.Infrastructure.Tests/Persistence/AppSettingsServiceTests.cs` — CRUD, ordering, conflicts, validation, secret-key rejection, transactional batch, reopen persistence.
   - `tests/OpenBar.Infrastructure.Tests/Persistence/WidgetInstanceServiceTests.cs` — multiple instances per widget type, independent JSON, unique names, settings updates, enabled filter, batch insert/update, transaction rollback.
   - `tests/OpenBar.Infrastructure.Tests/Persistence/DockLayoutServiceTests.cs` — field persistence, default-layout invariants, duplicate names, updates, validation, logical monitor targets, reopen persistence.
   - `tests/OpenBar.Infrastructure.Tests/Persistence/PersistenceHostTests.cs` — host start/stop, settings cache load, idempotent restart, pooled-connection release, corrupt-DB recovery through the host.
   - `tests/OpenBar.WidgetRuntime.Tests/WidgetRegistryTests.cs` — valid registration, duplicates, invalid ids/metadata/dimensions/JSON, lookup and sorting.
   - `tests/OpenBar.WidgetRuntime.Tests/WidgetDiscoveryTests.cs` — multi-provider discovery, throwing provider, invalid/duplicate definitions and cancellation.
   - `tests/OpenBar.WidgetRuntime.Tests/WidgetSchedulerTests.cs` — periodic execution, non-overlap, exception isolation, cancellation, limits and deterministic disposal.
   - `tests/OpenBar.WidgetRuntime.Tests/WidgetRuntimeLifecycleTests.cs` — create/start/stop/restart/delete, multiple instances, unique names, idempotence, concurrency, fault isolation, settings, window close and unavailable instances.
   - `tests/OpenBar.WidgetRuntime.Tests/WidgetRuntimePersistenceTests.cs` — real SQLite create/recover/update/disable/delete, independent JSON, unknown rows and restart without duplicates.
   - `tests/OpenBar.Infrastructure.Tests/Persistence/PinnedApplicationServiceTests.cs` — real SQLite pinned CRUD, duplicate targets, transactional reorder, reopen persistence and invalid input.
   - `tests/OpenBar.Dock.Tests/DockLayoutCalculatorTests.cs` — four-edge geometry, alignment, DPI, stretch, clamping, monitor fallback and new visual defaults.
   - `tests/OpenBar.Dock.Tests/DockMagnificationCalculatorTests.cs` — maximum under the cursor, proportional neighbours, falloff, disabled magnification and empty input.
   - `tests/OpenBar.Dock.Tests/DockAutoHideControllerTests.cs` — show/hide delays, edge activation, flicker prevention and disable behavior.
   - `tests/OpenBar.Dock.Tests/DockItemComposerTests.cs` — AUMID/executable/alias matching, unpinned grouping, explorer-host rule and ordering.
   - `tests/OpenBar.Dock.Tests/DockControllerTests.cs` — default layout, add/remove/reorder, launch/activate, cooldown, refresh, layout clamping and shortcut icon resolution.
   - `tests/OpenBar.WidgetRuntime.Tests/WidgetRuntimeHostTests.cs` — Host start with empty/existing DB, recovery, disabled instances, shutdown and restart.
   - `tests/OpenBar.WidgetRuntime.Tests/ClockWidgetTests.cs` — definition metadata, scheduler usage, settings updates and invalid-settings fallback without creating a WinUI view.
   - `tests/OpenBar.Integration.Tests/AssemblyCompositionTests.cs` — widget contract assembly/type composition smoke test.

6. **Analyzer-clean logging and interop (Foundation 0.1.2)**
   - Converted all new logging to source-generated `LoggerMessage` (CA1848/CA1873 clean) instead of suppressing analyzers.
   - Replaced manual `ObjectDisposedException` throws with `ObjectDisposedException.ThrowIf` (CA1513) and annotated `LoadImage` with `CharSet.Unicode` (CA2101).
   - `IHost` is disposed via `IAsyncDisposable` when available because `IHost` itself does not expose `DisposeAsync`.

## 4. Repository workflow (per methodology)
1. Inspect repo and environment; identify existing files/versions/incompatibilities.
2. Define technical design for the stage.
3. Implement one stage at a time.
4. Build and run tests; fix errors.
5. Document decisions (ADR) and update roadmap/dependencies.
6. Continue to next stage.

Do not create unnecessary files, duplicate implementations, or use fake/placeholder code presented as complete functionality. Do not claim completion without verification.

## 5. Architecture & rules
- Follow `docs/ARCHITECTURE.md` and ADRs in `docs/DECISIONS.md`.
- Keep Domain free of infrastructure/presentation dependencies; Presentation free of business logic.
- Widgets communicate via contracts/events; faulty widget must not crash app.
- No plaintext credentials; use Credential Manager/DPAPI.

## 6. Remaining work after Foundation 0.7
Verified in this stage: toolchain, restore/build/tests, single-instance startup, tray integration, controlled shutdown, SQLite/EF Core persistence, widget runtime, a polished dock with themes, proximity magnification, drag reorder, four real orientations, auto-hide, fullscreen awareness, monitor/DPI selection, settings, restart persistence, shared CPU/RAM/battery/network/storage metrics, embedded/floating widget presentations, overflow, and CPU/memory/battery/network/storage widgets. Still **not implemented** (do not mark complete):
1. Mixed-DPI validation across two displays and per-monitor dock layouts.
2. Dock drag/resize/snapping, floating/wide modes and fullscreen auto-hide tuning for games.
3. Direct drag reorder for embedded surfaces and suspend/resume for hidden overflow.
4. Full MSIX/AUMID application discovery beyond Start Menu shortcuts and explicit paths.
5. Per-core CPU, per-process metrics, temperature and other sensor sources where reliable.
6. Windows autostart (explicitly out of scope for this phase).
7. Final brand artwork and the complete Liquid Glass design system.
8. Credential Manager/DPAPI secret store for integrations (only the "no secrets in SQLite" policy exists).
9. Out-of-process third-party plugin host and IPC (ALC is not a security boundary).
10. Automated UI tests for WinUI surfaces; visual interactions remain manually validated.

## 7. Single-instance IPC contract (current)
- Names: `Local\<appId>.SingleInstance.<userSID>` (mutex) and `<appId>.SingleInstance.<userSID>` (pipe), `appId` ASCII alphanumeric.
- Protocol: the second process connects to the pipe and writes a single byte `0x01` meaning "activate this instance", then exits.
- The primary listens on a `NamedPipeServerStream` (`PipeOptions.CurrentUserOnly`) as an `IHostedService`, raises `ActivationRequested`, and the App marshals it to the UI thread.
- No payload, versioning or response is defined yet; extend only with an explicit protocol version if future commands are added.
