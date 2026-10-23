using System.Text.Json;
using Waive.Core;

var root = args.FirstOrDefault() ?? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
var fixtureFile = Path.Combine(root, "reference", "protocol-vectors.json");
using var fixtures = JsonDocument.Parse(File.ReadAllText(fixtureFile));
var commands = new Dictionary<string, Command>
{
    ["query-firmware"] = Protocol.Firmware, ["query-device-id"] = Protocol.DeviceId,
    ["query-model"] = Protocol.Model, ["query-matrix"] = Protocol.Matrix,
    ["select-onboard-1"] = Protocol.Mode(1), ["select-live"] = Protocol.Mode(5),
    ["ping"] = Protocol.Ping, ["enable-key-reports"] = Protocol.KeyReports(true),
    ["clean-onboard-static-colors"] = Protocol.CleanLighting(1),
    ["write-onboard-first-red-key"] = Protocol.StoredColor(1, 0, [255, 0, 0, 100]),
    ["write-live-first-red-key"] = Protocol.LiveColor(0, [255, 0, 0, 100]),
    ["commit-live-colors"] = Protocol.CommitLive,
    ["write-key-table"] = Protocol.KeyTable(1, 0, [4, 0, 0, 0]),
    ["write-last-partial-packet"] = Protocol.StoredColor(1, 672, new byte[32]),
};
var passed = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
foreach (var fixture in fixtures.RootElement.GetProperty("vectors").EnumerateArray())
{
    var name = fixture.GetProperty("name").GetString()!;
    var command = commands[name];
    Check(Convert.ToHexString(command.Encode()).Equals(fixture.GetProperty("reportHex").GetString(), StringComparison.OrdinalIgnoreCase), name);
    Check(Convert.ToHexString(command.WindowsReport(65)).Equals(fixture.GetProperty("windowsWriteHex").GetString(), StringComparison.OrdinalIgnoreCase), name + " Windows framing");
}
Check(Protocol.Crc("123456789"u8) == 0x29b1, "CRC standard check");
Check(Rgb.Parse("#FF8040") == new Rgb(255, 128, 64), "CSS RGB parsing");
Check(Rgb.Parse("0xff8040").Hex == "#FF8040", "hex RGB parsing");
Check(Protocol.Colors(new Rgb(255, 128, 64), 1, 100).SequenceEqual(new byte[] { 255, 128, 64, 100 }), "RGB payload bytes");
Check(Protocol.Chunks(new byte[704]).Last().Bytes.Length == 32, "final partial packet");
using var capturedReplies = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "reference", "native-ack-replies.json")));
foreach (var fixture in capturedReplies.RootElement.GetProperty("cases").EnumerateArray())
{
    var request = Convert.FromHexString(fixture.GetProperty("requestHex").GetString()!);
    var response = Convert.FromHexString(fixture.GetProperty("responseHex").GetString()!);
    var command = new Command(request[0], request[1]);
    Protocol.RequireAck(command, response);
    Check(true, $"captured firmware 4.5 ACK {command.Code:X2}/{command.Subcommand:X2}");
    response[2] = 0;
    try { Protocol.RequireAck(command, response); throw new Exception("Rejected status accepted"); }
    catch (IOException) { Check(true, $"negative status rejected for {command.Code:X2}/{command.Subcommand:X2}"); }
}
Check(!Protocol.KeyTable(3, 0, [0]).MatchesResponse([0x16, 1, 1]), "wrong subcommand does not complete the request");
Check(!Protocol.KeyReports(false).MatchesResponse([0x15, 4, 1]), "key notification does not complete a settings request");
try { Protocol.RequireAck(Protocol.CommitLive, [0x1a, 2]); throw new Exception("Missing status accepted"); }
catch (IOException) { Check(true, "truncated commit reply rejected"); }
try { Rgb.Parse("invalid"); throw new Exception("Invalid RGB accepted"); } catch (FormatException) { passed++; }
try { Protocol.Mode(0); throw new Exception("Invalid mode accepted"); } catch (ArgumentOutOfRangeException) { passed++; }
try { Protocol.CleanLighting(5); throw new Exception("Live profile flash accepted"); } catch (ArgumentOutOfRangeException) { passed++; }

using var fake = new FakeTransport();
var lighting = new LightingController(fake);
await lighting.ApplyLiveAsync(new Rgb(255, 0, 0), 100);
Check(fake.Commands.Count(c => c.Code == 0x1a && c.Subcommand == 1) == 10, "live packet count");
Check(fake.Commands.Last() == Protocol.CommitLive, "commit only after color frames");
Check(fake.Commands.Any(c => c.Code == 0x15 && c.Subcommand == 3 && c.Payload is [0]), "lighting disables unnecessary host key reports");
fake.Commands.Clear();
await lighting.ApplyLiveAsync(new Rgb(255, 0, 0), 100, keyTable: new byte[112 * 4]);
Check(fake.Commands.Count(c => c.Code == 0x16 && c.Subcommand == 1) == 8, "live standard mapping for the detected 14 by 8 matrix");
Check(fake.Commands.Count(c => c.Code == 0x16 && c.Subcommand == 3) == 2, "live key-effect index table");
fake.Commands.Clear();
try { await lighting.ApplyLiveAsync(new Rgb(0, 0, 0), 101); throw new Exception("Invalid brightness accepted"); }
catch (ArgumentOutOfRangeException) { Check(fake.Commands.Count == 0, "validate brightness before device writes"); }
fake.Commands.Clear();
await lighting.ApplyStoredAsync(1, new Rgb(0, 255, 0), 100);
Check(fake.Commands.Count(c => c.Code == 0x24) == 13, "onboard packet count");
Check(fake.Commands.All(c => c.Code is not (0x22 or 0x25 or 0x31)), "lighting does not overwrite keys or macros");
Check(fake.Commands.Last() == Protocol.Mode(1), "onboard layer activated");
fake.FailWrites = true;
try { await lighting.ApplyLiveAsync(new Rgb(0, 0, 255), 100); throw new Exception("Negative ACK accepted"); }
catch (IOException) { passed++; }
fake.FailWrites = false;
await lighting.ApplyLiveAsync(new Rgb(0, 0, 255), 100);
Check(true, "operation lock recovers after write failure");
using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
try { await lighting.ApplyLiveAsync(new Rgb(0, 0, 0), 100, null, cancelled.Token); throw new Exception("Cancellation ignored"); }
catch (OperationCanceledException) { passed++; }
await ProfileChecks.Run(root,Check);
await LightingMapChecks.Run(root,Check);
await FlashChecks.Run(root,Check);
Console.WriteLine($"{passed} checks passed; no hardware writes performed.");

sealed class FakeTransport : ITransport
{
    public List<Command> Commands { get; } = [];
    public bool FailWrites { get; set; }
    public bool FailFlashWrites { get; set; }
    public Task<byte[]> RequestAsync(Command command, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Commands.Add(command);
        var response = new byte[64]; response[0] = command.Code; response[1] = command.Subcommand; response[2] = (byte)(FailWrites ? 0 : 1);
        if(command.IsStoredWrite){response[1]=(byte)(FailFlashWrites?0xf0:command.Code==0x21?0:1);response[2]=0;}
        return Task.FromResult(response);
    }
    public void Dispose() { }
}
