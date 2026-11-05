using System;
using System.IO;
using Microsoft.Win32;

internal sealed class WindowsStartup
{
    internal const string Argument = "--startup";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "QuickBatchSnip";
    private readonly string command;
    private readonly string registryPath;

    internal WindowsStartup(string exePath, string registryPath = RunKey)
    {
        command = "\"" + exePath + "\" " + Argument;
        this.registryPath = registryPath;
    }

    internal string ReadCommand()
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(registryPath))
        {
            return key == null ? null :
                key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }
    }

    internal bool UsesThisCopy(string registeredCommand)
    {
        return string.Equals(command, registeredCommand, StringComparison.OrdinalIgnoreCase);
    }

    internal void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            if (command.Length > 260)
                throw new PathTooLongException();
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(registryPath))
            {
                if (key == null)
                    throw new IOException();
                key.SetValue(ValueName, command, RegistryValueKind.String);
            }
        }
        else
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(registryPath, true))
                if (key != null)
                    key.DeleteValue(ValueName, false);
        }
    }
}
