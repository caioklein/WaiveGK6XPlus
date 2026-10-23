using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Waive.Core;

namespace Waive.Device.Windows;

public sealed record HidDevice(string Path, string Product, ushort VendorId, ushort ProductId, ushort UsagePage, ushort Usage, ushort InputLength, ushort OutputLength)
{
    public override string ToString() => $"{Product} · {VendorId:X4}:{ProductId:X4}";
}

public static class HidDiscovery
{
    public static IReadOnlyList<HidDevice> Enumerate()
    {
        Native.HidD_GetHidGuid(out var guid);
        var set = Native.SetupDiGetClassDevs(ref guid, null, 0, 0x12);
        if (set == new nint(-1)) throw new Win32Exception();
        var devices = new List<HidDevice>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var entry = new Native.InterfaceData { Size = Marshal.SizeOf<Native.InterfaceData>() };
                if (!Native.SetupDiEnumDeviceInterfaces(set, 0, ref guid, index, ref entry))
                {
                    if (Marshal.GetLastWin32Error() == 259) break;
                    throw new Win32Exception();
                }
                Native.SetupDiGetDeviceInterfaceDetail(set, ref entry, 0, 0, out var size, 0);
                if (size == 0 || size > 32768) continue;
                var buffer = Marshal.AllocHGlobal((int)size);
                try
                {
                    Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                    if (!Native.SetupDiGetDeviceInterfaceDetail(set, ref entry, buffer, size, out _, 0)) continue;
                    var path = Marshal.PtrToStringUni(buffer + 4);
                    if (path is null) continue;
                    using var handle = Native.CreateFile(path, 0, 3, 0, 3, 0, 0);
                    if (handle.IsInvalid) continue;
                    var attributes = new Native.Attributes { Size = Marshal.SizeOf<Native.Attributes>() };
                    if (!Native.HidD_GetAttributes(handle, ref attributes)) continue;
                    if (attributes.VendorId != 0x1ea7 || attributes.ProductId != 0x0907) continue;
                    if (!Native.HidD_GetPreparsedData(handle, out var preparsed)) continue;
                    try
                    {
                        if (Native.HidP_GetCaps(preparsed, out var caps) != 0x110000) continue;
                        if (caps.UsagePage != 0xff00 || caps.Usage != 0x50) continue;
                        var productBuffer = Marshal.AllocHGlobal(512);
                        try
                        {
                            var product = Native.HidD_GetProductString(handle, productBuffer, 512)
                                ? Marshal.PtrToStringUni(productBuffer) : null;
                            devices.Add(new(path, product ?? "GK6+ keyboard", attributes.VendorId, attributes.ProductId, caps.UsagePage, caps.Usage, caps.InputLength, caps.OutputLength));
                        }
                        finally { Marshal.FreeHGlobal(productBuffer); }
                    }
                    finally { Native.HidD_FreePreparsedData(preparsed); }
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        finally { Native.SetupDiDestroyDeviceInfoList(set); }
        return devices;
    }
}

public sealed class HidTransport : ITransport
{
    readonly SemaphoreSlim gate = new(1, 1);
    readonly FileStream writer, reader;
    readonly HidDevice device;
    readonly CancellationTokenSource lifetime = new();
    readonly object sync = new();
    readonly HashSet<int> pressed = [];
    readonly Task reading;
    TaskCompletionSource<byte[]>? pending;
    Command? pendingCommand;
    bool disposed;
    public Action<string>? Trace { get; set; }
    public event Action<int,bool>? KeyChanged;
    public event Action<int>? ModeChanged;

    public HidTransport(HidDevice device)
    {
        this.device = device;
        if (device.OutputLength != 65 || device.InputLength != 65)
            throw new NotSupportedException($"Expected 65-byte HID reports; found input {device.InputLength}, output {device.OutputLength}.");
        FileStream Open(uint access)
        {
            var handle = Native.CreateFile(device.Path, access, 3, 0, 3, 0x40000000, 0);
            if (handle.IsInvalid) { var code = Marshal.GetLastWin32Error(); handle.Dispose(); throw new Win32Exception(code,"Cannot open the keyboard. Close other keyboard utilities and reconnect."); }
            try { return new FileStream(handle, access == 0x80000000 ? FileAccess.Read : FileAccess.Write,65,true); }
            catch { handle.Dispose(); throw; }
        }
        writer = Open(0x40000000);
        try { reader = Open(0x80000000); }
        catch { writer.Dispose(); throw; }
        reading = Task.Run(ReadLoopAsync);
    }

    async Task ReadLoopAsync()
    {
        var data = new byte[65];
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                var count = await reader.ReadAsync(data,lifetime.Token);
                if(count!=65 || data[0]!=0)throw new IOException("The keyboard returned an invalid HID report.");
                var response = data.AsSpan(1,64).ToArray();
                if(response[0]==0x15 && response[1]==4)
                {
                    var current = new HashSet<int>();
                    for(int index=0;index<128;index++)if((response[8+index/8]&(1<<(index%8)))!=0)current.Add(index);
                    foreach(var key in current.Except(pressed))KeyChanged?.Invoke(key,true);
                    foreach(var key in pressed.Except(current))KeyChanged?.Invoke(key,false);
                    pressed.Clear();pressed.UnionWith(current);continue;
                }
                TaskCompletionSource<byte[]>? completion = null;
                lock(sync) { if(pendingCommand?.MatchesResponse(response)==true)completion=pending; }
                if(completion is not null)
                {
                    Trace?.Invoke($"RX {Convert.ToHexString(response)}");
                    completion.TrySetResult(response);
                }
                else if(response[0]==0x0b)ModeChanged?.Invoke(response[1]);
                else { lock(sync){if(pendingCommand?.Code==response[0])Trace?.Invoke($"RX unmatched {Convert.ToHexString(response)}");} }
            }
        }
        catch(OperationCanceledException) when(lifetime.IsCancellationRequested){}
        catch(Exception error)
        {
            lock(sync)pending?.TrySetException(new IOException("The keyboard connection was lost.",error));
            Dispose();
        }
    }

    public async Task<byte[]> RequestAsync(Command command,CancellationToken cancellationToken=default)
    {
        await gate.WaitAsync(cancellationToken);
        var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            ObjectDisposedException.ThrowIf(disposed,this);
            cancellationToken.ThrowIfCancellationRequested();
            lock(sync) { pending=completion;pendingCommand=command; }
            // Complete an in-flight packet before honoring cancellation at the next packet.
            // This preserves reply attribution and permits clean Stop/return-to-onboard.
            using var timeout = new CancellationTokenSource(command.Code==0x0c?1000:3000);
            try
            {
                if(command.Code!=0x15 || command.Subcommand==3)Trace?.Invoke($"TX {Convert.ToHexString(command.Encode())}");
                await writer.WriteAsync(command.WindowsReport(device.OutputLength),timeout.Token);
                return await completion.Task.WaitAsync(timeout.Token);
            }
            catch(OperationCanceledException)
            {
                Dispose();
                throw new TimeoutException("The keyboard did not respond. Reconnect to start a fresh session.");
            }
            catch { Dispose();throw; }
        }
        finally
        {
            lock(sync){if(ReferenceEquals(pending,completion)){pending=null;pendingCommand=null;}}
            gate.Release();
        }
    }
    public void Dispose()
    {
        lock(sync) { if(disposed)return;disposed=true;pending?.TrySetException(new IOException("The keyboard session was closed.")); }
        lifetime.Cancel();writer.Dispose();reader.Dispose();
    }
}

internal static class Native
{
    [StructLayout(LayoutKind.Sequential)] internal struct InterfaceData { public int Size; public Guid ClassGuid; public int Flags; public nint Reserved; }
    [StructLayout(LayoutKind.Sequential)] internal struct Attributes { public int Size; public ushort VendorId, ProductId, Version; }
    [StructLayout(LayoutKind.Sequential)] internal struct Caps
    {
        public ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort LinkNodes, InputButtons, InputValues, InputIndices, OutputButtons, OutputValues, OutputIndices, FeatureButtons, FeatureValues, FeatureIndices;
    }
    [DllImport("hid.dll")] internal static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_GetAttributes(SafeFileHandle handle, ref Attributes attributes);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out nint data);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_FreePreparsedData(nint data);
    [DllImport("hid.dll")] internal static extern int HidP_GetCaps(nint data, out Caps caps);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)] internal static extern bool HidD_GetProductString(SafeFileHandle handle, nint buffer, int bytes);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint SetupDiGetClassDevs(ref Guid guid, string? enumerator, nint parent, int flags);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetupDiEnumDeviceInterfaces(nint set, nint device, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetupDiGetDeviceInterfaceDetail(nint set, ref InterfaceData data, nint detail, uint bytes, out uint required, nint device);
    [DllImport("setupapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetupDiDestroyDeviceInfoList(nint set);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint creation, uint flags, nint template);
}
