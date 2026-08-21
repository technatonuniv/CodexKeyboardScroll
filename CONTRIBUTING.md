# Contributing

Thank you for improving Codex Keyboard Scroll.

## Before opening a change

- Keep the utility compatible with Windows 10 and .NET Framework 4.8.
- Preserve the portable, single-executable distribution model.
- Avoid adding background services, administrator requirements, telemetry, or automatic startup.
- Keep UI text, code comments, commit messages, and documentation in English.
- Add comments only where they explain a non-obvious constraint, workaround, or design decision.

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
