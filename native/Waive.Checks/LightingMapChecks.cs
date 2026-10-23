using System.Buffers.Binary;
using System.Text.Json.Nodes;
using Waive.Core;

static class LightingMapChecks
{
    public static async Task Run(string root,Action<bool,string> check)
    {
        var letters="QWERTYUIOP".Select(c=>KeyboardLightingMap.Keys.Single(k=>k.KeyName==c.ToString())).ToArray();
        check(letters.Select(k=>k.LogicCode).SequenceEqual(new[]{26,32,14,27,29,34,30,18,24,25}),"Q–P keep original switch indices");
        check(letters.Select(k=>k.LocationCode).SequenceEqual(Enumerable.Range(46,10)),"Q–P use LED slots 46–55, not switch indices");
        var expected=new Dictionary<string,int>{{"Escape",0},{"1",24},{"2",25},{"A",68},{"Z",90},{"Space",116},{"Fn",121},{"Keypad 0",127},{"Keypad .",128}};
        check(expected.All(k=>KeyboardLightingMap.Keys.Single(p=>p.KeyName==k.Key).LocationCode==k.Value),"independent row and bottom-key LED positions");
        bool IsRed(byte[] frame,int led)=>frame.AsSpan(led*4,3).SequenceEqual(new byte[]{255,0,0});
        int[] Lit(byte[] frame)=>Enumerable.Range(0,frame.Length/4).Where(i=>frame[i*4]!=0||frame[i*4+1]!=0||frame[i*4+2]!=0).ToArray();
        bool all=true;
        foreach(var key in KeyboardLightingMap.Keys)
        {
            var one=new KeyboardProfile{Color="#000000",KeyColors=new(){{key.LogicCode,"#FF0000"}}};
            var frame=ProfileCompiler.Frame(one,null,0);
            all&=IsRed(frame,key.LocationCode)&&Lit(frame).SequenceEqual(new[]{key.LocationCode});
        }
        check(all,"all 103 displayed keys light exactly their own LED, including bottom row");
        var profile=new KeyboardProfile{Color="#000000",KeyColors=letters.ToDictionary(k=>k.LogicCode,_=>"#FF0000")};
        check(Lit(ProfileCompiler.Frame(profile,null,0)).SequenceEqual(Enumerable.Range(46,10)),"Q–P static colors leave number row and stray slots untouched");
        var compiled=ProfileCompiler.Compile(profile,new Dictionary<int,uint>(),onboard:true);
        check(Lit(compiled.Colors).SequenceEqual(Enumerable.Range(46,10)),"stored static colors use the same Q–P LED mapping");
        var effect=new LightingEffect{Frames=[new(){Data=letters.ToDictionary(k=>k.LogicCode,_=>"#FF0000")}],Rules=[new(){Type=0,Color="#FF0000",Keys=letters.Select(k=>k.LogicCode).ToList()}]};
        profile.Effects=[effect];profile.EffectId=effect.Id;profile.Keys[26]="A";profile.KeyEffects[26]=effect.Id;
        check(Lit(ProfileCompiler.Frame(profile,effect,0)).SequenceEqual(Enumerable.Range(46,10)),"live effect frame translates Q–P into LED locations");
        compiled=ProfileCompiler.Compile(profile,new Dictionary<int,uint>(),onboard:true);
        check(BinaryPrimitives.ReadUInt32LittleEndian(compiled.Keys.AsSpan(26*4))==KeyCatalog.Encode("A")&&compiled.KeyEffects[26]==1&&compiled.KeyEffects[46]==255,"bindings and key-press trigger indices stay logical");
        var bytes=compiled.EffectBytes;
        int frames=(int)BinaryPrimitives.ReadUInt32LittleEndian(bytes),rules=(int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8));
        int[] Bits(int at)=>Enumerable.Range(0,176).Where(i=>(bytes[at+i/8]&(1<<(i%8)))!=0).ToArray();
        check(Bits(frames+4).SequenceEqual(Enumerable.Range(46,10)),"stored lighting frame bitmap maps Q–P to LED slots");
        check(Bits(rules+2).SequenceEqual(Enumerable.Range(46,10)),"stored lighting color-rule bitmap maps Q–P to LED slots");
        effect.Rules.Clear();compiled=ProfileCompiler.Compile(profile,new Dictionary<int,uint>(),onboard:true);
        frames=(int)BinaryPrimitives.ReadUInt32LittleEndian(compiled.EffectBytes);
        check(Lit(compiled.EffectBytes.AsSpan(frames+4,704).ToArray()).SequenceEqual(Enumerable.Range(46,10)),"stored RGB effect frames translate Q–P as well");
        var legacy=LegacyImport.Effect(LegacyImport.EffectJson(effect));
        check(legacy.CoordinateSpace=="Led"&&legacy.Frames[0].Data.Keys.Order().SequenceEqual(Enumerable.Range(46,10)),"legacy LE export stores physical LED locations");
        check(Lit(ProfileCompiler.Frame(profile,legacy,0)).SequenceEqual(Enumerable.Range(46,10)),"legacy LE import does not translate physical LED positions twice");
        var imported=LegacyImport.Profile(LegacyImport.ProfileJson(profile,new()));
        check(imported.KeyColors[26]=="#FF0000"&&imported.KeyColors[32]=="#FF0000","CMF color import translates LED positions back to editor keys");
        var json=LegacyImport.ProfileJson(profile,new());
        check(json["LEData"]!["46"]!.ToString()=="0xFF0000"&&json["LEData"]!["24"]!.ToString()=="0x000000","CMF exports Q as LED 46 and leaves number 1 black");
        using var temporary=new CheckDirectory(root);var folder=temporary.Path;
        var old=new KeyboardProfile{Effects=[new(){EditableZones=true,Frames=[new(){Data=Enumerable.Range(0,112).ToDictionary(k=>k,_=>"#FF0000")}]}]};
        var node=JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(old))!;node["Effects"]![0]!.AsObject().Remove("CoordinateSpace");var file=Path.Combine(folder,"old-native.waive");var text=node.ToJsonString();File.WriteAllText(file,text);
        check(ProfileStore.Read(file).Effects[0].CoordinateSpace=="Logical"&&File.ReadAllText(file)==text,"old native-generated effects keep switch intent without rewriting files");
        node=JsonNode.Parse(System.Text.Json.JsonSerializer.Serialize(new KeyboardProfile{Effects=[legacy]}))!;node["Effects"]![0]!.AsObject().Remove("CoordinateSpace");file=Path.Combine(folder,"old-import.waive");File.WriteAllText(file,node.ToJsonString());
        check(ProfileStore.Read(file).Effects[0].CoordinateSpace=="Led","old imported sparse LE effects retain their LED coordinates");
        using var fake=new FakeTransport();await new LightingController(fake).ApplyProfileLiveAsync(profile,new Dictionary<int,uint>(),112);
        var sent=new byte[528];foreach(var c in fake.Commands.Where(c=>c.Code==0x1a&&c.Subcommand==1))c.Payload!.CopyTo(sent,(int)(c.Header&0xffff));
        check(Lit(sent).SequenceEqual(Enumerable.Range(46,10)),"actual queued USB payload lights only Q–P LEDs");
    }
}

