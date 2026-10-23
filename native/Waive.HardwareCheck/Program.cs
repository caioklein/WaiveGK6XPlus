using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using Waive.Core;
using Waive.Device.Windows;

if(args.Length < 2 || args[0] is not ("--apply-temporary" or "--apply-onboard-lighting" or "--apply-layer3-lighting")) throw new ArgumentException("Pass a supported test mode and an output JSON file.");
bool stored=args[0]!="--apply-temporary";
var log = new List<string>();
string? failure = null, restoreFailure = null;
DeviceIdentity? identity = null;
using var transport = new HidTransport(HidDiscovery.Enumerate().Single());
transport.Trace = text => { log.Add(text); Console.WriteLine(text); };
try
{
    identity = await DeviceIdentity.ReadAsync(transport);
    if(identity.ModelId != 656801936) throw new NotSupportedException("Only the confirmed 96XS-RGB is allowed.");
    var defaults = new Dictionary<int,uint>();
    using var keys = JsonDocument.Parse(File.ReadAllText("native/Waive.Desktop/Data/96xs-live-defaults.json"));
    foreach(var key in keys.RootElement.GetProperty("KeySet").EnumerateArray())
        defaults[key.GetProperty("Index").GetInt32()]=uint.Parse(key.GetProperty("DriverValue").GetString()![2..],NumberStyles.HexNumber);
    var row=KeyboardLightingMap.Keys.Where(k=>"QWERTYUIOP".Contains(k.KeyName,StringComparison.Ordinal)&&k.KeyName.Length==1).ToArray();
    var profile=new KeyboardProfile{Color="#000000",KeyColors=row.ToDictionary(k=>k.LogicCode,_=>"#FF0000")};
    var controller=new LightingController(transport);
    if(stored)
    {
        profile.Mode=args[0]=="--apply-layer3-lighting"?4:1;
        await controller.ApplyProfileStoredAsync(profile,defaults,identity.Columns*identity.Rows);
        Console.WriteLine("Built-in lighting stored; key and macro regions preserved.");
    }
    else
    {
    await controller.ApplyProfileLiveAsync(profile,defaults,identity.Columns*identity.Rows);
    Console.WriteLine("Temporary red Q–P accepted at LED positions "+string.Join(",",row.Select(k=>k.LocationCode)));
    var effect=new LightingEffect{Frames=[new(){Count=10,Data=row.ToDictionary(k=>k.LogicCode,_=>"#0080FF")}],Rules=[new(){Type=0,Color="#0080FF",Keys=row.Select(k=>k.LogicCode).ToList()}]};
    profile.Effects.Add(effect);profile.KeyEffects[26]=effect.Id;
    await controller.ApplyProfileLiveAsync(profile,defaults,identity.Columns*identity.Rows);
    await using(var playback=new LivePlayback(transport,controller,profile))
    {
        playback.Failed+=error=>failure=error.ToString();playback.Start();playback.Key(26,true);await Task.Delay(800);
    }
    Console.WriteLine("Temporary lighting playback completed. No stored regions written.");
    }
}
catch(Exception error){ failure=error.ToString(); Console.WriteLine(failure); }
finally
{
    if(identity is not null)
    {
        try { await new LightingController(transport).RestoreOnboardAsync(); }
        catch(Exception error){ restoreFailure=error.ToString(); }
    }
    File.WriteAllText(args[1],JsonSerializer.Serialize(new { identity,failure,restoreFailure,log },new JsonSerializerOptions{WriteIndented=true}));
}
Environment.ExitCode = failure is null ? 0 : 1;
