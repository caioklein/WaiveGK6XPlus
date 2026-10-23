using System.Diagnostics;
using System.Runtime.InteropServices;
using Waive.Core;

namespace Waive.Device.Windows;

public sealed class MacroRecorder : IDisposable
{
    delegate nint Hook(int code,nint message,nint data);
    readonly Hook keyboardCallback,mouseCallback;
    nint keyboard,mouse;
    readonly Stopwatch clock=new();
    long last;
    int count;
    bool stopping;
    readonly HashSet<string> heldKeys=[],heldMouse=[];
    public event Action<MacroEvent>? Recorded;
    public event Action? StopRequested;
    public bool IsRecording=>keyboard!=0;
    public MacroRecorder(){keyboardCallback=Keyboard;mouseCallback=Mouse;}
    public void Start(bool includeMouse)
    {
        if(IsRecording)return;last=0;count=0;stopping=false;heldKeys.Clear();heldMouse.Clear();clock.Restart();
        keyboard=SetWindowsHookEx(13,keyboardCallback,GetModuleHandle(null),0);
        if(keyboard==0)throw new System.ComponentModel.Win32Exception();
        if(includeMouse){mouse=SetWindowsHookEx(14,mouseCallback,GetModuleHandle(null),0);if(mouse==0){Stop();throw new System.ComponentModel.Win32Exception();}}
    }
    void Add(string kind,string value)
    {
        var now=clock.ElapsedMilliseconds;
        if(last>0 && now>last)Recorded?.Invoke(new(){Kind="Delay",Value=Math.Min(65535,now-last).ToString()});
        Recorded?.Invoke(new(){Kind=kind,Value=value});last=now;count+=2;
        if(count>=1900&&!stopping){stopping=true;StopRequested?.Invoke();}
    }
    nint Keyboard(int code,nint message,nint data)
    {
        if(code>=0 && !stopping)
        {
            int vk=Marshal.ReadInt32(data),scan=Marshal.ReadInt32(data,4),flags=Marshal.ReadInt32(data,8);
            bool down=message==0x100 || message==0x104,up=message==0x101 || message==0x105;
            if(vk==0x7b&&down){stopping=true;StopRequested?.Invoke();}
            else if(down||up)
            {
                var key=FromVirtualKey(vk,scan,(flags&1)!=0);
                if(key is not null)
                {
                    if(down&&heldKeys.Add(key))Add("KeyDown",key);
                    else if(up&&heldKeys.Remove(key))Add("KeyUp",key);
                }
            }
        }
        return CallNextHookEx(keyboard,code,message,data);
    }
    nint Mouse(int code,nint message,nint data)
    {
        if(code>=0&&!stopping)
        {
            GetWindowThreadProcessId(GetForegroundWindow(),out var process);
            if(process!=Environment.ProcessId)
            {
                var tuple=(int)message switch{0x201=>("MouseDown","Left"),0x202=>("MouseUp","Left"),0x204=>("MouseDown","Right"),0x205=>("MouseUp","Right"),0x207=>("MouseDown","Middle"),0x208=>("MouseUp","Middle"),_=>("","")};
                if(tuple.Item1=="MouseDown"&&heldMouse.Add(tuple.Item2))Add(tuple.Item1,tuple.Item2);
                else if(tuple.Item1=="MouseUp"&&heldMouse.Remove(tuple.Item2))Add(tuple.Item1,tuple.Item2);
            }
        }
        return CallNextHookEx(mouse,code,message,data);
    }
    public void Stop()
    {
        if(keyboard!=0)UnhookWindowsHookEx(keyboard);if(mouse!=0)UnhookWindowsHookEx(mouse);keyboard=0;mouse=0;
        foreach(var key in heldKeys.ToArray())Add("KeyUp",key);
        foreach(var button in heldMouse.ToArray())Add("MouseUp",button);
        heldKeys.Clear();heldMouse.Clear();clock.Stop();
    }
    public void Dispose()=>Stop();
    static string? FromVirtualKey(int vk,int scan,bool extended)
    {
        if(vk is >=0x41 and <=0x5a)return ((char)vk).ToString();
        if(vk is >=0x30 and <=0x39)return ((char)vk).ToString();
        if(vk is >=0x70 and <=0x87)return "F"+(vk-0x6f);
        if(vk is >=0x60 and <=0x69)return "Numpad"+(vk-0x60);
        return vk switch {0x08=>"Backspace",0x09=>"Tab",0x0d=>extended?"NumpadEnter":"Enter",0x10=>scan==54?"RShift":"LShift",0x11=>extended?"RCtrl":"LCtrl",0x12=>extended?"RAlt":"LAlt",0x13=>"Pause",0x14=>"Caps",0x1b=>"Esc",0x20=>"Space",0x21=>"PageUp",0x22=>"PageDown",0x23=>"End",0x24=>"Home",0x25=>"Left",0x26=>"Up",0x27=>"Right",0x28=>"Down",0x2c=>"PrintScreen",0x2d=>"Insert",0x2e=>"Delete",0x5b=>"LWin",0x5c=>"RWin",0x5d=>"App",0x6a=>"NumpadMultiply",0x6b=>"NumpadAdd",0x6d=>"NumpadSubtract",0x6e=>"NumpadDot",0x6f=>"NumpadDivide",0x90=>"NumLock",0x91=>"ScrollLock",0xa0=>"LShift",0xa1=>"RShift",0xa2=>"LCtrl",0xa3=>"RCtrl",0xa4=>"LAlt",0xa5=>"RAlt",0xba=>"Semicolon",0xbb=>"Equal",0xbc=>"Comma",0xbd=>"Minus",0xbe=>"Period",0xbf=>"Slash",0xc0=>"Backtick",0xdb=>"LeftBracket",0xdc=>"Backslash",0xdd=>"RightBracket",0xde=>"Quote",_=>null};
    }
    [DllImport("user32.dll",SetLastError=true)]static extern nint SetWindowsHookEx(int id,Hook callback,nint module,uint thread);
    [DllImport("user32.dll")]static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")]static extern nint CallNextHookEx(nint hook,int code,nint message,nint data);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll")]static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(nint window,out uint process);
}
