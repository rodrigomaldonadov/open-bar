# Changelog

## 0.5.0 — Visual polish and desktop experience (2026-11-16)

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
