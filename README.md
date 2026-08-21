# Codex Keyboard Scroll

An unofficial Windows keyboard navigation and focus utility for the Codex desktop app. It restores transcript scrolling and adds configurable controls for switching between the conversation transcript and composer.

The project was originally created in response to [openai/codex#39851](https://github.com/openai/codex/issues/39851). Its focus switching, reading mode, typing handoff, and navigation settings remain useful independently of that upstream regression.

## Download

Download the latest executable and `SHA256SUMS.txt` from [GitHub Releases](https://github.com/technatonuniv/CodexKeyboardScroll/releases/latest).

The executable is unsigned. Review the source, verify the SHA-256 checksum, or build it locally before running it.

## Features

- Restores transcript scrolling with `Up`, `Down`, `Page Up`, and `Page Down`.
- Uses `Space` and `Shift+Space` for page scrolling by default.
- Switches between the transcript and composer with one configurable shortcut.
- Automatically returns focus to the composer when a letter, number, or punctuation key is pressed in reading mode, while preserving the first typed character.
- Locates the composer through Windows UI Automation, with a coordinate-based fallback.
- Sends mouse-wheel input to the transcript without moving the user's pointer.
- Automatically retries hotkey registration after temporary shortcut conflicts.
- Provides a modern, high-DPI tray menu with a 1–10 scroll-speed scale.
- Includes 18 built-in interface languages and follows the Windows display language by default.
- Uses distinct active and waiting tray icons so the current capture state is visible at a glance.
- Can optionally start with Windows; this is disabled by default.
- Stores settings in a portable INI file next to the executable.

## Requirements

- Windows 10
- ChatGPT/Codex desktop app
- .NET Framework 4.8

The current build is an unsigned local executable. Windows SmartScreen may display a warning when it is downloaded from GitHub.

## Usage

1. Run `CodexKeyboardScroll.exe`.
2. Click the central transcript area or press the configured focus shortcut.
3. Use:

   - `Up` / `Down` — small scroll
   - `Page Up` / `Page Down` — page scroll
   - `Space` — page down
   - `Shift+Space` — page up

4. Press the focus shortcut again to return to the composer.
5. Typing a letter, number, or punctuation mark in reading mode automatically focuses the composer and inserts that same character.

The default focus shortcut is `Alt+F`. Available alternatives are `Ctrl+Alt+F` and `Ctrl+Shift+F`.

## Tray menu

The larger high-DPI menu uses modern spacing, colors, and state indicators. Its top line is a compact, read-only summary such as:

```text
Waiting · Alt+F · UIA
```

`Status and diagnostics` contains detailed read-only information about:

- the current mode;
- the registered focus shortcut;
- UI Automation and fallback status;
- the utility version.

Other menu items allow you to:

- enable or disable the utility;
- enable reading mode manually;
- choose the focus shortcut;
- select a scroll speed from 1 (comfortable continuous reading) to 10 (very fast), with 5 as the default;
- choose whether `Space` scrolls the transcript or starts typing;
- choose the interface language or follow the Windows display language;
- optionally start the utility with Windows;
- exit the utility.

Double-clicking the tray icon only displays the current enabled/disabled state. It does not toggle the utility.

The tray icon is vivid blue while reading mode is capturing navigation keys and muted gray while the utility is waiting. The executable uses the same modern icon at all standard Windows icon sizes.

## Languages

The interface includes Arabic, Chinese (Simplified and Traditional), Dutch, English, French, German, Hindi, Indonesian, Italian, Japanese, Korean, Polish, Portuguese (Brazil), Russian, Spanish, Turkish, and Ukrainian.

`System default` follows the current Windows display language and falls back to English when no matching language pack is available. Language changes are applied immediately without restarting the utility.

## Settings

Settings are stored in `CodexKeyboardScroll.settings.ini` next to the executable:

```ini
FocusShortcut=AltF
ScrollSpeed=5
SpaceScroll=True
Language=system
```

Changes made through the tray menu are applied immediately and saved atomically.

The optional startup setting is stored in the current user's standard Windows `Run` registry key instead of the portable INI file. Enabling it requires no administrator privileges, and disabling it removes the entry. The utility never enables startup by itself.

## Focus and scrolling behavior

The utility refreshes the Codex accessibility tree on a background worker and caches the composer. Composer focus is verified after every UI Automation focus request. If accessibility lookup is unavailable or the interface changes, a coordinate-based click is used as a fallback.

Scrolling deliberately uses mouse-wheel input. The utility sends `WM_MOUSEWHEEL` directly to the Codex child window at the transcript coordinates, so the system pointer never moves or flickers.

Manual click classification still uses the click position. If an unusual window layout is not classified correctly, use the focus shortcut or enable reading mode from the tray menu.

## Hotkey recovery

If all supported focus shortcuts are temporarily occupied, registration is retried every two seconds. Error notifications are limited to one every 30 seconds. The utility recovers automatically after a shortcut becomes available; restarting is not required.

## Privacy and security

- The utility does not modify Codex application files or Windows system settings.
- It adds a current-user startup entry only when the user explicitly enables `Start with Windows`.
- It does not require administrator privileges.
- It installs a low-level keyboard hook to detect the first typing key in reading mode. Key events are not logged, stored, or transmitted.
- Scrolling and focus behavior are activated only while `ChatGPT.exe` or `Codex.exe` is the foreground process.
- The source code is included so the executable can be reviewed and rebuilt locally.

## Build from source

The repository includes a classic .NET Framework project and a build script that does not require a separate .NET SDK installation. From PowerShell:

```powershell
.\build.ps1
```

The script compiles with warnings treated as errors, runs the built-in self-tests, and writes the executable to `artifacts\CodexKeyboardScroll.exe`. It also prints the SHA-256 hash.

Visual Studio Build Tools users can build the project directly:

```powershell
msbuild .\CodexKeyboardScroll.csproj /p:Configuration=Release /p:Platform=x64
```

GitHub Actions runs the same build script for every pull request and every push to `main`.

## Project structure

- `App` — application lifecycle, tray menu, and reading-mode coordination.
- `Automation` — background UI Automation lookup and verified composer focus.
- `Configuration` — portable settings loading and atomic persistence.
- `Domain` — commands, settings values, and scroll profiles.
- `Input` — hotkeys, keyboard hook, Win32 input, and window-layout heuristics.
- `Localization` — embedded language packs, culture resolution, and English fallback.
- `Platform` — isolated Windows integrations such as optional current-user startup.
- `Presentation` — the modern tray menu, renderer, and icon resources.
- `Assets` — transparent source artwork and multi-size active/waiting icons.
- `Tests` — dependency-free self-tests that run against the release executable.

See [CHANGELOG.md](CHANGELOG.md) for release history and [CONTRIBUTING.md](CONTRIBUTING.md) before submitting a change.

## Removal

If `Start with Windows` is enabled, turn it off first so the utility removes its startup entry. Then choose `Exit` and delete the utility directory.

## Project status

This is an independent community utility. Future Codex updates may allow individual compatibility paths to be simplified or removed, while the broader keyboard focus and navigation features can continue to evolve.

This project is not affiliated with or endorsed by OpenAI.

## License

[MIT](LICENSE)
