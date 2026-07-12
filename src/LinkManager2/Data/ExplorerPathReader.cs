using System.Runtime.InteropServices;

namespace LinkManager2.Data;

public static class ExplorerPathReader
{

    public static string? GetSelectedPath(IntPtr hwnd = default)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return null;
            dynamic? shell = Activator.CreateInstance(shellType);
            if (shell is null) return null;

            var activeHwnd = hwnd == IntPtr.Zero ? GetForegroundWindow() : hwnd;

            foreach (var window in shell.Windows())
            {
                if ((IntPtr)(long)window.HWND == activeHwnd)
                {
                    var selection = window.Document.SelectedItems();
                    if (selection.Count > 0) return selection.Item(0).Path;
                    return window.Document.Folder.Self.Path;
                }
            }

            if (activeHwnd == GetShellWindow())
            {
                var desktop = shell.Namespace(0);
                var selection = desktop.SelectedItems();
                if (selection.Count > 0) return selection.Item(0).Path;
                return desktop.Self.Path;
            }
        }
        catch (Exception ex) { Diagnostics.Log("explorer path capture", ex); }
        return null;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
}
