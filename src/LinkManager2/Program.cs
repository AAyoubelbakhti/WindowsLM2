using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using LinkManager2.Data;
using Microsoft.Windows.AppLifecycle;
using Velopack;

namespace LinkManager2;

public static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var keyInstance = AppInstance.FindOrRegisterForKey("linkmanager");
        if (!keyInstance.IsCurrent)
        {
            RedirectToPrimary(keyInstance);
            return;
        }

        keyInstance.Activated += App.OnActivatedFromOtherInstance;

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start((p) =>
        {
            var context = new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }

    /// <summary>
    /// Redirects activation to the primary instance following the official AppLifecycle
    /// sample: the async redirect runs on a pool thread while the STA thread waits with
    /// CoWaitForMultipleObjects, which keeps pumping COM messages and avoids the
    /// documented deadlock of blocking the STA with GetResult().
    /// </summary>
    private static void RedirectToPrimary(AppInstance primary)
    {
        Microsoft.Windows.AppLifecycle.AppActivationArguments eventArgs;
        try { eventArgs = AppInstance.GetCurrent().GetActivatedEventArgs(); }
        catch (Exception ex) { Diagnostics.Log("redirect activation args", ex); return; }

        using var redirected = new ManualResetEvent(false);
        _ = Task.Run(() =>
        {
            try { primary.RedirectActivationToAsync(eventArgs).AsTask().Wait(); }
            catch (Exception ex) { Diagnostics.Log("redirect activation", ex); }
            finally { redirected.Set(); }
        });
        _ = CoWaitForMultipleObjects(0, 0xFFFFFFFF, 1,
            new[] { redirected.SafeWaitHandle.DangerousGetHandle() }, out _);
    }

    [DllImport("ole32.dll")]
    private static extern uint CoWaitForMultipleObjects(
        uint dwFlags, uint dwMilliseconds, ulong nHandles, IntPtr[] pHandles, out uint dwIndex);
}
