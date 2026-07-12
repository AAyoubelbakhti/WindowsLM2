using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace LinkManager2.Data;

/// <summary>
/// Builds the taskbar jump list for the unpackaged app via the native ICustomDestinationList
/// COM API. Each task relaunches this executable with a startup arg the single-instance
/// redirector routes. Windows.UI.StartScreen.JumpList is unavailable without package identity,
/// hence the manual interop. Failures are logged and swallowed so startup is never blocked.
/// </summary>
public static class TaskbarJumpList
{
    public static void Configure()
    {
        var exe = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exe)) return;

        ICustomDestinationList? list = null;
        IObjectCollection? tasks = null;
        try
        {
            list = (ICustomDestinationList)new DestinationList();
            list.BeginList(out _, typeof(IObjectArray).GUID, out _);

            tasks = (IObjectCollection)new EnumerableObjectCollection();
            AddTask(tasks, exe, Startup.AddArg, "Añadir enlace", "Guarda un enlace nuevo.");
            AddTask(tasks, exe, Startup.SearchArg, "Buscar", "Abre la búsqueda de enlaces.");
            AddTask(tasks, exe, Startup.SaveClipboardArg, "Guardar portapapeles",
                "Guarda el enlace o la ruta del portapapeles.");

            list.AddUserTasks((IObjectArray)tasks);
            list.CommitList();
        }
        catch (Exception ex) { Diagnostics.Log("jumplist configure", ex); }
        finally
        {
            if (tasks is not null) Marshal.ReleaseComObject(tasks);
            if (list is not null) Marshal.ReleaseComObject(list);
        }
    }

    private static void AddTask(IObjectCollection tasks, string exe, string arg, string title, string description)
    {
        IShellLinkW? link = null;
        try
        {
            link = (IShellLinkW)new ShellLink();
            link.SetPath(exe);
            link.SetArguments(arg);
            link.SetIconLocation(exe, 0);
            link.SetDescription(description);

            var store = (IPropertyStore)link;
            using var titleValue = new PropVariant(title);
            store.SetValue(PkeyTitle, titleValue.Reference);
            store.Commit();

            tasks.AddObject(link);
        }
        finally
        {
            if (link is not null) Marshal.ReleaseComObject(link);
        }
    }

    private static readonly PropertyKey PkeyTitle =
        new(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FmtId;
        public int Pid;
        public PropertyKey(Guid fmtId, int pid) { FmtId = fmtId; Pid = pid; }
    }

    private sealed class PropVariant : IDisposable
    {
        private IntPtr _native;
        public PropVariant(string value)
        {
            _native = Marshal.AllocCoTaskMem(16);
            for (var i = 0; i < 16; i++) Marshal.WriteByte(_native, i, 0);
            Marshal.WriteInt16(_native, 0, VT_LPWSTR);
            Marshal.WriteIntPtr(_native, 8, Marshal.StringToCoTaskMemUni(value));
        }

        public IntPtr Reference => _native;

        public void Dispose()
        {
            if (_native == IntPtr.Zero) return;
            var str = Marshal.ReadIntPtr(_native, 8);
            if (str != IntPtr.Zero) Marshal.FreeCoTaskMem(str);
            Marshal.FreeCoTaskMem(_native);
            _native = IntPtr.Zero;
        }

        private const short VT_LPWSTR = 31;
    }

    [ComImport, Guid("77f10cf0-3db5-4966-b520-b7c54fd35ed6")]
    private class DestinationList { }

    [ComImport, Guid("2d3468c1-36a7-43b6-ac24-d3f02fd9607a")]
    private class EnumerableObjectCollection { }

    [ComImport, Guid("00021401-0000-0000-c000-000000000046")]
    private class ShellLink { }

    [ComImport, Guid("6332debf-87b5-4670-90c0-5e57b408a49e"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICustomDestinationList
    {
        void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
        void BeginList(out uint pcMaxSlots, in Guid riid, out IntPtr ppv);
        void AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string pszCategory, IObjectArray poa);
        void AppendKnownCategory(int category);
        void AddUserTasks(IObjectArray poa);
        void CommitList();
        void GetRemovedDestinations(in Guid riid, out IntPtr ppv);
        void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
        void AbortList();
    }

    [ComImport, Guid("92CA9DCD-5622-4bba-A805-5E9F541BD8C9"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectArray
    {
        void GetCount(out uint pcObjects);
        void GetAt(uint uiIndex, in Guid riid, out IntPtr ppv);
    }

    [ComImport, Guid("5632b1a4-e38a-400a-928a-d4cd63230295"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectCollection
    {
        void GetCount(out uint pcObjects);
        void GetAt(uint uiIndex, in Guid riid, out IntPtr ppv);
        void AddObject([MarshalAs(UnmanagedType.IUnknown)] object punk);
        void AddFromArray(IObjectArray poaSource);
        void RemoveObjectAt(uint uiIndex);
        void Clear();
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile,
            int cch, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath,
            int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PropertyKey pkey);
        void GetValue(in PropertyKey key, IntPtr pv);
        void SetValue(in PropertyKey key, IntPtr propvar);
        void Commit();
    }
}
