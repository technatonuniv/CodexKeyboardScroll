# Changelog

All notable changes to Codex Keyboard Scroll are documented here.

## [Unreleased]

### Added

- Added 18 embedded interface languages with automatic Windows-language selection, immediate switching, English fallback, and completeness validation.
- Added modern multi-size application icons with visually distinct active and waiting tray states.
- Added optional current-user Windows startup registration, disabled by default.

### Changed

- Rebuilt the tray menu with larger typography, high-DPI support, modern spacing, colors, and state indicators.
- Replaced the three scroll-speed presets with a monotonic 1–10 scale that defaults to level 5 and migrates existing settings.
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

[1.4.1]: https://github.com/technatonuniv/CodexKeyboardScroll/compare/v1.4.0...v1.4.1
[1.4.0]: https://github.com/technatonuniv/CodexKeyboardScroll/compare/v1.3.0...v1.4.0
[1.3.0]: https://github.com/technatonuniv/CodexKeyboardScroll/releases/tag/v1.3.0
