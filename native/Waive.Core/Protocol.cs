using System.Buffers.Binary;
using System.Globalization;

namespace Waive.Core;

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb Parse(string value)
    {
        if(value is null)throw new FormatException("A color is missing.");
        var text = value.Trim();
        if (text.StartsWith('#')) text = text[1..];
        else if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        if (text.Length != 6 || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var color))
            throw new FormatException("Enter a six-digit RGB color, such as #35B8FF.");
        return new((byte)(color >> 16), (byte)(color >> 8), (byte)color);
    }
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
}

public sealed record Command(byte Code, byte Subcommand, uint Header = 0, byte[]? Payload = null)
{
    public bool IsStoredWrite => Code is 0x21 or 0x22 or 0x24 or 0x25 or 0x26 or 0x27 or 0x31;
    public bool MatchesResponse(ReadOnlySpan<byte> response) => response.Length >= 2 && response[0] == Code && (IsStoredWrite||response[1] == Subcommand);
    public byte[] Encode()
    {
        if (Payload?.Length > 56) throw new ArgumentException("A report holds at most 56 payload bytes.");
        var data = new byte[64];
        data[0] = Code; data[1] = Subcommand;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(2), Header);
        Payload?.CopyTo(data, 8);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(6), Protocol.Crc(data));
        return data;
    }
    public byte[] WindowsReport(int reportLength)
    {
        if (reportLength != 65) throw new NotSupportedException($"Expected a 65-byte HID output report, found {reportLength}.");
        var report = new byte[reportLength];
        Encode().CopyTo(report, 1);
        return report;
    }
}

public static class Protocol
{
    public static ushort Crc(ReadOnlySpan<byte> bytes)
    {
        ushort crc = 0xffff;
        foreach (var value in bytes)
        {
            crc ^= (ushort)(value << 8);
            for (int bit = 0; bit < 8; bit++)
                crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
        }
        return crc;
    }

    public static readonly Command Firmware = new(1, 1), DeviceId = new(1, 2), Model = new(1, 8), Matrix = new(1, 9), Ping = new(0x0c, 0), CommitLive = new(0x1a, 2);
    public static Command Mode(int mode) => mode is >= 1 and <= 5 ? new(0x0b, (byte)mode) : throw new ArgumentOutOfRangeException(nameof(mode));
    public static Command KeyReports(bool enabled) => new(0x15, 3, 0, [(byte)(enabled ? 1 : 0)]);
    public static Command CleanLighting(int mode) => new(0x21, CheckedOnboard(mode), 3);
    static byte CheckedOnboard(int mode) => mode is >= 1 and <= 4 ? (byte)mode : throw new ArgumentOutOfRangeException(nameof(mode));
    public static Command StoredColor(int mode, int offset, byte[] bytes) => new(0x24, CheckedOnboard(mode), Header(offset, bytes.Length, 16), bytes);
    public static Command LiveColor(int offset, byte[] bytes) => new(0x1a, 1, Header(offset, bytes.Length, 24), bytes);
    public static Command KeyTable(int type, int offset, byte[] bytes) => new(0x16, checked((byte)type), Header(offset, bytes.Length, 24), bytes);
    static uint Header(int offset, int count, int shift)
    {
        if (offset < 0 || offset >= 1 << shift || count is < 1 or > 56) throw new ArgumentOutOfRangeException(nameof(offset));
        return (uint)offset | (uint)count << shift;
    }

    public static byte[] Colors(Rgb color, int count, int brightness)
    {
        if (count is < 1 or > 176) throw new ArgumentOutOfRangeException(nameof(count));
        if (brightness is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(brightness));
        var data = new byte[count * 4];
        for (var index = 0; index < count; index++)
        {
            // Match the old app's RGB + brightness byte format.
            data[index * 4] = color.R; data[index * 4 + 1] = color.G;
            data[index * 4 + 2] = color.B; data[index * 4 + 3] = (byte)brightness;
        }
        return data;
    }
    public static IEnumerable<(int Offset, byte[] Bytes)> Chunks(byte[] bytes)
    {
        for (var offset = 0; offset < bytes.Length; offset += 56)
            yield return (offset, bytes.AsSpan(offset, Math.Min(56, bytes.Length - offset)).ToArray());
    }
    public static void RequireAck(Command command, byte[] response)
    {
        if(command.IsStoredWrite)
        {
            if(response.Length<2||!command.MatchesResponse(response)||(response[1]!=1&&!(command.Code==0x21&&response[1]==0)))throw new IOException($"The keyboard refused the onboard write ({command.Code:X2}, status {(response.Length>1?response[1]:0):X2}). No further packets were sent.");
            return;
        }
        // Firmware 4.5 replies with command, subcommand, status. The subcommand
        // is echoed even when it is 2 (commit) or 3 (key-effect table).
        if (response.Length < 3 || !command.MatchesResponse(response) || response[2] != 1)
            throw new IOException($"The keyboard rejected command {command.Code:X2}/{command.Subcommand:X2} (reply {Convert.ToHexString(response.AsSpan(0, Math.Min(response.Length, 12)))}).");
    }
}

public interface ITransport : IDisposable
{
    Task<byte[]> RequestAsync(Command command, CancellationToken cancellationToken = default);
}

public sealed record DeviceIdentity(uint ModelId, uint FirmwareId, ushort FirmwareVersion, ulong DeviceId, byte Columns, byte Rows)
{
    public string Version => $"{FirmwareVersion >> 8}.{FirmwareVersion & 255}";
    public static async Task<DeviceIdentity> ReadAsync(ITransport transport, CancellationToken ct = default)
    {
        var fw = await transport.RequestAsync(Protocol.Firmware, ct);
        var device = await transport.RequestAsync(Protocol.DeviceId, ct);
        var model = await transport.RequestAsync(Protocol.Model, ct);
        var matrix = await transport.RequestAsync(Protocol.Matrix, ct);
        foreach (var response in new[] { fw, device, model, matrix })
            if (response.Length < 16) throw new IOException("The keyboard returned an incomplete identification report.");
        ulong id = 0;
        for (var i = 0; i < 6; i++) id |= (ulong)device[8 + i] << (8 * i);
        var columns = matrix[8]; var rows = matrix[9];
        if (columns == 0 || rows == 0 || columns * rows > 176) throw new NotSupportedException("The keyboard reported an unsupported key matrix.");
        return new(BinaryPrimitives.ReadUInt32LittleEndian(model.AsSpan(8)), BinaryPrimitives.ReadUInt32LittleEndian(fw.AsSpan(8)), BinaryPrimitives.ReadUInt16LittleEndian(fw.AsSpan(12)), id, columns, rows);
    }
}

public sealed partial class LightingController(ITransport transport)
{
    readonly SemaphoreSlim operation = new(1, 1);
    async Task SendCheckedAsync(Command command, CancellationToken ct)
    {
        Protocol.RequireAck(command, await transport.RequestAsync(command, ct));
    }
    public async Task ApplyLiveAsync(Rgb color, int brightness, IProgress<int>? progress = null, CancellationToken ct = default, byte[]? keyTable = null)
    {
        if (keyTable is not null && (keyTable.Length < 4 || keyTable.Length > 704 || keyTable.Length % 4 != 0)) throw new ArgumentException("Invalid live key table.");
        var frames = Protocol.Chunks(Protocol.Colors(color, 132, brightness)).ToArray();
        await operation.WaitAsync(ct);
        try
        {
            await transport.RequestAsync(Protocol.Ping, ct);
            var mode = await transport.RequestAsync(Protocol.Mode(5), ct);
            if (mode.Length < 2 || mode[1] != 5) throw new IOException("The keyboard did not enter live mode.");
            await transport.RequestAsync(Protocol.Ping, ct);
            // Static lighting does not need a host copy of every keystroke.
            await SendCheckedAsync(Protocol.KeyReports(false), ct);
            if (keyTable is not null)
            {
                foreach (var (offset, bytes) in Protocol.Chunks(keyTable))
                    await SendCheckedAsync(Protocol.KeyTable(1, offset, bytes), ct);
                var effects = Enumerable.Repeat((byte)0xff, keyTable.Length / 4).ToArray();
                foreach (var (offset, bytes) in Protocol.Chunks(effects))
                    await SendCheckedAsync(Protocol.KeyTable(3, offset, bytes), ct);
            }
            var chunks = frames;
            for (var i = 0; i < chunks.Length; i++)
            {
                var (offset, data) = chunks[i];
                await SendCheckedAsync(Protocol.LiveColor(offset, data), ct);
                progress?.Report((i + 1) * 95 / chunks.Length);
            }
            await SendCheckedAsync(Protocol.CommitLive, ct);
            progress?.Report(100);
        }
        finally { operation.Release(); }
    }
    public async Task ApplyStoredAsync(int mode, Rgb color, int brightness, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        // Compile before cleaning any region. This changes only the static lighting region.
        var chunks = Protocol.Chunks(Protocol.Colors(color, 176, brightness)).ToArray();
        var clean = Protocol.CleanLighting(mode);
        await operation.WaitAsync(ct);
        try
        {
            await SendCheckedAsync(clean, ct);
            for (var i = 0; i < chunks.Length; i++)
            {
                var (offset, data) = chunks[i];
                await SendCheckedAsync(Protocol.StoredColor(mode, offset, data), ct);
                progress?.Report((i + 1) * 95 / chunks.Length);
                await Task.Delay(10, ct);
            }
            await RestoreOnboardInternal(mode, ct);
            progress?.Report(100);
        }
        finally { operation.Release(); }
    }
    public async Task RestoreOnboardAsync(int mode = 1, CancellationToken ct = default)
    {
        await operation.WaitAsync(ct);
        try { await RestoreOnboardInternal(mode, ct); }
        finally { operation.Release(); }
    }
    async Task RestoreOnboardInternal(int mode, CancellationToken ct)
    {
        var reply = await transport.RequestAsync(Protocol.Mode(mode), ct);
        if (reply.Length < 2 || reply[1] != mode) throw new IOException("The keyboard did not confirm the onboard layer change.");
    }
}
