using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace LinkManager2.Data;

public static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LinkManager";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is not null;
            }
            catch { return false; }
        }
    }

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key is null) return;
            if (enabled)
            {
                var exe = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(exe)) key.SetValue(ValueName, $"\"{exe}\" {Startup.TrayArg}");
            }
            else
            {
                if (key.GetValue(ValueName) is not null) key.DeleteValue(ValueName, false);
            }
        }
        catch (Exception ex) { Diagnostics.Log("startup toggle", ex); }
    }
}
