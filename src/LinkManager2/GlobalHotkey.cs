using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace LinkManager2;

internal sealed class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_NOREPEAT = 0x4000;
    private const uint VK_L = 0x4C;
    private const int HotkeyId = 0x4C4D;

    public event EventHandler? Pressed;

    public bool IsRegistered { get; private set; }

    private readonly IntPtr _hwnd;
    private readonly Window _window;
    private readonly SUBCLASSPROC _subclass;

    public GlobalHotkey(Window window)
    {
        _window = window;
        _hwnd = WindowNative.GetWindowHandle(window);
        _subclass = SubclassProc;
        SetWindowSubclass(_hwnd, _subclass, HotkeyId, IntPtr.Zero);
        IsRegistered = RegisterHotKey(_hwnd, HotkeyId, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_L);
    }

    public void Dispose()
    {
        if (IsRegistered) UnregisterHotKey(_hwnd, HotkeyId);
        RemoveWindowSubclass(_hwnd, _subclass, HotkeyId);
        IsRegistered = false;
    }

    private IntPtr SubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (uMsg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {

            _window.DispatcherQueue.TryEnqueue(() => Pressed?.Invoke(this, EventArgs.Empty));
        }
        return DefSubclassProc(hWnd, uMsg, wParam, lParam);
    }

    private delegate IntPtr SUBCLASSPROC(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("comctl32.dll", CharSet = CharSet.Auto)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll", CharSet = CharSet.Auto)]
    private static extern bool RemoveWindowSubclass(IntPtr hWnd, SUBCLASSPROC pfnSubclass, IntPtr uIdSubclass);

    [DllImport("comctl32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);
}
