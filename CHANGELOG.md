# Changelog

All notable changes to Codex Keyboard Scroll are documented here.

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

[1.4.0]: https://github.com/technatonuniv/CodexKeyboardScroll/compare/v1.3.0...v1.4.0
[1.3.0]: https://github.com/technatonuniv/CodexKeyboardScroll/releases/tag/v1.3.0
