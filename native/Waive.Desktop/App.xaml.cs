using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace Waive.Desktop;

public partial class App : Application
{
    Mutex? instance;
    bool ownsInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        var smokeIndex=Array.IndexOf(e.Args,"--smoke");
        if(smokeIndex>=0&&smokeIndex+1<e.Args.Length)DispatcherUnhandledException+=(_,error)=>
        {
            System.IO.Directory.CreateDirectory(e.Args[smokeIndex+1]);
            System.IO.File.WriteAllText(System.IO.Path.Combine(e.Args[smokeIndex+1],"startup-error.txt"),error.Exception.ToString());
            error.Handled=true;Shutdown(1);
        };
        instance = new Mutex(true, @"Local\WaiveGK6XPlus", out ownsInstance);
        if (!ownsInstance)
        {
            // Hidden tray windows have no Process.MainWindowHandle.
            PostMessage(new nint(0xffff),TrayControl.RestoreMessage,0,0);
            foreach (var process in Process.GetProcessesByName("WaiveGK6XPlus"))
            {
                using (process)
                {
                    if (process.Id == Environment.ProcessId || process.MainWindowHandle == 0) continue;
                    if (IsIconic(process.MainWindowHandle)) ShowWindow(process.MainWindowHandle, 9);
                    SetForegroundWindow(process.MainWindowHandle);
                    break;
                }
            }
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        if (ownsInstance) instance?.ReleaseMutex();
        instance?.Dispose();
        base.OnExit(e);
    }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] static extern bool PostMessage(nint window,uint message,nint wparam,nint lparam);
}
