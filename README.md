# RadialShell

Windows Explorer radial quick-action utility.

## Target resolution

The target policy handles selected items, an unselected item under the pointer, and blank file-view space (the current folder). Windows resolution uses Shell automation, UI Automation for Explorer's virtualized item rows, and MSAA as a fallback. Non-file-view Explorer controls are filtered in the low-level hook so their normal right-click behavior remains intact. When Windows exposes only the generic `Items View` container, the resolver searches UI Automation descendants whose bounds contain the cursor and matches their names against canonical Shell items. It also handles Explorer views that hide file extensions or expose rows as generic/custom controls before falling back to the current folder.

## Active radial wheel

Run on Windows with an interactive desktop:

```powershell
RadialShell.exe --run
```

Alt + Right-Click a file, folder, or blank area in an Explorer/Desktop file view. The native context menu is suppressed for that gesture. The wheel appears at the pointer once the shell view is identified; UI Automation resolves virtualized rows, and unresolved item metadata falls back to the current folder with a console log. Hover a slice and left-click to run it; the wheel closes immediately after any action is chosen. Action errors are written to the console before closing. Escape, right-click in the wheel, or a 15-second idle timeout also dismisses it. The overlay is a topmost, no-activate tool window painted opaquely (no layered-window alpha dependency) and uses Per-Monitor V2 DPI awareness. If the shell window or current folder cannot be resolved, the app logs the failure and does not show a wheel.

The six built-in actions are **Copy Path**, **Copy Name**, **Open Folder**, **Terminal Here**, **POSIX Path**, and **Copy Contents**. Copy Contents accepts text-like extensions only and has size limits (2 MiB per file, 5 MiB total). Clipboard and process-launch actions run only after a slice is explicitly clicked.

## Themes and preview

Eight built-in palettes are available: **Midnight, Neon Circuit, Frost, Sunset, Emerald, Paper, Ocean,** and **Orchid**. The circular menu uses wedge separation, a center hub, contrasting labels, and hover feedback. Theme preference is saved as human-readable JSON in the executable's adjacent `data\settings.json`, keeping portable state within the application folder. The configuration includes an integer `schema_version`; the loader explicitly migrates the prior unversioned theme file and leaves malformed or unsupported future versions untouched.

```powershell
RadialShell.exe --themes
RadialShell.exe --preview
RadialShell.exe --preview Frost
RadialShell.exe --theme "Neon Circuit"
```

In preview mode, use **Left/Right** to browse and save palettes; hover a slice to see its state and press **Esc** to close. Preview clicks do not execute actions.

## Diagnostic modes

```powershell
RadialShell.exe --self-test
RadialShell.exe --listen
```

`--self-test` checks target-policy precedence, theme and action registration, logging-level parsing, and the Win32 `GetCurrentThreadId` entry point. `--listen` is passive: it reports resolved targets without suppressing native right-click. Live Explorer behavior still needs validation on each supported Windows build and view mode.

Operational diagnostics are written as JSON Lines to `data\logs\radialshell.jsonl` beside the executable, with three 2 MiB rotated backups. The bounded queue keeps logging off the hook/UI caller path. Default verbosity is `info`; use `--log-level error|warn|info|debug|trace`, or the `--verbose`, `--debug`, and `--trace` shortcuts. Log records include UTC timestamp, level, event name, and message. Logging is local-only.

## Build and download

The `Windows executable` GitHub Actions workflow builds, tests, publishes, and uploads a self-contained `win-x64` executable plus a SHA-256 checksum on pushes to the active development branch. Download the `RadialShell-win-x64` artifact from the successful workflow run and verify the hash with `Get-FileHash .\RadialShell.exe -Algorithm SHA256`. The current build is not code-signed; Windows security software may block or warn on it. Do not disable protection to run it. A checksum confirms download integrity, not that an antivirus detection is harmless; inspect the detection or build from source if it is blocked. To build locally with the .NET 8 SDK on Windows:

```powershell
dotnet build .\RadialShell.sln --configuration Release
dotnet run --project .\tests\RadialShell.Core.Tests\RadialShell.Core.Tests.csproj --configuration Release
dotnet publish .\src\RadialShell.App\RadialShell.App.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --output .\artifacts\RadialShell-win-x64
```
