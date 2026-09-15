using Microsoft.Win32;

namespace NetworkMonitor.Services;

internal static class StartupManager
{
    private const string ValueName = "NetworkMonitor";
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (key is null)
            return;

        if (enabled)
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(path))
                key.SetValue(ValueName, $"\"{path}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
