using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Waive.Core;

public sealed class KeyboardProfile
{
    public int Schema { get; set; } = 1;
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "My keyboard";
    public uint ModelId { get; set; } = 656801936;
    public int Mode { get; set; }
    public int ReturnLayer { get; set; } = 1;
    public string Color { get; set; } = "#68E4CC";
    public int Brightness { get; set; } = 100;
    public Dictionary<int,string> KeyColors { get; set; } = [];
    public Dictionary<int,string> Keys { get; set; } = [];
    public Dictionary<int,string> FnKeys { get; set; } = [];
    public Dictionary<int,Guid> KeyEffects { get; set; } = [];
    public List<MacroDefinition> Macros { get; set; } = [];
    public List<LightingEffect> Effects { get; set; } = [];
    public Guid? EffectId { get; set; }
    public KeyboardProfile Clone() => JsonSerializer.Deserialize<KeyboardProfile>(JsonSerializer.Serialize(this))!;
    public void Validate(int slots = 112)
    {
        if(KeyColors is null || Keys is null || FnKeys is null || KeyEffects is null || Macros is null || Effects is null || Color is null || Keys.Values.Any(x=>x is null) || FnKeys.Values.Any(x=>x is null) || Macros.Any(x=>x is null) || Effects.Any(x=>x is null)) throw new FormatException("The profile contains missing fields.");
        foreach(var macro in Macros) macro.Validate();
        foreach(var effect in Effects) effect.Validate();
        if (Schema != 1 || ModelId != 656801936 || Id == Guid.Empty) throw new FormatException("This profile uses an unsupported format or keyboard model.");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 100) throw new FormatException("Enter a profile name of 1–100 characters.");
        if (Mode is < 0 or > 4 || ReturnLayer is < 1 or > 4 || Brightness is < 0 or > 100) throw new FormatException("Invalid target layer or brightness.");
        Rgb.Parse(Color);
        foreach (var (index,color) in KeyColors) { CheckIndex(index,slots); Rgb.Parse(color); }
        foreach (var bindings in new[] { Keys,FnKeys })
            foreach (var (index,binding) in bindings) { CheckIndex(index,slots); if(binding.StartsWith("open:")){HostAction.Validate(binding[5..]);if(Mode!=0)throw new FormatException("Opening a website, file, or app needs live mode.");} else if (binding.StartsWith("macro:",StringComparison.Ordinal)) { var id=Guid.Parse(binding[6..]); if(!Macros.Any(m=>m.Id==id && m.Events.Count>0)) throw new FormatException("A referenced macro is missing or empty. Import its CMS file before applying."); } else KeyCatalog.Encode(binding); }
        if (Macros.Select(m=>m.Id).Distinct().Count()!=Macros.Count || Effects.Select(e=>e.Id).Distinct().Count()!=Effects.Count) throw new FormatException("Library items have duplicate IDs.");
        foreach(var macro in Macros) macro.Validate();
        foreach(var effect in Effects) effect.Validate();
        if(EffectId is Guid effectId && !Effects.Any(e=>e.Id==effectId)) throw new FormatException("The selected lighting effect is missing. Import its LE file before applying.");
        foreach(var (index,effect) in KeyEffects) { CheckIndex(index,slots); if(!Effects.Any(e=>e.Id==effect)) throw new FormatException("A key-press effect is missing."); }
        if (Mode == 1 && (Keys.Count>0 || FnKeys.Count>0)) throw new FormatException("Built-in lighting cannot change key assignments. Choose onboard layer 2, 3, or 4.");
        if (Mode == 1 && KeyEffects.Count>0) throw new FormatException("Key-press lighting needs Live mode or onboard layer 1, 2, or 3. Built-in lighting supports the profile look only.");
    }
    public static void CheckIndex(int index,int slots) { if(index<0 || index>=slots) throw new FormatException($"Key index {index} is outside this keyboard's {slots}-key matrix."); }
    public override string ToString() => Name;
}

public static class HostAction
{
    public static void Validate(string target)
    {
        if(target.Length>2048 || target.Contains('\n') || target.Contains('\r') || target.Contains('"'))throw new FormatException("Choose a website address or a full file path without command arguments.");
        if(Uri.TryCreate(target,UriKind.Absolute,out var uri)&&uri.Scheme is "https" or "http")return;
        if(!Path.IsPathFullyQualified(target))throw new FormatException("Enter an https:// website address or choose a file or program.");
    }
}

public static class KeyCatalog
{
    public static readonly Dictionary<string,byte> Modifiers = new(StringComparer.OrdinalIgnoreCase) { ["LCtrl"]=1,["LShift"]=2,["LAlt"]=4,["LWin"]=8,["RCtrl"]=16,["RShift"]=32,["RAlt"]=64,["RWin"]=128 };
    public static readonly Dictionary<string,byte> Usages = Build();
    public static readonly Dictionary<string,ushort> Media = new(StringComparer.OrdinalIgnoreCase) { ["MediaPrev"]=0xb6,["MediaNext"]=0xb5,["MediaPlayPause"]=0xcd,["MediaStop"]=0xb7,["Mute"]=0xe2,["VolUp"]=0xe9,["VolDown"]=0xea,["BrowserBack"]=0x224,["BrowserForward"]=0x225,["BrowserHome"]=0x223,["Calculator"]=0x192 };
    static Dictionary<string,byte> Build()
    {
        var d=new Dictionary<string,byte>(StringComparer.OrdinalIgnoreCase);
        for(int i=0;i<26;i++)d[((char)('A'+i)).ToString()]=(byte)(4+i);
        for(int i=1;i<=9;i++)d[i.ToString()]=(byte)(0x1d+i);d["0"]=0x27;
        foreach(var (name,code) in new Dictionary<string,byte> { ["Enter"]=0x28,["Esc"]=0x29,["Backspace"]=0x2a,["Tab"]=0x2b,["Space"]=0x2c,["Minus"]=0x2d,["Equal"]=0x2e,["LeftBracket"]=0x2f,["RightBracket"]=0x30,["Backslash"]=0x31,["Semicolon"]=0x33,["Quote"]=0x34,["Backtick"]=0x35,["Comma"]=0x36,["Period"]=0x37,["Slash"]=0x38,["Caps"]=0x39,["PrintScreen"]=0x46,["ScrollLock"]=0x47,["Pause"]=0x48,["Insert"]=0x49,["Home"]=0x4a,["PageUp"]=0x4b,["Delete"]=0x4c,["End"]=0x4d,["PageDown"]=0x4e,["Right"]=0x4f,["Left"]=0x50,["Down"]=0x51,["Up"]=0x52,["NumLock"]=0x53,["NumpadDivide"]=0x54,["NumpadMultiply"]=0x55,["NumpadSubtract"]=0x56,["NumpadAdd"]=0x57,["NumpadEnter"]=0x58,["Numpad0"]=0x62,["NumpadDot"]=0x63,["App"]=0x65 })d[name]=code;
        for(int i=1;i<=12;i++)d["F"+i]=(byte)(0x39+i);for(int i=13;i<=24;i++)d["F"+i]=(byte)(0x68+i-13);
        for(int i=1;i<=9;i++)d["Numpad"+i]=(byte)(0x58+i);
        return d;
    }
    public static string Normalize(string input)
    {
        var value=input.Trim().Trim('"');
        return value.ToUpperInvariant() switch
        {
            "ESCAPE"=>"Esc","CAPSLOCK" or "CAPS LOCK"=>"Caps","LEFT CTRL" or "CTRL" or "CONTROL"=>"LCtrl","RIGHT CTRL"=>"RCtrl",
            "LEFT SHIFT" or "SHIFT"=>"LShift","RIGHT SHIFT" or "SHIFTRIGHT"=>"RShift","LEFT ALT" or "ALT"=>"LAlt","RIGHT ALT"=>"RAlt","LEFT WIN" or "WIN" or "META"=>"LWin","RIGHT WIN" or "METARIGHT"=>"RWin",
            "PAGE UP"=>"PageUp","PAGE DOWN"=>"PageDown","PRINT SCREEN"=>"PrintScreen","SCROLL LOCK"=>"ScrollLock","NUM LOCK"=>"NumLock",
            "-"=>"Minus","="=>"Equal","["=>"LeftBracket","]"=>"RightBracket","\\"=>"Backslash",";"=>"Semicolon","'"=>"Quote",","=>"Comma","`"=>"Backtick","."=>"Period","/"=>"Slash",
            "NUM +"=>"NumpadAdd","NUM -"=>"NumpadSubtract","NUM *"=>"NumpadMultiply","NUM /"=>"NumpadDivide","NUM ."=>"NumpadDot","NUM ENTER"=>"NumpadEnter",
            var s when s.Length==5&&s.StartsWith("NUM ")&&char.IsAsciiDigit(s[4])=>"Numpad"+s[4],_=>value
        };
    }
    public static string LegacyName(string input)
    {
        var value=Normalize(input);
        if(value.StartsWith("Numpad")&&value.Length==7&&char.IsAsciiDigit(value[6]))return "Num "+value[6];
        return value switch
        {
            "LCtrl"=>"Left Ctrl","RCtrl"=>"Right Ctrl","LShift"=>"Left Shift","RShift"=>"Right Shift","LAlt"=>"Left Alt","RAlt"=>"Right Alt","LWin"=>"Left Win","RWin"=>"Right Win",
            "Caps"=>"Caps Lock","PageUp"=>"Page Up","PageDown"=>"Page Down","PrintScreen"=>"Print Screen","ScrollLock"=>"Scroll Lock","NumLock"=>"Num Lock",
            "Minus"=>"-","Equal"=>"=","LeftBracket"=>"[","RightBracket"=>"]","Backslash"=>"\\","Semicolon"=>";","Quote"=>"'","Comma"=>",","Backtick"=>"`","Period"=>".","Slash"=>"/",
            "NumpadAdd"=>"Num +","NumpadSubtract"=>"Num -","NumpadMultiply"=>"Num *","NumpadDivide"=>"Num /","NumpadDot"=>"Num .","NumpadEnter"=>"Num Enter",_=>value
        };
    }
    public static IEnumerable<string> Names => new[]{"Disabled"}.Concat(Usages.Keys).Concat(Modifiers.Keys).Concat(Media.Keys);
    public static uint Encode(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression)) throw new FormatException("Choose a key, shortcut, or macro.");
        if (expression.StartsWith("0x",StringComparison.OrdinalIgnoreCase)) return uint.Parse(expression[2..],NumberStyles.HexNumber,CultureInfo.InvariantCulture);
        if(expression.Equals("Disabled",StringComparison.OrdinalIgnoreCase))return 0;
        var parts=expression.Split('+',StringSplitOptions.TrimEntries);
        byte mods=0;
        foreach(var p in parts[..^1]) { if(!Modifiers.TryGetValue(Normalize(p),out var m))throw new FormatException($"Unknown modifier: {p}");mods|=m; }
        var key=Normalize(parts[^1]);
        if(Modifiers.TryGetValue(key,out var modifier))return 0x02000000u|mods|modifier;
        if(Media.TryGetValue(key,out var media)) { if(mods!=0)throw new FormatException("Media keys cannot include modifiers.");return 0x03000000u|media; }
        if(!Usages.TryGetValue(key,out var usage))throw new FormatException($"Unknown key: {key}. Use a listed key or a shortcut such as LCtrl+Up.");
        return 0x02000000u | (uint)usage<<8 | mods;
    }
    public static string Decode(uint value)
    {
        if(value==0)return "Disabled";
        if(value>>24==3) { var media=Media.FirstOrDefault(x=>x.Value==(value&0xffff)); if(media.Key is not null)return media.Key; }
        if(value>>24==2)
        {
            var names=Modifiers.Where(x=>((byte)value & x.Value)!=0).Select(x=>x.Key).ToList();
            var key=(byte)(value>>8);
            if(key!=0) { var usage=Usages.FirstOrDefault(x=>x.Value==key);if(usage.Key is null)return $"0x{value:X8}";names.Add(usage.Key); }
            if(names.Count>0)return string.Join('+',names);
        }
        return $"0x{value:X8}";
    }
}

public sealed class MacroEvent
{
    public string Kind { get; set; } = "Delay";
    public string Value { get; set; } = "50";
}
public sealed class MacroDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New macro";
    public int Repeats { get; set; } = 1;
    public int StopMode { get; set; } = 1;
    public List<MacroEvent> Events { get; set; } = [];
    public void Validate()
    {
        if(Events is null || Events.Any(e=>e is null || e.Kind is null || e.Value is null))throw new FormatException("The macro contains missing events.");
        if(Id==Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Length>100)throw new FormatException("Enter a macro name.");
        if(Repeats is <1 or >255 || StopMode is <1 or >3 || Events.Count>2000)throw new FormatException("Invalid macro repeat count, stop mode, or event count.");
        foreach(var e in Events)
        {
            if(e.Kind=="Delay") { if(!int.TryParse(e.Value,out var delay)||delay<0||delay>65535)throw new FormatException("Macro delays must be between 0 and 65535 milliseconds."); }
            else if(e.Kind is "KeyDown" or "KeyUp") { var key=KeyCatalog.Normalize(e.Value);if(!KeyCatalog.Usages.ContainsKey(key)&&!KeyCatalog.Modifiers.ContainsKey(key))throw new FormatException("Choose a standard keyboard key for macro events: "+e.Value); }
            else if(e.Kind is "MouseDown" or "MouseUp") { if(e.Value is not ("Left" or "Right" or "Middle"))throw new FormatException("Choose Left, Right, or Middle for a mouse event."); }
            else throw new FormatException("Unknown macro event: "+e.Kind);
        }
    }
    public byte[] Compile(int index)
    {
        Validate();
        if(Events.Count==0 || Events.Count>125)throw new FormatException("Onboard macros need 1–125 events. Longer macros can run in live mode.");
        var data=new byte[512];
        var events=Events.Concat(new[]{new MacroEvent{Kind="Delay",Value="5"}}).ToArray();
        for(int i=0;i<events.Length;i++)
        {
            var e=events[i];var at=8+i*4;
            if(e.Kind=="Delay"){BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(at),ushort.Parse(e.Value,CultureInfo.InvariantCulture));data[at+3]=3;}
            else if(e.Kind.StartsWith("Mouse")){data[at]=(byte)(e.Value=="Left"?1:e.Value=="Right"?2:4);data[at+2]=(byte)(e.Kind=="MouseDown"?1:2);data[at+3]=2;}
            else { var name=KeyCatalog.Normalize(e.Value);if(KeyCatalog.Modifiers.TryGetValue(name,out var mod))data[at+1]=mod;else data[at]=KeyCatalog.Usages[name];data[at+2]=(byte)(e.Kind=="KeyDown"?1:2);data[at+3]=1; }
        }
        BinaryPrimitives.WriteUInt16LittleEndian(data,0x55aa);BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(2),Protocol.Crc(data.AsSpan(8,events.Length*4)));
        data[4]=(byte)events.Length;data[5]=checked((byte)index);data[6]=(byte)StopMode;data[7]=(byte)Repeats;
        return data;
    }
    public override string ToString()=>Name;
}

public sealed class EffectFrame
{
    public int Count { get; set; } = 1;
    public Dictionary<int,string> Data { get; set; } = [];
}
public sealed class EffectRule
{
    public int Type { get; set; }
    public string Color { get; set; } = "#68E4CC";
    public int Count { get; set; } = 10;
    public int StayCount { get; set; } = 5;
    public List<int> Keys { get; set; } = [];
}
public sealed class LightingEffect
{
    public string CoordinateSpace { get; set; } = "Logical";
    public bool EditableZones { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New effect";
    public List<EffectFrame> Frames { get; set; } = [];
    public List<EffectRule> Rules { get; set; } = [];
    public int DurationTicks => Frames.Sum(f=>f.Count);
    public void Validate()
    {
        if(Frames is null || Rules is null || Frames.Any(f=>f is null || f.Data is null) || Rules.Any(r=>r is null || r.Keys is null || r.Color is null))throw new FormatException("The lighting effect contains missing fields.");
        if(CoordinateSpace is not ("Logical" or "Led"))throw new FormatException("The effect uses an unknown lighting coordinate system.");
        if(Id==Guid.Empty || string.IsNullOrWhiteSpace(Name) || Frames.Count is <1 or >1000)throw new FormatException("A lighting effect needs a name and 1–1000 frames.");
        if(Frames.Any(f=>f.Count is <1 or >1000) || DurationTicks>10000)throw new FormatException("Effect duration exceeds 10000 ticks.");
        foreach(var f in Frames)foreach(var (index,color) in f.Data){KeyboardProfile.CheckIndex(index,CoordinateSpace=="Led"?176:112);Rgb.Parse(color);}
        if(Rules.Count>1000)throw new FormatException("Too many color rules.");
        foreach(var rule in Rules){if(rule.Type is <0 or >2 || rule.Count is <1 or >360 || rule.StayCount is <0 or >65535)throw new FormatException("Invalid effect timing.");Rgb.Parse(rule.Color);foreach(var index in rule.Keys)KeyboardProfile.CheckIndex(index,CoordinateSpace=="Led"?176:112);}
    }
    public Dictionary<int,string> FrameAt(long tick)
    {
        if(Frames.Count==0)return [];
        var current=(int)(tick%DurationTicks);var frame=Frames[0];
        foreach(var candidate in Frames){frame=candidate;if(current<candidate.Count)break;current-=candidate.Count;}
        var output=new Dictionary<int,string>(frame.Data);
        foreach(var rule in Rules)
        {
            var color=Rgb.Parse(rule.Color);
            if(rule.Type==1)color=ColorMath.HueShift(color,(tick*(360/rule.Count))%360);
            if(rule.Type==2){var cycle=2L*rule.Count+2L*rule.StayCount;var t=tick%Math.Max(1,cycle);double scale=t<rule.Count?(double)t/rule.Count:t<rule.Count+rule.StayCount?1:t<2*rule.Count+rule.StayCount?1-(double)(t-rule.Count-rule.StayCount)/rule.Count:0;color=ColorMath.Scale(color,scale);}
            foreach(var key in rule.Keys)if(output.ContainsKey(key))output[key]=color.Hex;
        }
        return output;
    }
    public override string ToString()=>Name;
}
public static class ColorMath
{
    public static Rgb Scale(Rgb c,double value)=>new((byte)Math.Clamp(Math.Round(c.R*value),0,255),(byte)Math.Clamp(Math.Round(c.G*value),0,255),(byte)Math.Clamp(Math.Round(c.B*value),0,255));
    public static Rgb HueShift(Rgb c,double degrees)
    {
        double r=c.R/255d,g=c.G/255d,b=c.B/255d,max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b)),delta=max-min;
        double hue=delta==0?0:max==r?60*((g-b)/delta%6):max==g?60*((b-r)/delta+2):60*((r-g)/delta+4);
        hue=((hue+degrees)%360+360)%360;double chroma=delta,x=chroma*(1-Math.Abs(hue/60%2-1)),m=max-chroma;
        var (rr,gg,bb)=hue switch{<60=>(chroma,x,0d),<120=>(x,chroma,0d),<180=>(0d,chroma,x),<240=>(0d,x,chroma),<300=>(x,0d,chroma),_=>(chroma,0d,x)};
        return new((byte)Math.Round((rr+m)*255),(byte)Math.Round((gg+m)*255),(byte)Math.Round((bb+m)*255));
    }
}
