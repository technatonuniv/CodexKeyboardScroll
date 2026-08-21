# Contributing

Thank you for improving Codex Keyboard Scroll.

## Before opening a change

- Keep the utility compatible with Windows 10 and .NET Framework 4.8.
- Preserve the portable, single-executable distribution model.
- Avoid adding background services, administrator requirements, telemetry, or startup behavior that is not explicitly user-controlled.
- Keep UI text, code comments, commit messages, and documentation in English.
- Add comments only where they explain a non-obvious constraint, workaround, or design decision.

## Localization

Language packs live in `Localization/Resources` and are embedded in the executable. Add a single UTF-8 `.lang` file whose name is a BCP 47 language code; no application-code change is required. Every pack must define every `UiText` key and preserve the format placeholders used by the English pack. The self-tests enforce both rules.

## Build and test

Run the release build from PowerShell:

```powershell
.\build.ps1
```

The build treats compiler warnings as errors and runs the built-in self-tests. For input or focus changes, also verify the behavior manually in the current Codex desktop app on Windows 10.

## Pull requests

Keep each pull request focused. Describe:

- the user-visible problem;
- the chosen approach and its tradeoffs;
- automated and manual verification performed;
- any Codex interface assumptions that may need future adjustment.

Do not include generated executables in commits. Release binaries are published only through GitHub Releases.
