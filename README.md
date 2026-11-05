# Quick Batch Snip

A portable Windows utility for queuing region and full-screen captures and pasting them together.

1. Run `QuickBatchSnip.exe`.
2. Press **Win+S** to freeze the screen at its original brightness. Drag and release to queue a region, or click **Full screen** once or press **Enter** to queue all displays in one image. Press **Esc** to cancel. Repeat to queue more screenshots.
3. Press **Ctrl+V** in the destination app. Apps that accept clipboard attachments receive separate PNGs. For apps that accept only one image, choose **Paste batch as one image** in the tray menu.

Captures stay in memory until paste. The queue clears when an app reads the batch from the clipboard. Temporary attachment files are cleaned up after 10 minutes or when Quick Batch Snip exits.

Double-click the EXE to reopen the window while the utility is running. Quick Batch Snip uses **Win+S** while it runs. Choose **Exit** in the main window or tray menu, then confirm to stop the app.

## Start with Windows

Turn on **Start with Windows** in the main window to create a shortcut in your Startup folder (`shell:startup`). The shortcut launches the current copy in the tray when you sign in. Turn the switch off to remove it. If you move the EXE, turn the switch off and back on to update the shortcut.

If you upgraded from 1.4, turn the switch on once to replace its old startup entry. Windows also lets you manage startup apps under **Settings → Apps → Startup**.

## Build

On Windows 10 or 11, run this from the repository root in PowerShell. The script uses the C# compiler included with .NET Framework.

```powershell
.\build.ps1
```

The executable is written to `build/QuickBatchSnip.exe`.
