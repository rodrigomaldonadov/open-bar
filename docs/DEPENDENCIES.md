# OpenBar — Dependencies

## Target stack — verification status (2026-11-16, updated after successful build/run)

| Area | Configured version | Status |
|---|---|---|
| Language | C# 14 (`LangVersion=preview`) | **VERIFIED** — compiles on .NET SDK 10.0.404. |
| Framework / TFM | `net10.0-windows10.0.26100.0` | **VERIFIED** — restored, built, tested, run. |
| UI | WinUI 3 (`UseWinUI`) | **VERIFIED** — app launches with `WinUIDesktopWin32WindowClass` window titled `OpenBar`. |
| Windows App SDK | `Microsoft.WindowsAppSDK` **2.6.5** | **VERIFIED** — restores and builds; app runs in self-contained unpackaged mode. |
| Windows SDK BuildTools | `Microsoft.Windows.SDK.BuildTools` **10.0.26100.4654** | **VERIFIED** — required by WinAppSDK 2.6.5 (previous 10.0.26100.0 caused NU1605 downgrade). |
| Windows SDK (OS) | 10.0.26100 (include 10.0.26100.0) | **VERIFIED (detected)**. |
| MVVM | `CommunityToolkit.Mvvm` **8.4.0** | **VERIFIED** — restored/built (no runtime use yet). |
| DI / Hosting / Logging | `Microsoft.Extensions.*` **10.0.0** | **VERIFIED** — Host builds and resolves services (tested); `Microsoft.Extensions.Hosting.Abstractions` was added for the widget hosted service. |
| Widget runtime | No new third-party packages | **VERIFIED** — the runtime uses `Microsoft.Extensions.*`, `System.Threading`, `System.Text.Json` and the existing Windows App SDK for the clock module view. |
| Persistence | `Microsoft.EntityFrameworkCore` / `.Sqlite` / `Microsoft.Data.Sqlite` **10.0.0** | **VERIFIED** — `OpenBarDbContext`, per-entity configurations and the `20261116070957_InitialPersistence` migration ran against a real SQLite database; no `EnsureCreated`. |
| EF Core tooling | `Microsoft.EntityFrameworkCore.Design` **10.0.0** + local `dotnet-ef` **10.0.0** | **VERIFIED** — migration generated with `dotnet ef` through the tooling-only `tools/OpenBar.DesignTime` project; Design package is `PrivateAssets=all` and not part of `OpenBar.sln`. |
| SQLite native | `SQLitePCLRaw.bundle_e_sqlite3` **2.1.14** (direct in Infrastructure) | **VERIFIED** — resolves GHSA-2m69-gcr7-jv3q / NU1903; vulnerability scan clean. |
| Hardware | `LibreHardwareMonitorLib` **0.9.4** | **PARTIAL** — restores, not used/tested yet (sensor access is hardware/driver dependent). |
| HTTP resilience | `Microsoft.Extensions.Http.Resilience` **10.0.0** | **VERIFIED** — restores/builds; no connectors yet. |
| Testing | xUnit 2.9.2, runner 2.8.2, Test SDK 17.12.0, coverlet 6.0.2 | **VERIFIED** - **254/254 tests passed** across 6 projects (Domain 2, Application 47, Infrastructure 86, Dock 43, WidgetRuntime 75, Integration 1); persistence uses real temp SQLite files, dock/widget tests use deterministic fakes. |

## Package/version policy
- Verify compatibility with **.NET 10** and the Windows App SDK in use before adding/upgrading dependencies.
- Keep versions centralized in `Directory.Packages.props` (`ManagePackageVersionsCentrally=true`).
- Do not introduce additional frameworks without justification (record in `docs/DECISIONS.md`).
- Transitive security pins are allowed with an explanatory comment (see SQLitePCLRaw below).

## Verified command results (2026-11-16)

```
dotnet --list-sdks                  -> 10.0.404, 11.0.100
dotnet restore OpenBar.sln                   -> success, 0 warnings (19 projects)
dotnet build OpenBar.sln -c Debug            -> 0 warnings, 0 errors
dotnet test OpenBar.sln -c Debug             -> 254 passed, 0 failed, 0 skipped
dotnet list OpenBar.sln package --vulnerable --include-transitive -> no vulnerable packages
dotnet tool restore                          -> dotnet-ef 10.0.0 restored
dotnet ef migrations list --project src/OpenBar.Infrastructure --startup-project tools/OpenBar.DesignTime
                                             -> 20261116070957_InitialPersistence (pending before first run, applied afterwards)
```

## Persistence tooling notes

### EF Core packages and design-time isolation
- `Microsoft.EntityFrameworkCore`, `Microsoft.EntityFrameworkCore.Sqlite` and `Microsoft.Data.Sqlite` are **10.0.0** and are referenced by `OpenBar.Infrastructure`.
- `Microsoft.EntityFrameworkCore.Design` is needed only to generate migrations. It is referenced exclusively by `tools/OpenBar.DesignTime` (`PrivateAssets=all`, `IncludeAssets` limited to build/runtime assets). That project is a self-contained `win-x64` executable and is **intentionally excluded from `OpenBar.sln`**, so the production build and its vulnerability report do not include the Design package graph.
- `dotnet-ef` is a **local tool** pinned to **10.0.0** (`rollForward: false`) in the repo-root `dotnet-tools.json`; restore it with `dotnet tool restore`.
- EF Core 10 creates its own `__EFMigrationsLock` table at runtime. It is expected; the application's own `openbar.db.migration.lock` file adds cross-process serialization around the whole initialization sequence (integrity check, backup, migrations).
- Never call `EnsureCreated()`. Schema changes are migration-only and applied at startup after a pre-migration backup (see `docs/ARCHITECTURE.md`).

## Widget runtime dependencies

- No new third-party package is required by Foundation 0.3. The runtime uses `Microsoft.Extensions.Hosting.Abstractions` (10.0.0) for `IHostedService`, the existing DI/logging packages, `System.Text.Json` and `PeriodicTimer`.
- `OpenBar.Widgets.Clock` uses the already-verified `Microsoft.WindowsAppSDK` **2.6.5** and `Microsoft.Windows.SDK.BuildTools` **10.0.26100.4654** for its WinUI view; it adds no new version families.
- `OpenBar.WidgetRuntime.Tests` uses the existing xUnit/Test SDK/coverlet versions and the real SQLite provider (`Microsoft.Data.Sqlite`) for persistence tests.
- The solution grew from 14 to **19 projects**: the `OpenBar.Widgets.Clock` module, `OpenBar.SystemBridge`, the `OpenBar.Widgets.System` module, the `OpenBar.WidgetRuntime.Tests` project and the `OpenBar.Dock.Tests` project. All are in `OpenBar.sln`; the tooling-only `tools/OpenBar.DesignTime` remains outside it.
- Foundation 0.4 adds **no new third-party package**. `OpenBar.Dock` was reduced to a plain class library (Application reference + existing DI/logging packages) so dock geometry/lifecycle stay testable without WinUI. `OpenBar.Presentation` adds only the already-centralized `Microsoft.Extensions.Logging.Abstractions`; icon loading uses `Windows.Storage`/`StorageItemThumbnail` from the Windows App SDK projection.
- Foundation 0.5 adds **no new package** either. Light/dark resources, proximity magnification, fullscreen detection and the `VisualPolish` migration use the existing Windows App SDK, `Windows.UI.ViewManagement`, Win32 shell APIs and EF Core stack. The theme/fullscreen/DPI work does not add a dependency.
- Foundation 0.6 adds **no new package**. `OpenBar.SystemBridge` uses only `Microsoft.Extensions.Logging`, the BCL and `PeriodicTimer`; the Windows source is a `DllImport` wrapper over `kernel32.dll` (`GetSystemTimes`, `GlobalMemoryStatusEx`). `OpenBar.Widgets.System` reuses the already-centralized Windows App SDK and BuildTools packages for its WinUI view.
- `dotnet list OpenBar.sln package --vulnerable --include-transitive` reports no vulnerable packages for any of the 19 projects.
- The migration sequence is now `InitialPersistence` → `DockUi` → `VisualPolish`; the last one adds theme, corner radius, auto-hide delays, reduced-motion and fullscreen columns with safe defaults.

## Notable dependency findings

### Microsoft.Windows.SDK.BuildTools
- Windows App SDK 2.6.5 → `Microsoft.WindowsAppSDK.Base` requires `Microsoft.Windows.SDK.BuildTools >= 10.0.26100.4654`.
- 10.0.26100.0 caused NU1605 (downgrade treated as error). Pinned to **10.0.26100.4654**.

### SQLitePCLRaw (CVE-2025-6965 / GHSA-2m69-gcr7-jv3q)
- EF Core Sqlite 10.0.0 floors `SQLitePCLRaw.lib.e_sqlite3` at 2.1.11, which bundles vulnerable SQLite (< 3.50.2).
- No patched 2.1.11; the 2.1.x fix line is 2.1.12+. A `PackageVersion` alone does not move a transitive-only package, so `OpenBar.Infrastructure` now has a direct `PackageReference` to `SQLitePCLRaw.bundle_e_sqlite3`, centrally pinned to **2.1.14**.
- Deliberately stayed on the 2.1.x line (3.x changes assembly identity to `9.x`/major bump); revisit when EF Core ships a floor on 2.1.12+.
- Verified at runtime by `SqliteRuntimeTests` (in-memory DB + `sqlite_version()` ≥ 3.50.2).

## Environment
- OS: Windows 11 26H2, build 26300.
- .NET SDK: 10.0.404 (resolved by `global.json`) + 11.0.100.
- Visual Studio: Community 2026 Insiders 18.13.12409.143.
- Windows SDK: 10.0.26100 (Kits root + include present).
