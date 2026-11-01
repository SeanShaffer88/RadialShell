# RadialShell

Windows Explorer radial quick-action utility.

## Target resolution behavior

The action wheel must work from Explorer file views and the Desktop, including icon, list, and details layouts. Resolve the target from the pointer position and the shell view state:

1. **Pointer over a selected item:** target the complete current selection (multi-selection).
2. **Pointer over an unselected item:** target that item, rather than an unrelated selection.
3. **Pointer over empty space in a file view:** target the current folder.
4. **Desktop:** apply the same item/selection/empty-space rules, with empty space targeting the Desktop folder.

The resolver should return a structured target (item(s) or folder, plus the source context), so actions can distinguish files from folders and handle each correctly. If the pointer is over a non-file-view control or target resolution fails, do not show the wheel or swallow the normal input event; pass the event through.

## First milestone: target-resolution proof of concept

Build a Windows-only proof of concept that detects the configured gesture and reports the resolved target. Validate all four cases above, including multi-selection, across Explorer views and the Desktop. Keep shell/COM discovery out of the low-level hook callback; the callback should do only the time-critical trigger and pass-through/suppress decision.

The radial UI and action execution come after this behavior is verified.

## Current implementation

The repository now has a platform-independent target model and resolution policy in `src/RadialShell.Core`. Its executable smoke tests cover selected-item multi-selection, an unselected hovered item, blank folder-view space, Desktop blank space, and rejection outside a folder view. Windows hook and shell-view discovery are not implemented yet.

With the .NET 8 SDK installed, run the smoke tests with:

```powershell
dotnet run --project .\tests\RadialShell.Core.Tests\RadialShell.Core.Tests.csproj
```
