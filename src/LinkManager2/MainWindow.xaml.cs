using System;
using System.Runtime.InteropServices;
using System.Text;
using LinkManager2.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using WinRT.Interop;

namespace LinkManager2;

public sealed partial class MainWindow : Window
{
    public Frame RootFrame => RootFrameControl;

    public XamlRoot? ContentXamlRoot => RootFrameControl.XamlRoot;

    public RelayCommand ShowFromTrayCommand { get; }
    public RelayCommand ExitCommand { get; }

    public string? HotkeyWarning { get; private set; }

    private readonly CommandLayerController _commandLayer;
    private GlobalHotkey? _hotkey;
    private bool _exitRequested;
    private bool _tornDown;
    private bool _hooksInstalled;
    private LocalPreferences _prefs;

    public MainWindow()
    {
        ShowFromTrayCommand = new RelayCommand(ShowFromTray);
        ExitCommand = new RelayCommand(() => { _exitRequested = true; TearDownAndExit(); });
        _commandLayer = new CommandLayerController(this);
        InitializeComponent();
        _prefs = LocalPreferences.Load();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        ApplyBackdrop();

        Closed += OnClosed;
        AppWindow.Closing += OnAppWindowClosing;

        RootFrameControl.Navigate(typeof(LoginPage), null, new SuppressNavigationTransitionInfo());
    }

    public void NavigateTo(Type pageType, object? parameter = null)
    {
        RootFrameControl.Navigate(pageType, parameter, new EntranceNavigationTransitionInfo());
        RootFrameControl.BackStack.Clear();
    }

    public void ApplyBackdrop() => SystemBackdrop = _prefs.MicaBackdrop ? new MicaBackdrop() : null;

    public void ReloadPreferences()
    {
        _prefs = LocalPreferences.Load();
        ApplyBackdrop();
        if (_prefs.GlobalHotkeyEnabled && _hotkey is null) InstallHotkey();
        else if (!_prefs.GlobalHotkeyEnabled && _hotkey is not null) UninstallHotkey();
        if (_hooksInstalled) SetTrayVisible(_prefs.MinimizeToTrayOnClose);
    }

    public void InstallSystemHooks()
    {
        _hooksInstalled = true;
        if (_prefs.MinimizeToTrayOnClose) SetTrayVisible(true);
        if (_prefs.GlobalHotkeyEnabled) InstallHotkey();
    }

    public void UninstallSystemHooks()
    {
        _hooksInstalled = false;
        SetTrayVisible(false);
        UninstallHotkey();
    }

    public void SetStatus(string text)
    {
        if (RootFrameControl.Content is MainPage mp) mp.SetExternalStatus(text);
    }

    public void ShowFromTray()
    {

        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter
            { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized } p)
        {
            p.Restore();
        }
        AppWindow.Show();
        Activate();
        SetForegroundWindow(WindowNative.GetWindowHandle(this));
    }

    private void SetTrayVisible(bool visible)
    {
        try
        {
            TrayIcon.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (visible)
            {
                RenameTrayHelperWindow();
                DispatcherQueue.TryEnqueue(RenameTrayHelperWindow);
            }
        }
        catch {  }
    }

    private void RenameTrayHelperWindow()
    {
        EnumThreadWindows(GetCurrentThreadId(), (hWnd, _) =>
        {
            var sb = new StringBuilder(64);
            if (GetWindowText(hWnd, sb, sb.Capacity) > 0
                && sb.ToString().StartsWith("H.NotifyIcon", StringComparison.Ordinal))
            {
                SetWindowText(hWnd, "LinkManager");
            }
            return true;
        }, IntPtr.Zero);
    }

    private void InstallHotkey()
    {
        if (_hotkey is not null) return;
        _hotkey = new GlobalHotkey(this);
        _hotkey.Pressed += (_, _) => _commandLayer.Show();
        HotkeyWarning = _hotkey.IsRegistered
            ? null
            : "Aviso: no se pudo registrar el atajo Ctrl+Shift+L (otra app lo usa).";
    }

    private void UninstallHotkey()
    {
        _hotkey?.Dispose();
        _hotkey = null;
        HotkeyWarning = null;
    }

    private void TearDownAndExit()
    {
        if (_tornDown) return;
        _tornDown = true;
        try { App.State?.Cache.Dispose(); } catch {  }
        try { UninstallHotkey(); } catch {  }
        try { TrayIcon.Dispose(); } catch {  }
        try { SupabaseClientHolder.Shutdown(); } catch {  }
        Environment.Exit(0);
    }

    private void OnAppWindowClosing(Microsoft.UI.Windowing.AppWindow sender,
        Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        if (_exitRequested || !_hooksInstalled || !_prefs.MinimizeToTrayOnClose) return;
        args.Cancel = true;
        AppWindow.Hide();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        TearDownAndExit();
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private delegate bool EnumThreadDelegate(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumThreadWindows(uint dwThreadId, EnumThreadDelegate lpfn, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetWindowText(IntPtr hWnd, string lpString);
}
