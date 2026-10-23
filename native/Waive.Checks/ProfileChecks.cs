using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using Waive.Core;

static class ProfileChecks
{
    public static async Task Run(string root,Action<bool,string> check)
    {
        var selection=new HashSet<int>();var stroke=new KeySelectionStroke(selection,false);
        var bounds=Enumerable.Range(0,4).Select(i=>new KeyBounds(i,i*25,0,20,20)).ToArray();
        foreach(var key in bounds)if(key.Crosses(5,10,90,10))stroke.Visit(key.Index);
        check(selection.SetEquals(new[]{0,1,2,3}),"fast drag selects intermediate keys across gaps");
        stroke=new KeySelectionStroke(selection,true);stroke.Visit(1);stroke.Visit(1);check(!selection.Contains(1)&&selection.Count==3,"subtract stroke never toggles revisited keys back on");
        stroke=new KeySelectionStroke(selection,false);stroke.Visit(1);stroke.Visit(1);check(selection.Count==4,"add stroke keeps existing selection and ignores revisits");
        check(!bounds[0].Crosses(-10,25,100,25)&&bounds[0].Crosses(10,-5,10,30)&&bounds[0].Crosses(10,10,10,10),"drag intersection handles misses, vertical lines, and stationary points");
        check(!bounds[0].Crosses(30,10,30,10)&&bounds[0].Crosses(30,30,0,0),"drag intersection handles outside points and reversed diagonal");
        void Reject(Action action,string name){try{action();throw new Exception("Accepted invalid input: "+name);}catch(FormatException){check(true,name);}}
        using var vectors=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"reference","legacy-file-vectors.json")));
        MacroDefinition? macro=null;LightingEffect? effect=null;
        foreach(var vector in vectors.RootElement.EnumerateArray())
        {
            var type=vector.GetProperty("type").GetByte();var name=vector.GetProperty("name").GetString()!;var body=vector.GetProperty("body").GetString()!;
            var bytes=Convert.FromBase64String(vector.GetProperty("bytesBase64").GetString()!);
            check(LegacyCodec.Encode(type,name,body,1700000000).SequenceEqual(bytes),"legacy file encoder matches original type "+type);
            check(LegacyCodec.Decode(bytes)==new PackedContent(type,name,body),"legacy file decoder type "+type);
            var corrupt=bytes.ToArray();corrupt[^1]^=1;Reject(()=>LegacyCodec.Decode(corrupt),"reject corrupt legacy body "+type);
            corrupt=bytes.ToArray();corrupt[8]^=1;Reject(()=>LegacyCodec.Decode(corrupt),"reject corrupt legacy header "+type);
            Reject(()=>LegacyCodec.Decode(bytes[..^1]),"reject truncated legacy file "+type);
            if(type==5)macro=LegacyImport.Macro(body);
            if(type==2)effect=LegacyImport.Effect(JsonNode.Parse(body)!.AsObject());
        }
        check(macro!.Events[0].Value=="LShift"&&macro.Events[^2].Kind=="MouseDown"&&macro.Events[^1].Kind=="MouseUp","legacy keyboard and mouse macro import");
        check(LegacyImport.Macro(LegacyImport.MacroText(macro)).Events.Count==7,"macro text export round trip");
        check(LegacyImport.MacroText(macro).Contains("\"Left Shift\""),"export uses original modifier names");
        check(KeyCatalog.Normalize("Num +")=="NumpadAdd"&&KeyCatalog.Normalize("Page Up")=="PageUp"&&KeyCatalog.Normalize(",")=="Comma","legacy punctuation and keypad names");
        check(LegacyImport.EffectJson(effect!)["LEConfigs"]![0]!["Color"]!.ToString()=="0xFF0000","effect export uses legacy hexadecimal colors");
        var block=macro.Compile(3);
        check(block.Length==512&&block[4]==8&&block[5]==3&&BinaryPrimitives.ReadUInt16LittleEndian(block)==0x55aa,"onboard macro header and terminal event");
        check(BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(2))==Protocol.Crc(block.AsSpan(8,32)),"onboard macro event checksum");
        check(KeyCatalog.Encode("LCtrl+Up")==0x02005201&&KeyCatalog.Encode("MediaPlayPause")==0x030000cd,"shortcut and media encoding");
        check(KeyCatalog.Decode(KeyCatalog.Encode("RAlt+F24"))=="RAlt+F24","extended key decode round trip");
        check(KeyCatalog.Encode("Disabled")==0&&KeyCatalog.Encode("Caps Lock")==0x02003900,"disabled key and old key alias");
        Reject(()=>KeyCatalog.Encode("unknown"),"reject unknown key assignment");
        HostAction.Validate("https://example.com");Reject(()=>HostAction.Validate("start something"),"host action rejects shell command text");
        var hostProfile=new KeyboardProfile{Keys=new(){{1,"open:https://example.com"}}};
        check(ProfileCompiler.Compile(hostProfile,new Dictionary<int,uint>()).NeedsReports,"host action requests key notifications");
        hostProfile.Mode=2;Reject(()=>hostProfile.Validate(),"host actions require live mode");hostProfile.Mode=0;
        var importedHost=LegacyImport.Profile(LegacyImport.ProfileJson(hostProfile,new()));check(importedHost.Keys[1]==hostProfile.Keys[1],"legacy host action round trip");
        var defaults=new Dictionary<int,uint>{{1,KeyCatalog.Encode("A")},{2,KeyCatalog.Encode("B")}};
        var profile=new KeyboardProfile{Mode=2,Name="Regression",Macros=[macro],Effects=[effect!],EffectId=effect!.Id,Keys=new(){{1,"macro:"+macro.Id}},FnKeys=new(){{2,"macro:"+macro.Id}},KeyEffects=new(){{1,effect.Id}},KeyColors=new(){{47,"#0080FF"}}};
        var compiled=ProfileCompiler.Compile(profile,defaults,onboard:true);
        check(BinaryPrimitives.ReadUInt32LittleEndian(compiled.Keys.AsSpan(4))==0x0a010000&&BinaryPrimitives.ReadUInt32LittleEndian(compiled.FnKeys.AsSpan(8))==0x0a010000,"shared base and Fn macro use same stored index");
        check(compiled.MacroBytes.Length==512&&compiled.KeyEffects[1]==1,"deduplicated onboard dependencies");
        check(compiled.Colors.AsSpan(0,4).SequenceEqual(new byte[]{0,128,255,100}),"Escape colors map switch 47 to LED 0 and retain brightness");
        check(compiled.EffectBytes.Length>512&&compiled.NeedsReports,"effect directory and live report requirements");
        var plan=ProfileCompiler.StoredPlan(profile,defaults);
        check(plan.Any(s=>s.Command.Code==0x31)&&plan.Any(s=>s.Command.Code==0x25)&&plan.Any(s=>s.Command.Code==0x27),"stored plan includes Fn, macros, and effects");
        var builtIn=new KeyboardProfile{Mode=1};
        check(ProfileCompiler.StoredPlan(builtIn,defaults).All(s=>s.Command.Code is not (0x22 or 0x31)),"built-in layer preserves key maps");
        var bad=profile.Clone();bad.Macros.Clear();Reject(()=>ProfileCompiler.StoredPlan(bad,defaults),"missing macro rejected before write plan");
        bad=profile.Clone();bad.Mode=1;Reject(()=>bad.Validate(),"built-in layer rejects remapping");
        bad=profile.Clone();bad.Keys=null!;Reject(()=>bad.Validate(),"missing JSON collections rejected");
        bad=profile.Clone();bad.Macros[0].Events=Enumerable.Range(0,126).Select(_=>new MacroEvent()).ToList();Reject(()=>ProfileCompiler.StoredPlan(bad,defaults),"oversized onboard macro rejected before writing");
        var oversized=new KeyboardProfile{Mode=2,Effects=[new LightingEffect{Frames=[new EffectFrame{Count=1000,Data=new(){{1,"#FFFFFF"}}}]}]};oversized.EffectId=oversized.Effects[0].Id;
        Reject(()=>ProfileCompiler.StoredPlan(oversized,defaults),"lighting capacity checked before writing");
        var frameEffect=new LightingEffect{Frames=[new(){Count=2,Data=new(){{1,"#FF0000"}}},new(){Count=1,Data=new(){{1,"#0000FF"}}}]};
        check(frameEffect.FrameAt(1)[1]=="#FF0000"&&frameEffect.FrameAt(2)[1]=="#0000FF"&&frameEffect.FrameAt(3)[1]=="#FF0000","frame duration and looping");
        using var temporary=new CheckDirectory(root);var folder=temporary.Path;
        var store=new ProfileStore(folder);store.Save(profile);var saved=store.Load().Single();check(saved.FnKeys.Count==1&&saved.Macros.Count==1,"native profile bundle save and load");
        profile.Name="Edited";store.Save(profile);check(Directory.GetFiles(folder,"*.bak").Length==1,"save preserves previous profile backup");
        File.WriteAllText(Path.Combine(folder,"broken.waive"),"{\"Keys\":null}");check(store.Load().Count==1&&store.Warnings.Count==1,"unreadable saved profile reported and preserved");
        foreach(var extension in new[]{".waive",".gpr"})
        {
            profile.Brightness=43;profile.ReturnLayer=3;
            var path=Path.Combine(folder,"roundtrip"+extension);ProfileTransfer.Export(path,profile,defaults);var imported=ProfileTransfer.Import(path).Single();
            check(imported.Macros.Single().Id==macro.Id&&imported.Effects.Single().Id==effect.Id&&imported.KeyEffects[1]==effect.Id,"complete profile transfer "+extension);
            check(imported.Brightness==43&&imported.ReturnLayer==3,"transfer preserves brightness and return layer "+extension);
        }
        store.Delete(profile);check(!File.Exists(Path.Combine(folder,profile.Id.ToString("N")+".waive"))&&Directory.GetFiles(folder,"*.deleted-*").Length==1,"profile deletion keeps recoverable file");
        using var fake=new FakeTransport();var controller=new LightingController(fake);
        string? requested=null;await using(var playback=new LivePlayback(fake,controller,hostProfile)){playback.OpenRequested+=target=>requested=target;playback.Key(1,false);check(requested is null,"release does not launch host action");playback.Key(1,true);check(requested=="https://example.com","press dispatches host action without spawning a process in tests");}
        var invalid=profile.Clone();invalid.Macros.Clear();
        try{await controller.ApplyProfileStoredAsync(invalid,defaults,112);throw new Exception("Invalid profile written");}catch(FormatException){check(fake.Commands.Count==0,"invalid stored profile sends zero device commands");}
        fake.FailFlashWrites=true;
        try{await controller.ApplyProfileStoredAsync(profile,defaults,112);throw new Exception("Rejected write ignored");}catch(IOException){check(fake.Commands.Count(c=>c.IsStoredWrite)==1,"negative flash ACK aborts stored operation immediately");}
        fake.FailFlashWrites=false;fake.Commands.Clear();
        var live=new KeyboardProfile{KeyColors=new(){{1,"#FF0000"}}};await controller.ApplyProfileLiveAsync(live,defaults,112);
        check(fake.Commands.Last().Payload is [0]&&fake.Commands.Last().Subcommand==3,"static live profile disables key reports");
        fake.Commands.Clear();
        await using(var playback=new LivePlayback(fake,controller,live)){playback.Start();await Task.Delay(150);check(fake.Commands.Count==0,"static live playback has no rendering loop");}
        live.Effects=[frameEffect];live.KeyEffects[1]=frameEffect.Id;fake.Commands.Clear();
        await using(var playback=new LivePlayback(fake,controller,live)){playback.Start();await Task.Delay(150);check(fake.Commands.Count==0,"key-press lighting sleeps until triggered");playback.Key(1,true);await Task.Delay(350);}
        check(fake.Commands.Any(c=>c==Protocol.CommitLive),"key-press lighting generates committed frames");
        fake.Commands.Clear();live.KeyEffects.Clear();live.Macros=[new(){Events=[new(){Kind="KeyDown",Value="A"},new(){Kind="Delay",Value="5000"}]}];live.Keys[1]="macro:"+live.Macros[0].Id;
        await using(var playback=new LivePlayback(fake,controller,live)){playback.Key(1,true);await Task.Delay(80);}
        check(fake.Commands.Any(c=>c.Code==0x15&&c.Subcommand==2&&c.Payload![1]==4)&&fake.Commands.Last(c=>c.Subcommand==2).Payload!.All(b=>b==0),"cancelled macro releases held keys");
    }
}
