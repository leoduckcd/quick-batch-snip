using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

internal sealed class WindowsStartup
{
    internal const string Argument = "--startup";
    private const string ShortcutName = "Quick Batch Snip.lnk";
    private const string LegacyRunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string LegacyValueName = "QuickBatchSnip";
    private readonly string exePath;
    private readonly string startupDirectory;
    private readonly string legacyRegistryPath;

    internal WindowsStartup(string exePath, string startupDirectory = null, string legacyRegistryPath = LegacyRunKey)
    {
        this.exePath = exePath;
        this.startupDirectory = startupDirectory;
        this.legacyRegistryPath = legacyRegistryPath;
    }

    private string ShortcutPath
    {
        get
        {
            string folder = startupDirectory ?? Environment.GetFolderPath(
                Environment.SpecialFolder.Startup, Environment.SpecialFolderOption.DoNotVerify);
            if (string.IsNullOrEmpty(folder))
                throw new DirectoryNotFoundException("Windows did not provide a Startup folder.");
            return Path.Combine(folder, ShortcutName);
        }
    }

    internal bool Enabled { get { return File.Exists(ShortcutPath); } }

    internal bool UsesThisCopy()
    {
        return WithShortcut(ShortcutPath, shortcut =>
            string.Equals(exePath, (string)shortcut.TargetPath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Argument, (string)shortcut.Arguments, StringComparison.Ordinal));
    }

    internal bool SetEnabled(bool enabled)
    {
        string path = ShortcutPath;
        if (enabled)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            WithShortcut(path, shortcut =>
            {
                shortcut.TargetPath = exePath;
                shortcut.Arguments = Argument;
                shortcut.WorkingDirectory = Path.GetDirectoryName(exePath);
                shortcut.Description = "Open Quick Batch Snip when you sign in";
                shortcut.Save();
                return true;
            });
            if (!File.Exists(path))
                throw new IOException("Windows did not save the startup shortcut.");
        }
        else
        {
            File.Delete(path);
        }
        return RemoveLegacyEntry();
    }

    private static T WithShortcut<T>(string path, Func<dynamic, T> use)
    {
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
        object shortcut = null;
        try
        {
            shortcut = ((dynamic)shell).CreateShortcut(path);
            return use(shortcut);
        }
        finally
        {
            if (shortcut != null)
                Marshal.ReleaseComObject(shortcut);
            Marshal.ReleaseComObject(shell);
        }
    }

    private bool RemoveLegacyEntry()
    {
        try
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(legacyRegistryPath))
                if (key == null || key.GetValue(LegacyValueName) == null)
                    return true;

            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(legacyRegistryPath, true))
                if (key != null)
                    key.DeleteValue(LegacyValueName, false);
            return true;
        }
        catch (Exception ex)
        {
            if (!(ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException))
                throw;
            return false;
        }
    }
}
