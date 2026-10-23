using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Waive.Core;

public sealed record PackedContent(byte Type,string Name,string Body);
public static class LegacyCodec
{
    static readonly string[] Names=["_","PROFILE","LIGHT","STATASTIC","APPCONF","MACRO"];
    static ushort Seed(byte type,string name)
    {
        if(type<1||type>=Names.Length)throw new FormatException("Unsupported packed file type.");
        var bytes=new byte[8];Encoding.Latin1.GetBytes(name.AsSpan(),bytes);
        return Update(Protocol.Crc(Encoding.ASCII.GetBytes(Names[type])),bytes);
    }
    public static ushort Update(ushort crc,ReadOnlySpan<byte> bytes)
    {
        foreach(var value in bytes){crc^=(ushort)(value<<8);for(int bit=0;bit<8;bit++)crc=(ushort)((crc&0x8000)!=0?(crc<<1)^0x1021:crc<<1);}
        return crc;
    }
    public static byte[] Encode(byte type,string name,string text,uint? timestamp=null)
    {
        if(name.Length>8)throw new FormatException("Packed file name is too long.");
        var plain=Encoding.UTF8.GetBytes(text);if(plain.Length>8*1024*1024)throw new FormatException("File exceeds 8 MiB.");
        var data=new byte[32+plain.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(data,0x434d4631);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8),timestamp??checked((uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12),(uint)plain.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(16),Protocol.Crc(plain));data[20]=type;
        Encoding.Latin1.GetBytes(name).CopyTo(data,24);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4),Protocol.Crc(data.AsSpan(0,32)));
        var crc=Seed(type,name);
        for(int i=0;i<plain.Length;i++){data[32+i]=(byte)(plain[i]^(crc&255));crc=Update(crc,plain.AsSpan(i,1));}
        return data;
    }
    public static PackedContent Decode(byte[] data)
    {
        if(data.Length<32 || data.Length>8*1024*1024+32 || BinaryPrimitives.ReadUInt32LittleEndian(data)!=0x434d4631)throw new FormatException("This is not a valid GK6+ packed file.");
        if(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12))!=data.Length-32)throw new FormatException("The packed file is incomplete.");
        var header=data.AsSpan(0,32).ToArray();var expected=BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));header.AsSpan(4,4).Clear();
        if(Protocol.Crc(header)!=expected)throw new FormatException("The file header checksum does not match.");
        var type=data[20];var name=Encoding.Latin1.GetString(data,24,8).TrimEnd('\0');var crc=Seed(type,name);var body=new byte[data.Length-32];
        for(int i=0;i<body.Length;i++){body[i]=(byte)(data[32+i]^(crc&255));crc=Update(crc,body.AsSpan(i,1));}
        if(Protocol.Crc(body)!=BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(16)))throw new FormatException("The file data checksum does not match.");
        return new(type,name,new UTF8Encoding(false,true).GetString(body).TrimStart('\ufeff'));
    }
}

public static class LegacyImport
{
    public static MacroDefinition Macro(string text,string? name=null)
    {
        var macro=new MacroDefinition();var section="";
        foreach(var raw in text.Split(['\r','\n'],StringSplitOptions.RemoveEmptyEntries))
        {
            var line=raw.Trim();if(line.StartsWith('[')){section=line;continue;}
            if(section=="[General]")
            {
                var kv=line.Split('=',2);if(kv.Length<2)continue;
                if(kv[0]=="Name")macro.Name=kv[1];
                if(kv[0]=="ScriptID" && Guid.TryParse(kv[1],out var id))macro.Id=id;
                if(kv[0]=="Repeats" && int.TryParse(kv[1],out var repeats))macro.Repeats=Math.Clamp(repeats,1,255);
                if(kv[0]=="StopMode" && int.TryParse(kv[1],out var stop))macro.StopMode=Math.Clamp(stop,1,3);
            }
            else if(section=="[Script]")
            {
                var parts=line.Split(' ',2);var kind=parts[0];var value=parts.Length==2?parts[1].Trim().Trim('"'):"";
                if(kind is "Delay" or "KeyDown" or "KeyUp")macro.Events.Add(new(){Kind=kind,Value=kind=="Delay"?value:KeyCatalog.Normalize(value)});
                else if(new[]{"LeftDown","LeftUp","RightDown","RightUp","MiddleDown","MiddleUp","MouseLeftDown","MouseLeftUp","MouseRightDown","MouseRightUp","MouseMiddleDown","MouseMiddleUp"}.Contains(kind))macro.Events.Add(new(){Kind=kind.EndsWith("Down")?"MouseDown":"MouseUp",Value=kind.Contains("Right")?"Right":kind.Contains("Middle")?"Middle":"Left"});
                else throw new FormatException("Unsupported macro event: "+kind);
            }
        }
        if(name is not null)macro.Name=name;macro.Validate();return macro;
    }
    public static string MacroText(MacroDefinition macro)
    {
        macro.Validate();var lines=new List<string>{"[General]","Name="+macro.Name,"ScriptID="+macro.Id,"Repeats="+macro.Repeats,"StopMode="+macro.StopMode,"","[Script]"};
        foreach(var e in macro.Events)lines.Add(e.Kind.StartsWith("Mouse")?e.Value+(e.Kind=="MouseDown"?"Down":"Up"):e.Kind+" "+(e.Kind=="Delay"?e.Value:'"'+KeyCatalog.LegacyName(e.Value)+'"'));
        return string.Join("\r\n",lines);
    }
    public static LightingEffect Effect(JsonObject json)
    {
        if(json["Canvases"] is JsonArray)throw new FormatException("Grouped/game effects need a separate port. Import a simple lighting effect.");
        var effect=new LightingEffect{Name=json["Name"]?.ToString()??"Imported effect",CoordinateSpace="Led"};
        if(Guid.TryParse(json["GUID"]?.ToString(),out var id))effect.Id=id;
        foreach(var frame in json["Frames"]?.AsArray()??throw new FormatException("No effect frames found."))
        {
            var f=new EffectFrame{Count=Int(frame?["Count"],1)};
            foreach(var pair in frame?["Data"]?.AsObject()??[])f.Data[int.Parse(pair.Key,CultureInfo.InvariantCulture)]=Color(pair.Value);
            effect.Frames.Add(f);
        }
        foreach(var rule in json["LEConfigs"]?.AsArray()??[])
            effect.Rules.Add(new(){Type=Int(rule?["Type"]),Color=Color(rule?["Color"]),Count=Math.Max(1,Int(rule?["Count"],10)),StayCount=Int(rule?["StayCount"],5),Keys=(rule?["Keys"]?.AsArray()??[]).Select(k=>Int(k)).ToList()});
        effect.Validate();return effect;
    }
    public static JsonObject EffectJson(LightingEffect effect)=>new()
    {
        ["GUID"]=effect.Id.ToString().ToUpperInvariant(),["Name"]=effect.Name,
        ["Frames"]=JsonSerializer.SerializeToNode(effect.Frames.Select(f=>new{f.Count,Data=f.Data.Where(k=>KeyboardLightingMap.EffectLed(effect,k.Key).HasValue).ToDictionary(k=>KeyboardLightingMap.EffectLed(effect,k.Key)!.Value,k=>"0x"+Rgb.Parse(k.Value).Hex[1..])})),
        ["LEConfigs"]=JsonSerializer.SerializeToNode(effect.Rules.Select(r=>new{r.Type,Color="0x"+Rgb.Parse(r.Color).Hex[1..],r.Count,r.StayCount,Keys=KeyboardLightingMap.EffectLeds(effect,r.Keys).ToArray()}))
    };
    static int Int(JsonNode? node,int fallback=0)=>int.TryParse(node?.ToString(),out var value)?value:fallback;
    static string Color(JsonNode? node)
    {
        var value=node?.ToString()??"000000";value=value.Replace("0x","",StringComparison.OrdinalIgnoreCase).TrimStart('#');
        if(value.Length<=6 && uint.TryParse(value,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var color))return $"#{color:X6}";
        return Rgb.Parse(value).Hex;
    }
    public static KeyboardProfile Profile(JsonObject json)
    {
        var profile=new KeyboardProfile{Name=json["Name"]?.ToString()??"Imported profile",Mode=Int(json["ModeIndex"]),ModelId=uint.TryParse(json["ModelID"]?.ToString(),out var model)&&model!=0?model:656801936};
        // Some vendor templates carry an unrelated ModelID; caller checks the device folder when importing templates.
        foreach(var (field,target) in new[]{("KeySet",profile.Keys),("FnKeySet",profile.FnKeys)})
            foreach(var key in json[field]?.AsArray()??[])
            {
                int index=Int(key?["Index"],-1);KeyboardProfile.CheckIndex(index,112);
                var task=key?["Task"];
                if(task?["Type"]?.ToString()=="Macro" && Guid.TryParse(task["Data"]?["GUID"]?.ToString(),out var macro))
                {
                    target[index]="macro:"+macro;
                    profile.Macros.Add(new(){Id=macro,Name=key?["MenuName"]?.ToString()??"Missing macro",Repeats=Int(task["Data"]?["Repeats"],1),StopMode=Int(task["Data"]?["StopMode"],1)});
                }
                else if(task?["Type"]?.ToString()=="OpenURL")
                {var path=task["Data"]?["AppPath"]?.ToString()??"";HostAction.Validate(path);target[index]="open:"+path;}
                else if(task?["Type"] is not null)throw new FormatException("Unsupported legacy action: "+task["Type"]);
                else if(key?["DriverValue"] is JsonNode driver)
                {var value=driver.ToString();if(value.StartsWith("0x",StringComparison.OrdinalIgnoreCase))target[index]=KeyCatalog.Decode(uint.Parse(value[2..],NumberStyles.HexNumber,CultureInfo.InvariantCulture));else{HostAction.Validate(value);target[index]="open:"+value;}}
                if(Guid.TryParse(key?["KeyLE"]?["GUID"]?.ToString(),out var keyEffect))profile.KeyEffects[index]=keyEffect;
            }
        var colors=json["ModeLE"]?["LEData"] as JsonObject ?? json["LEData"] as JsonObject;
        if(colors is not null)foreach(var (key,value) in colors){int index=int.Parse(key,CultureInfo.InvariantCulture);if(KeyboardLightingMap.KeyForLed(index) is int logical)profile.KeyColors[logical]=Color(value);}
        if(Guid.TryParse(json["ModeLE"]?["GUID"]?.ToString(),out var effectId))profile.EffectId=effectId;
        profile.Macros=profile.Macros.DistinctBy(m=>m.Id).ToList();
        if(profile.Mode==1){profile.Keys.Clear();profile.FnKeys.Clear();}
        profile.Brightness=Int(json["WaiveBrightness"],100);profile.ReturnLayer=Int(json["WaiveReturnLayer"],profile.Mode>0?profile.Mode:1);
        return profile;
    }
    public static JsonObject ProfileJson(KeyboardProfile profile,Dictionary<int,uint> defaults)
    {
        profile.Validate();
        JsonArray Keys(Dictionary<int,string> bindings,bool fn)
        {
            var entries=fn?new Dictionary<int,uint>():new(defaults);
            var result=new JsonArray();
            foreach(var index in entries.Keys.Union(bindings.Keys).Order())
            {
                var binding=bindings.GetValueOrDefault(index);var macro=binding?.StartsWith("macro:")==true?profile.Macros.Single(m=>m.Id==Guid.Parse(binding[6..])):null;
                bool host=binding?.StartsWith("open:")==true;
                uint value=macro is not null||host?0x0a010001:binding is null?entries[index]:KeyCatalog.Encode(binding);
                var item=new JsonObject{["Index"]=index,["DriverValue"]=$"0x{value:X8}",["MenuName"]=binding??KeyCatalog.Decode(value)};
                if(macro is not null)item["Task"]=new JsonObject{["Type"]="Macro",["Data"]=new JsonObject{["GUID"]=macro.Id.ToString().ToUpperInvariant(),["Repeats"]=macro.Repeats,["StopMode"]=macro.StopMode}};
                if(host)item["Task"]=new JsonObject{["Type"]="OpenURL",["Data"]=new JsonObject{["AppPath"]=binding![5..]}};
                if(profile.KeyEffects.TryGetValue(index,out var effect))item["KeyLE"]=new JsonObject{["GUID"]=effect.ToString().ToUpperInvariant(),["Name"]=profile.Effects.FirstOrDefault(e=>e.Id==effect)?.Name??""};
                result.Add(item);
            }
            return result;
        }
        var colors=new JsonObject();foreach(var key in KeyboardLightingMap.Keys)colors[key.LocationCode.ToString()]=(profile.KeyColors.GetValueOrDefault(key.LogicCode,profile.Color)).Replace("#","0x");
        return new(){["GUID"]=profile.Id.ToString().ToUpperInvariant(),["Name"]=profile.Name,["ModelID"]=profile.ModelId,["ModeIndex"]=profile.Mode,["WaiveBrightness"]=profile.Brightness,["WaiveReturnLayer"]=profile.ReturnLayer,["Active"]=1,["KeySet"]=Keys(profile.Keys,false),["FnKeySet"]=Keys(profile.FnKeys,true),["LEData"]=colors.DeepClone(),["ModeLE"]=new JsonObject{["GUID"]=profile.EffectId?.ToString().ToUpperInvariant()??"",["LEData"]=colors}};
    }
}

public sealed class ProfileStore(string directory)
{
    public List<string> Warnings { get; } = [];
    public static readonly JsonSerializerOptions Json = new(){WriteIndented=true};
    public IReadOnlyList<KeyboardProfile> Load()
    {
        Directory.CreateDirectory(directory);
        Warnings.Clear();
        var profiles=new List<KeyboardProfile>();
        foreach(var file in Directory.EnumerateFiles(directory,"*.waive"))
        {
            try { var profile=Read(file); profiles.Add(profile); }
            catch(Exception error) when(error is IOException or JsonException or FormatException)
            { Warnings.Add(Path.GetFileName(file)+": "+error.Message); }
        }
        return profiles;
    }
    public static KeyboardProfile Read(string file)
    {
        if(new FileInfo(file).Length>8*1024*1024)throw new FormatException("Profile exceeds 8 MiB.");
        var text=File.ReadAllText(file);
        var profile=JsonSerializer.Deserialize<KeyboardProfile>(text,Json)??throw new FormatException("Empty profile.");
        using var document=JsonDocument.Parse(text);
        if(document.RootElement.TryGetProperty("Effects",out var effects)&&effects.ValueKind==JsonValueKind.Array&&profile.Effects is not null)
        {
            int index=0;
            foreach(var source in effects.EnumerateArray())
            {
                var effect=profile.Effects[index++];if(effect is null||source.ValueKind!=JsonValueKind.Object||source.TryGetProperty("CoordinateSpace",out _))continue;
                // Old Waive-generated effects used a full 112-switch frame (or quick-zone flag).
                // Legacy LE libraries already use LED locations and must not be translated twice.
                bool generated=effect.EditableZones||(effect.Frames is {Count:>0}&&effect.Frames.All(f=>f?.Data is not null&&f.Data.Count==112&&f.Data.Keys.All(k=>k>=0&&k<112)));
                effect.CoordinateSpace=generated?"Logical":"Led";
            }
        }
        profile.Validate();return profile;
    }
    public void Save(KeyboardProfile profile)
    {
        profile.Validate();Directory.CreateDirectory(directory);
        var target=Path.Combine(directory,profile.Id.ToString("N")+".waive");var temporary=target+".tmp";
        File.WriteAllText(temporary,JsonSerializer.Serialize(profile,Json));
        if(File.Exists(target))File.Copy(target,target+".bak",true);
        File.Move(temporary,target,true);
    }
    public void Delete(KeyboardProfile profile)
    {
        var file=Path.Combine(directory,profile.Id.ToString("N")+".waive");
        if(File.Exists(file))File.Move(file,file+".deleted-"+DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }
}
