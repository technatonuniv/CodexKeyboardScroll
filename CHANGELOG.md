# Changelog

All notable changes to Codex Keyboard Scroll are documented here.

## [Unreleased]

### Fixed

- Rebuilt update checks around a fresh HTTP client per attempt and added one retry for transient failures.
- Preserved actionable failure categories instead of collapsing every update error into an unexplained message.
- Added tray notifications for user-initiated update checks so progress and results remain visible after the menu closes.
- Normalized two-component release tags before three-component formatting and persistence.

## [2.0.0] - 2026-08-21

### Added

- Added quarter-speed (`0.25`) and half-speed (`0.5`) scrolling with exact ratios relative to level 1.
- Added a clickable version entry that opens the project repository.
- Added manual and opt-in daily GitHub Releases checks with a persistent, clickable update notice.
- Added 18 embedded interface languages with automatic Windows-language selection, immediate switching, English fallback, and completeness validation.
- Added modern multi-size application icons with visually distinct active and waiting tray states.
- Added optional current-user Windows startup registration, disabled by default.

### Changed

- Renamed `Status and diagnostics` to `Service` and grouped diagnostics, version information, and update tools there.
- Removed enabled-state notifications when opening the tray menu.
- Positioned submenus directly against the parent menu and tightened their internal spacing.
- Simplified the `0.25` and `0.5` speed labels to numeric values only.
- Vertically centered menu text and check indicators with explicit high-DPI-aware rendering.
- Rebuilt the tray menu with larger typography, high-DPI support, modern spacing, colors, and state indicators.
- Replaced the three scroll-speed presets with a monotonic 0.25–10 scale that defaults to level 5 and migrates existing settings.
- Separated localization, presentation, application state, and Windows startup integration into independently testable components.

## [1.4.1] - 2026-08-21

### Fixed

- Eliminated pointer movement and flicker during keyboard scrolling by targeting wheel messages directly at the transcript.

## [1.4.0] - 2026-08-21

### Changed

- Split the original single-file implementation into responsibility-based components.
- Simplified transcript scrolling to the reliable mouse-wheel path; UI Automation is now limited to composer discovery and focus.
- Added a classic .NET Framework project, a reproducible PowerShell build, and pull-request CI.
- Expanded dependency-free self-tests for layout classification, typing-key detection, shortcut labels, and scroll profiles.
- Added contributor guidance and documented the source layout.

### Fixed

- Prevented menu accelerators from remaining active after an Alt-based focus shortcut.

### Preserved

- Windows 10 and .NET Framework 4.8 compatibility.
- Configurable focus shortcut, scroll speed, and Space behavior.
- Automatic typing handoff, portable settings, and hotkey recovery.

## [1.3.0] - 2026-08-21

- First public release.

[Unreleased]: https://github.com/technatonuniv/CodexKeyboardScroll/compare/v2.0.0...HEAD
[2.0.0]: https://github.com/technatonuniv/CodexKeyboardScroll/compare/v1.4.1...v2.0.0
[1.4.1]: https://github.com/technatonuniv/CodexKeyboardScroll/compare/v1.4.0...v1.4.1
[1.4.0]: https://github.com/technatonuniv/CodexKeyboardScroll/compare/v1.3.0...v1.4.0
[1.3.0]: https://github.com/technatonuniv/CodexKeyboardScroll/releases/tag/v1.3.0
