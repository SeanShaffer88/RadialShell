# RadialShell

Windows Explorer radial quick-action utility.

## Target resolution

The target policy handles selected items, an unselected item under the pointer, and blank file-view space (the current folder). Windows resolution uses Shell automation and Microsoft Active Accessibility (MSAA). Non-file-view Explorer controls are filtered in the low-level hook so their normal right-click behavior remains intact.

## Active radial wheel

Run on Windows with an interactive desktop:

```powershell
RadialShell.exe --run
```

Alt + Right-Click a file, folder, or blank area in an Explorer/desktop file view. The native context menu is suppressed for that gesture; once the target is resolved, the wheel appears at the pointer. Hover a slice and left-click to run it; Escape or right-click in the wheel dismisses it. If Shell target resolution fails after the gesture is consumed, the app logs the reason and does not show a wheel.

The six built-in actions are **Copy Path**, **Copy Name**, **Open Folder**, **Terminal Here**, **POSIX Path**, and **Copy Contents**. Copy Contents accepts text-like extensions only and has size limits (2 MiB per file, 5 MiB total). Clipboard and process-launch actions run only after a slice is explicitly clicked.

## Themes and preview

Eight built-in palettes are available: **Midnight, Neon Circuit, Frost, Sunset, Emerald, Paper, Ocean,** and **Orchid**. The circular menu uses wedge separation, a center hub, contrasting labels, and hover feedback. Theme preference is saved in `%APPDATA%\RadialShell\settings.json`.

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

`--self-test` checks target-policy precedence, theme and action registration, and the Win32 `GetCurrentThreadId` entry point. `--listen` is passive: it logs resolved targets without suppressing native right-click. Live Explorer behavior still needs validation on each supported Windows build and view mode.

## Build and download

The `Windows executable` GitHub Actions workflow builds, tests, publishes, and uploads a self-contained `win-x64` executable on pushes to the active development branch. Download the `RadialShell-win-x64` artifact from the successful workflow run. To build locally with the .NET 8 SDK on Windows:

```powershell
dotnet build .\RadialShell.sln --configuration Release
dotnet run --project .\tests\RadialShell.Core.Tests\RadialShell.Core.Tests.csproj --configuration Release
dotnet publish .\src\RadialShell.App\RadialShell.App.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --output .\artifacts\RadialShell-win-x64
```
