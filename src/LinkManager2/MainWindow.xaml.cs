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
    public RelayCommand AddLinkCommand { get; }
    public RelayCommand SaveClipboardCommand { get; }
    public RelayCommand SearchCommand { get; }

    public string? HotkeyWarning { get; private set; }

    private readonly CommandLayerController _commandLayer;
    private GlobalHotkey? _hotkey;
    private bool _exitRequested;
    private bool _tornDown;
    private bool _hooksInstalled;
    private LocalPreferences _prefs;
    private Windows.Graphics.RectInt32? _lastNormalBounds;

    public MainWindow()
    {
        ShowFromTrayCommand = new RelayCommand(ShowFromTray);
        ExitCommand = new RelayCommand(() => { _exitRequested = true; TearDownAndExit(); });
        _commandLayer = new CommandLayerController(this);
        AddLinkCommand = new RelayCommand(() => _commandLayer.AddLink());
        SaveClipboardCommand = new RelayCommand(() => _commandLayer.SaveClipboard());
        SearchCommand = new RelayCommand(() => _commandLayer.Search());
        InitializeComponent();
        _prefs = LocalPreferences.Load();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        ApplyBackdrop();
        RestoreWindowBounds();

        Closed += OnClosed;
        AppWindow.Closing += OnAppWindowClosing;
        AppWindow.Changed += OnAppWindowChanged;
    }

    /// <summary>
    /// Applies the persisted window size and position. If the saved position no longer
    /// falls on a visible display (monitor unplugged, resolution change) the window is
    /// centered on the nearest display keeping the saved size.
    /// </summary>
    private void RestoreWindowBounds()
    {
        if (_prefs.WindowWidth is not int w || _prefs.WindowHeight is not int h) return;
        if (w < 320 || h < 240) return;

        if (_prefs.WindowX is int x && _prefs.WindowY is int y)
        {
            var rect = new Windows.Graphics.RectInt32(x, y, w, h);
            try
            {
                var area = Microsoft.UI.Windowing.DisplayArea.GetFromRect(
                    rect, Microsoft.UI.Windowing.DisplayAreaFallback.None);
                if (area is not null)
                {
                    AppWindow.MoveAndResize(rect);
                    return;
                }
            }
            catch (Exception ex) { Diagnostics.Log("restore window bounds", ex); }
        }
        CenterWithSize(w, h);
    }

    private void CenterWithSize(int w, int h)
    {
        try
        {
            var area = Microsoft.UI.Windowing.DisplayArea.GetFromWindowId(
                AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest);
            var wa = area.WorkArea;
            w = Math.Min(w, wa.Width);
            h = Math.Min(h, wa.Height);
            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                wa.X + (wa.Width - w) / 2, wa.Y + (wa.Height - h) / 2, w, h));
        }
        catch (Exception ex) { Diagnostics.Log("center window", ex); }
    }

    /// <summary>Tracks the last visible, non-minimized, non-maximized bounds for persistence.</summary>
    private void OnAppWindowChanged(Microsoft.UI.Windowing.AppWindow sender,
        Microsoft.UI.Windowing.AppWindowChangedEventArgs args)
    {
        if (!args.DidPositionChange && !args.DidSizeChange) return;
        if (!sender.IsVisible) return;
        if (sender.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter
            { State: Microsoft.UI.Windowing.OverlappedPresenterState.Restored }) return;
        _lastNormalBounds = new Windows.Graphics.RectInt32(
            sender.Position.X, sender.Position.Y, sender.Size.Width, sender.Size.Height);
    }

    /// <summary>Persists the tracked bounds reloading preferences from disk first, so newer settings saved elsewhere are not clobbered.</summary>
    private void SaveWindowBounds()
    {
        if (_lastNormalBounds is not { } b) return;
        try
        {
            var prefs = LocalPreferences.Load();
            prefs.WindowX = b.X;
            prefs.WindowY = b.Y;
            prefs.WindowWidth = b.Width;
            prefs.WindowHeight = b.Height;
            prefs.Save();
        }
        catch (Exception ex) { Diagnostics.Log("save window bounds", ex); }
    }

    /// <summary>
    /// Chooses the first page from the local session so a returning user lands straight on
    /// MainPage (SQLite cache, network-tolerant) instead of waiting on LoginPage. With a
    /// persisted session the system hooks are installed here too, since MainPage bypasses
    /// the LoginPage path that normally installs them.
    /// </summary>
    public void PrepareStartup(bool hasLocalSession)
    {
        if (hasLocalSession)
        {
            InstallSystemHooks();
            RootFrameControl.Navigate(typeof(MainPage), null, new SuppressNavigationTransitionInfo());
        }
        else
        {
            RootFrameControl.Navigate(typeof(LoginPage), null, new SuppressNavigationTransitionInfo());
        }
    }

    /// <summary>Creates the window without activating it and hides it into the tray for the --tray autostart path.</summary>
    public void StartHiddenInTray()
    {
        if (!_hooksInstalled) InstallSystemHooks();
        SetTrayVisible(true);
        AppWindow.Hide();
    }

    /// <summary>Routes a jump-list / tray startup verb onto the existing command-layer actions.</summary>
    public void RouteStartupAction(Data.StartupAction action)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (action)
            {
                case Data.StartupAction.Add: AddLinkCommand.Execute(null); break;
                case Data.StartupAction.Search: SearchCommand.Execute(null); break;
                case Data.StartupAction.SaveClipboard: SaveClipboardCommand.Execute(null); break;
            }
        });
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
        if (_prefs.GlobalHotkeyEnabled)
        {
            UninstallHotkey();
            InstallHotkey();
        }
        else if (_hotkey is not null)
        {
            UninstallHotkey();
        }
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
        if (RootFrameControl.Content is MainPage mp)
        {
            mp.SetExternalStatus(text);
            return;
        }

        WindowStatusText.Text = text;
        WindowStatusText.Visibility = Visibility.Visible;
        Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer
            .CreatePeerForElement(WindowStatusText)
            ?.RaiseAutomationEvent(Microsoft.UI.Xaml.Automation.Peers.AutomationEvents.LiveRegionChanged);
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
        _hotkey = new GlobalHotkey(this, _prefs.HotkeyModifiers, _prefs.HotkeyVirtualKey);
        _hotkey.Pressed += (_, _) => _commandLayer.Show();
        var combo = GlobalHotkey.Describe(_prefs.HotkeyModifiers, _prefs.HotkeyVirtualKey);
        HotkeyWarning = _hotkey.IsRegistered
            ? null
            : $"Aviso: no se pudo registrar el atajo {combo} (otra app lo usa).";
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
        SaveWindowBounds();
        try { App.State?.Cache.Dispose(); } catch {  }
        try { UninstallHotkey(); } catch {  }
        try { TrayIcon.Dispose(); } catch {  }
        try { LinkManager2.Data.Notifications.Unregister(); } catch {  }
        try { SupabaseClientHolder.Shutdown(); } catch {  }
        Environment.Exit(0);
    }

    private void OnAppWindowClosing(Microsoft.UI.Windowing.AppWindow sender,
        Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        SaveWindowBounds();
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
