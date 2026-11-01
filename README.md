# RadialShell

Windows Explorer radial quick-action utility.

## Target resolution behavior

The action wheel is intended to work from Explorer file views and the Desktop, including icon, list, and details layouts. Resolve the target from the pointer position and shell-view state:

1. **Pointer over a selected item:** target the complete current selection (multi-selection).
2. **Pointer over an unselected item:** target that item, rather than an unrelated selection.
3. **Pointer over empty space in a file view:** target the current folder.
4. **Desktop:** apply the same item/selection/empty-space rules, with empty space targeting the Desktop folder.

Non-file-view controls and unresolved targets must pass through normally. The policy is implemented in `src/RadialShell.Core`; Windows-specific discovery currently uses Shell automation and Microsoft Active Accessibility (MSAA).

## M1 diagnostic prototype

`RadialShell.App` is a Windows console diagnostic, not the radial menu yet. It observes **Alt + Right-Click** over Explorer/Desktop, resolves a target on a separate STA worker, and prints the paths. It deliberately does **not** suppress the click or native context menu while target discovery is being validated.

Run it on Windows:

```powershell
RadialShell.exe --self-test
RadialShell.exe --listen
```

`--self-test` runs policy checks without installing a hook. `--listen` requires an interactive Windows desktop and reports targets as you test the gesture. Shell accessibility varies across Windows versions and Explorer layouts; this prototype is a validation tool, not a claim that every view has been verified.

## Build and download

The `Windows executable` GitHub Actions workflow builds and smoke-tests a self-contained `win-x64` executable on pushes to this branch. Download the `RadialShell-win-x64` artifact from the successful workflow run. To build locally with the .NET 8 SDK on Windows:

```powershell
dotnet build .\RadialShell.sln --configuration Release
dotnet run --project .\tests\RadialShell.Core.Tests\RadialShell.Core.Tests.csproj --configuration Release
dotnet publish .\src\RadialShell.App\RadialShell.App.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true --output .\artifacts\RadialShell-win-x64
```

The radial UI and action execution are subsequent milestones.
