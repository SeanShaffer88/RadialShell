# RadialShell

Windows Explorer radial quick-action utility.

## Target resolution behavior

The action wheel is intended to work from Explorer file views and the Desktop, including icon, list, and details layouts. Resolve the target from the pointer position and shell-view state:

1. **Pointer over a selected item:** target the complete current selection (multi-selection).
2. **Pointer over an unselected item:** target that item, rather than an unrelated selection.
3. **Pointer over empty space in a file view:** target the current folder.
4. **Desktop:** apply the same item/selection/empty-space rules, with empty space targeting the Desktop folder.

The platform-independent policy lives in `src/RadialShell.Core`; the Windows diagnostic uses Shell automation and Microsoft Active Accessibility (MSAA). Unresolved/non-file-view targets are reported rather than acted upon.

## Themes and wheel preview

The app includes eight built-in palettes: **Midnight, Neon Circuit, Frost, Sunset, Emerald, Paper, Ocean,** and **Orchid**. The preview uses a segmented circular menu, a center hub, high-contrast labels, and a visible hover state. Theme preference is saved in `%APPDATA%\RadialShell\settings.json`.

```powershell
RadialShell.exe --themes
RadialShell.exe --preview
RadialShell.exe --preview Frost
RadialShell.exe --theme "Neon Circuit"
```

In the preview, use **Left/Right** to browse themes (the chosen palette is saved), hover a slice to highlight it, and press **Esc** to close. Clicking a slice is preview-only; no file action is executed.

## Diagnostic listener

`RadialShell.App` is a Windows console diagnostic, not the integrated radial menu yet. It observes **Alt + Right-Click** over Explorer/Desktop, resolves a target on a separate STA worker, and prints paths. It deliberately does **not** suppress the click or native context menu while target discovery is being validated.

```powershell
RadialShell.exe --self-test
RadialShell.exe --listen
```

`--self-test` checks target precedence, all theme registrations, and the Win32 `GetCurrentThreadId` entry point used to start the global hook. `--listen` requires an interactive Windows desktop. Shell accessibility varies across Windows versions and Explorer layouts; live target resolution still needs manual validation.

## Build and download

The `Windows executable` GitHub Actions workflow builds, tests, publishes, and uploads a self-contained `win-x64` executable on pushes to the active development branch. Download the `RadialShell-win-x64` artifact from the successful workflow run. To build locally with the .NET 8 SDK on Windows:

```powershell
dotnet build .\RadialShell.sln --configuration Release
dotnet run --project .\tests\RadialShell.Core.Tests\RadialShell.Core.Tests.csproj --configuration Release
dotnet publish .\src\RadialShell.App\RadialShell.App.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --output .\artifacts\RadialShell-win-x64
```

The radial menu preview and theme selection are implemented; connecting the wheel to the trigger and implementing its actions are subsequent milestones.
