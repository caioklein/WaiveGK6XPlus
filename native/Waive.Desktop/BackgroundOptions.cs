using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;

namespace Waive.Desktop;

public partial class MainWindow
{
    const string StartupKey="Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    const string StartupName="WaiveGK6XPlus";
    bool loadingOptions;
    TrayControl? tray;
    void InitializeBackgroundOptions()
    {
        loadingOptions=true;
        try
        {
            using var run=Registry.CurrentUser.OpenSubKey(StartupKey);
            var command=run?.GetValue(StartupName) as string??run?.GetValue("WaiveKeyboard") as string;
            var oldPath=Path.Combine(AppContext.BaseDirectory,"Waive.exe");
            var oldFolderPath=Path.Combine(Directory.GetParent(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar))!.FullName,"Waive.Native","Waive.exe");
            bool ours=command is not null&&(command.StartsWith('"'+Environment.ProcessPath+'"',StringComparison.OrdinalIgnoreCase)||command.StartsWith('"'+oldPath+'"',StringComparison.OrdinalIgnoreCase)||command.StartsWith('"'+oldFolderPath+'"',StringComparison.OrdinalIgnoreCase));
            StartWithWindows.IsChecked=ours;
            if(ours&&command!='"'+Environment.ProcessPath+'"'+" --minimized")
            {using var update=Registry.CurrentUser.CreateSubKey(StartupKey);update.SetValue(StartupName,'"'+Environment.ProcessPath+'"'+" --minimized");update.DeleteValue("WaiveKeyboard",false);}
        }
        catch(Exception error) when(error is UnauthorizedAccessException or System.Security.SecurityException){StartWithWindows.IsEnabled=false;StartWithWindows.ToolTip="Windows startup preferences are unavailable in this session.";}
        ShowTray.IsChecked=File.Exists(Path.Combine(AppContext.BaseDirectory,"Profiles","tray-enabled"));
        loadingOptions=false;UpdateTray();
    }
    void UpdateTray()
    {
        if(ShowTray.IsChecked==true||WindowState==WindowState.Minimized)EnsureTray();
        else {tray?.Dispose();tray=null;}
    }
    void EnsureTray()=>tray??=new TrayControl(this,RestoreFromTray,()=>StopClicked(this,new RoutedEventArgs()),()=>{RestoreFromTray();Close();});
    void RestoreFromTray()
    {
        ShowInTaskbar=true;Show();WindowState=WindowState.Normal;Activate();UpdateTray();
    }
    void MinimizeToTray()
    {
        try
        {
            // Create a working way back before hiding the window.
            EnsureTray();ShowInTaskbar=false;Hide();
        }
        catch(Exception error)
        {
            RestoreFromTray();SetResult("Could not minimize to the system tray: "+error.Message,true);
        }
    }
    void TrayOptionChanged(object sender,RoutedEventArgs e)
    {
        if(loadingOptions||!IsLoaded)return;
        Guard(()=>{var path=Path.Combine(AppContext.BaseDirectory,"Profiles","tray-enabled");if(ShowTray.IsChecked==true){Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path,"");}else if(File.Exists(path))File.Delete(path);UpdateTray();});
    }
    void StartupOptionChanged(object sender,RoutedEventArgs e)
    {
        if(loadingOptions||!IsLoaded)return;
        Guard(()=>{using var run=Registry.CurrentUser.CreateSubKey(StartupKey);if(StartWithWindows.IsChecked==true)run.SetValue(StartupName,'"'+Environment.ProcessPath+'"'+" --minimized");else run.DeleteValue(StartupName,false);run.DeleteValue("WaiveKeyboard",false);SetResult("Windows startup preference updated. Startup opens WaiveGK6XPlus minimized; Apply starts your live profile.");});
    }
}

sealed class TrayControl : IDisposable
{
    const int Message=0x8001;
    internal static readonly uint RestoreMessage=RegisterWindowMessage("WaiveGK6XPlus.RestoreWindow");
    static readonly uint TaskbarCreated=RegisterWindowMessage("TaskbarCreated");
    readonly HwndSource source;
    readonly Action restore,stop,exit;
    Notice data;
    public TrayControl(Window owner,Action restore,Action stop,Action exit)
    {
        this.restore=restore;this.stop=stop;this.exit=exit;
        var handle=new WindowInteropHelper(owner).Handle;source=HwndSource.FromHwnd(handle)!;source.AddHook(Hook);
        data=new(){Size=(uint)Marshal.SizeOf<Notice>(),Window=handle,Id=1,Flags=7,Message=Message,Icon=LoadIcon(0,32512),Tip="WaiveGK6XPlus",Info="",Title=""};
        if(!Shell_NotifyIcon(0,ref data)){source.RemoveHook(Hook);throw new IOException("Windows could not add the notification icon.");}
    }
    nint Hook(nint window,int message,nint wparam,nint lparam,ref bool handled)
    {
        if((uint)message==RestoreMessage){handled=true;restore();return 0;}
        if((uint)message==TaskbarCreated){Shell_NotifyIcon(0,ref data);return 0;}
        if(message==Message)
        {
            handled=true;
            if((int)lparam==0x203)restore();
            if((int)lparam==0x205)
            {
                SetForegroundWindow(window);
                var menu=new ContextMenu{Placement=PlacementMode.MousePoint};
                foreach(var (label,action) in new[]{("Open WaiveGK6XPlus",restore),("Stop live profile",stop),("Exit",exit)}){var item=new MenuItem{Header=label};item.Click+=(_,_)=>action();menu.Items.Add(item);}
                menu.IsOpen=true;
            }
        }
        return 0;
    }
    public void Dispose(){Shell_NotifyIcon(2,ref data);source.RemoveHook(Hook);}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct Notice
    {
        public uint Size;public nint Window;public uint Id,Flags;public int Message;public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Tip;
        public uint State,StateMask;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string Title;
        public uint InfoFlags;public Guid Guid;public nint BalloonIcon;
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)][return:MarshalAs(UnmanagedType.Bool)]static extern bool Shell_NotifyIcon(uint message,ref Notice data);
    [DllImport("user32.dll")]static extern nint LoadIcon(nint instance,nint name);
    [DllImport("user32.dll")]static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern uint RegisterWindowMessage(string message);
}

