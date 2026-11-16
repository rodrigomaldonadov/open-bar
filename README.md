# OpenBar

OpenBar is an independent, open-source dock and widget host for **Windows 11**, built with WinUI 3 and designed around a translucent Liquid Glass-inspired desktop experience. It does not replace Explorer or the Windows taskbar.

## Current state - Foundation 0.7

- Floating, borderless, topmost dock with Desktop Acrylic and adaptive light/dark materials.
- Configurable edge (bottom/top/left/right), corner radius, icon size, spacing, material intensity and animation duration.
- True orientation-aware layout: icons and widgets stack vertically on left/right edges.
- Pinned applications discovered from the real Start Menu or added by executable path.
- Real shell icons with a retryable cache and a neutral fallback.
- Click to launch or activate; duplicate-launch cooldown; context menu to open/remove/settings.
- Temporary tiles for running applications that are not pinned, with running indicators.
- Proximity-based magnification: the cursor scales the hovered icon and its neighbours without moving the dock.
- Pointer-based drag reorder persisted in SQLite.
- Auto-hide with configurable show/hide delays, edge activation and menu-aware suppression.
- Fullscreen-aware behavior (shell fullscreen state plus a conservative borderless-window check).
- Monitor selection with work-area, DPI and display-topology handling.
- Separate settings window for applications, appearance, behavior, screens and widgets.
- SQLite + EF Core persistence with migrations, backups and corruption quarantine.
- Widget runtime with an official clock widget, independent windows and a shared scheduler.
- Widget presentation modes per instance: **DockEmbedded** (inside the dock) or **FloatingWindow**, persisted across restarts.
- Stable dock widget surfaces with context menu, ordering, enable/disable, delete, and live mode switching without overlapping schedules.
- Space management: soft length cap, hidden surfaces and a `+N` overflow menu.
- Shared System Bridge for CPU and physical memory using `GetSystemTimes` and `GlobalMemoryStatusEx`, with one cache/single-flight sample reused by every consumer.
- Official `openbar.cpu`, `openbar.memory`, `openbar.battery`, `openbar.network` and `openbar.storage` widgets.
- Compact CPU/RAM status chip in the dock and per-widget configuration in the tools window.
- System tray, single instance and controlled shutdown.

## Requirements

- Windows 11 (x64 validated).
- .NET SDK **10.0.404** (see `global.json`); do not retarget to .NET 11.
- Visual Studio 2026 Insiders or the .NET CLI with the Windows App SDK workload.
- Windows App SDK **2.6.5** and Windows SDK **26100** (restored through NuGet).

## Build, test and run

```powershell
dotnet restore OpenBar.sln
dotnet build OpenBar.sln -c Debug
dotnet test OpenBar.sln -c Debug

.\src\OpenBar.App\bin\x64\Debug\net10.0-windows10.0.26100.0\OpenBar.App.exe
```

Validated result for Foundation 0.7: **0 warnings, 0 errors, 277/277 tests passed**.

## Data and diagnostics

- Runtime database: `%LOCALAPPDATA%\OpenBar\openbar.db` (never in the repository or next to the executable).
- Pre-migration backups: `%LOCALAPPDATA%\OpenBar\backups\`.
- Startup errors: `%LOCALAPPDATA%\OpenBar\startup-error.log` (exception text only, no secrets).
- The database is created automatically; no credentials, API keys or tokens are stored in SQLite.

## Controls

- Hover: magnify the dock icons and show the application name.
- Left click: launch the application, or activate its first visible window.
- Right click: open/remove the app or open settings.
- Drag a pinned icon over another pinned icon to reorder it.
- `+`: add an application discovered from Start Menu or by path.
- Gear: open the separate settings window.
- CPU/RAM chip: open the settings window, which also configures the installed widgets.
- Integrated widget: right click for configure / move to window / disable / remove; the `+N` button reveals hidden surfaces.
- Tray: open/toggle the dock or exit OpenBar.

## Architecture

```
src/
  OpenBar.App/                  Composition root, dock/tools windows, window host services
  OpenBar.Presentation/         Dock view models, controls, embedded surface host, icon loader, themes
  OpenBar.Dock/                 Pure geometry, magnification, auto-hide, item composition
  OpenBar.Application/          Use-case contracts and DTOs
  OpenBar.Infrastructure/       SQLite/EF Core, Windows shell, shortcuts, windows, fullscreen, native System Bridge source
  OpenBar.Domain/               Entities and layout rules
  OpenBar.SystemBridge/         Shared CPU/RAM/battery/network/storage contracts, services and caches
  OpenBar.WidgetRuntime/        Widget registry, lifecycle, scheduler
  OpenBar.Widgets.Abstractions/ Widget contracts
modules/System/OpenBar.Widgets.Clock/  Official clock widget
modules/System/OpenBar.Widgets.System/ Official CPU, memory, battery, network and storage widgets
tests/                          Domain, Application, Infrastructure, Dock, WidgetRuntime, Integration
docs/                           Architecture, roadmap, decisions, development and dependencies
```

## Known limitations

- Multi-monitor placement and monitor selection are implemented and were validated with two displays; mixed DPI scaling between displays still needs a dedicated hardware validation.
- Fullscreen detection uses `SHQueryUserNotificationState` plus a conservative borderless-window heuristic. Exclusive games are supported by the shell state; custom-chrome borderless apps can be misclassified. Fullscreen behavior was manually validated with Chrome F11; no game was launched.
- Pinned app discovery covers Start Menu `.lnk`/`.exe` entries and explicit executable paths; a full MSIX/AUMID browser is not implemented.
- CPU usage is system-wide (`GetSystemTimes` idle/kernel/user deltas), not per-core. The first sample after startup has no delta and is reported as unavailable instead of a fabricated value.
- Memory values are physical RAM from `GlobalMemoryStatusEx`; commit charge, paging and temperature are not implemented.
- Battery uses `GetSystemPowerStatus`; machines without a battery show an unavailable state. Network speeds need two cumulative samples and are unavailable on the first sample; adapter enumeration filters virtual/filter interfaces but some VPN adapters may still appear.
- Storage uses `DriveInfo` over ready volumes; it does not scan directories recursively. Network/storage volume selection is stored per instance.
- Embedded surfaces use a soft dock-length cap (85% of the work area). Overflowing surfaces are hidden behind `+N`; their lifecycles remain active so they update when revealed.
- Direct pointer drag reordering of embedded surfaces is not implemented. Ordering is available through the settings window and the overflow menu; pinned application drag reorder remains available.
- The compact surface is intentionally read-only for layout; widget controls inside the view remain interactive. A fully expandable embedded widget editor is future work.
- Dock drag/resize/snapping and floating/wide modes remain future work.
- The icon/branding and the complete Liquid Glass design system are still provisional. Blur/tint is real Acrylic; no optical refraction is claimed.

## Documentation

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md)
- [`docs/ROADMAP.md`](docs/ROADMAP.md)
- [`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md)
- [`docs/DEPENDENCIES.md`](docs/DEPENDENCIES.md)
- [`docs/DECISIONS.md`](docs/DECISIONS.md)
- [`CHANGELOG.md`](CHANGELOG.md)

## License

No license has been chosen yet. Add one before distributing binaries or accepting external contributions.
