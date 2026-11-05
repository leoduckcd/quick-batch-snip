# Quick Batch Snip

A portable Windows utility for capturing several screen regions and pasting them together.

1. Run `QuickBatchSnip.exe`.
2. Press **Win+S** and drag to select an area of the frozen screen. Press **Esc** to cancel. Repeat to queue more screenshots.
3. Press **Ctrl+V** in the destination app. Apps that accept clipboard attachments receive separate PNGs. For apps that accept only one image, choose **Paste batch as one image** in the tray menu.

Captures stay in memory until paste. The queue clears when an app reads the batch from the clipboard. Temporary attachment files are cleaned up after 10 minutes or when Quick Batch Snip exits.

Use **Start with Windows** in the main window to launch in the tray at sign-in. Double-click the EXE to reopen the window while the utility is running. Quick Batch Snip uses **Win+S** while it runs.

## Build

On Windows 10 or 11, run this from the repository root in PowerShell. The script uses the C# compiler included with .NET Framework.

```powershell
.\build.ps1
```

The executable is written to `build/QuickBatchSnip.exe`.
