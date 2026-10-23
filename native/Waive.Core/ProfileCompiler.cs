using System.Buffers.Binary;
using System.Text.Json;

namespace Waive.Core;

public sealed record WriteStep(Command Command,string Description);
public sealed record CompiledProfile(byte[] Keys,byte[] FnKeys,byte[] Colors,byte[] KeyEffects,byte[] MacroBytes,byte[] EffectBytes,bool NeedsReports);

public static class ProfileCompiler
{
    public static CompiledProfile Compile(KeyboardProfile profile,IReadOnlyDictionary<int,uint> defaults,int slots=112,bool onboard=false)
    {
        profile.Validate(slots);
        if(!onboard && profile.FnKeys.Count>0)throw new FormatException("Fn remapping is available in onboard layers 2–4.");
        var macroIds=profile.Keys.Values.Concat(profile.FnKeys.Values).Where(b=>b.StartsWith("macro:")).Select(b=>Guid.Parse(b[6..])).Distinct().ToList();
        if(onboard&&macroIds.Count>16)throw new FormatException("This version supports at most 16 onboard macros per profile.");
        var keys=Enumerable.Repeat(onboard?(byte)0xff:(byte)0,slots*4).ToArray();
        var fn=Enumerable.Repeat((byte)0xff,slots*4).ToArray();
        foreach(var (index,value) in defaults){KeyboardProfile.CheckIndex(index,slots);BinaryPrimitives.WriteUInt32LittleEndian(keys.AsSpan(index*4),value);}
        void Bind(Dictionary<int,string> bindings,byte[] target)
        {
            foreach(var (index,binding) in bindings)
            {
                uint value;
                if(binding.StartsWith("open:"))value=0x0a010000u|(uint)(index+1);
                else if(binding.StartsWith("macro:"))value=0x0a010000u|(uint)(onboard?macroIds.IndexOf(Guid.Parse(binding[6..])):index+1);
                else value=KeyCatalog.Encode(binding);
                BinaryPrimitives.WriteUInt32LittleEndian(target.AsSpan(index*4),value);
            }
        }
        Bind(profile.Keys,keys);Bind(profile.FnKeys,fn);
        var colors=Frame(profile,null,0,176);
        var effectIds=profile.KeyEffects.Values.Distinct().ToList();
        var keyEffects=Enumerable.Repeat((byte)0xff,128).ToArray();
        foreach(var (index,id) in profile.KeyEffects)keyEffects[index]=(byte)(onboard?effectIds.IndexOf(id)+1:index+1);
        var effectBytes=onboard?CompileEffects(profile,effectIds):[];
        var macros=onboard?macroIds.SelectMany((id,index)=>profile.Macros.Single(m=>m.Id==id).Compile(index)).ToArray():[];
        return new(keys,fn,colors,keyEffects,macros,effectBytes,profile.Keys.Values.Any(x=>x.StartsWith("macro:")||x.StartsWith("open:")) || profile.KeyEffects.Count>0);
    }
    public static byte[] Frame(KeyboardProfile profile,LightingEffect? effect,long tick,int slots=132)
    {
        var data=Protocol.Colors(Rgb.Parse(profile.Color),slots,profile.Brightness);
        foreach(var (key,hex) in profile.KeyColors)if(KeyboardLightingMap.LedForKey(key) is int led&&led<slots)Set(data,led,Rgb.Parse(hex),profile.Brightness);
        if(effect is not null)
        {
            data.AsSpan().Clear();
            foreach(var (key,hex) in effect.FrameAt(tick))if(KeyboardLightingMap.EffectLed(effect,key) is int led&&led<slots)Set(data,led,Rgb.Parse(hex),profile.Brightness);
        }
        return data;
    }
    static void Set(byte[] data,int index,Rgb c,int brightness){data[index*4]=c.R;data[index*4+1]=c.G;data[index*4+2]=c.B;data[index*4+3]=(byte)brightness;}
    static byte[] CompileEffects(KeyboardProfile profile,List<Guid> keyEffects)
    {
        var effects=new List<LightingEffect?>();
        effects.Add(profile.EffectId is Guid id?profile.Effects.Single(e=>e.Id==id):null);
        effects.AddRange(keyEffects.Select(id=>profile.Effects.Single(e=>e.Id==id)));
        if(effects.Count>32)throw new FormatException("At most 32 stored lighting effects fit in a profile.");
        using var payload=new MemoryStream();payload.Write(Enumerable.Repeat((byte)0xff,512).ToArray());
        for(int index=0;index<effects.Count;index++)
        {
            var effect=effects[index];int frameAddress=(int)payload.Position,frames=0,logicAddress=0,logicCount=0;
            if(effect is null)
            {
                var frame=new byte[708];BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2),704);Frame(profile,null,0,176).CopyTo(frame,4);payload.Write(frame);frames=1;
            }
            else
            {
                effect.Validate();
                if(effect.Rules.Count==0)
                {
                    // Encode exact per-key RGB frames, including multi-color effects.
                    foreach(var f in effect.Frames)for(int repeat=0;repeat<f.Count;repeat++)
                    {
                        var bytes=new byte[708];BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2),704);
                        foreach(var (key,hex) in f.Data){if(KeyboardLightingMap.EffectLed(effect,key) is not int led)continue;var c=Rgb.Parse(hex);bytes[4+led*4]=c.R;bytes[5+led*4]=c.G;bytes[6+led*4]=c.B;bytes[7+led*4]=(byte)profile.Brightness;}
                        payload.Write(bytes);frames++;if(payload.Length>128*1024)throw new FormatException("Lighting exceeds the keyboard's 128 KiB profile limit.");
                    }
                }
                else
                {
                    foreach(var f in effect.Frames)for(int repeat=0;repeat<f.Count;repeat++)
                    {
                        var bytes=new byte[26];bytes[0]=3;bytes[2]=22;
                        foreach(var led in KeyboardLightingMap.EffectLeds(effect,f.Data.Keys))bytes[4+led/8]|=(byte)(1<<(led%8));
                        payload.Write(bytes);frames++;
                        if(payload.Length>128*1024)throw new FormatException("Lighting exceeds the keyboard's 128 KiB profile limit.");
                    }
                    logicAddress=(int)payload.Position;logicCount=effect.Rules.Count;
                    foreach(var rule in effect.Rules)
                    {
                        var bytes=new byte[32];bytes[0]=(byte)rule.Type;bytes[1]=32;
                        foreach(var led in KeyboardLightingMap.EffectLeds(effect,rule.Keys))bytes[2+led/8]|=(byte)(1<<(led%8));
                        var c=ColorMath.Scale(Rgb.Parse(rule.Color),profile.Brightness/100d);bytes[24]=c.R;bytes[25]=c.G;bytes[26]=c.B;
                        if(rule.Type==1)BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28),(ushort)(360/rule.Count));
                        if(rule.Type==2){BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(28),(ushort)(100/rule.Count));BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(30),(ushort)rule.StayCount);}
                        payload.Write(bytes);
                    }
                }
            }
            if(payload.Length>128*1024)throw new FormatException("Lighting exceeds the keyboard's 128 KiB profile limit.");
            var end=payload.Position;payload.Position=index*16;
            var header=new byte[16];BinaryPrimitives.WriteUInt32LittleEndian(header,(uint)frameAddress);BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4),(uint)frames);BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8),(uint)logicAddress);BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12),(uint)logicCount);
            payload.Write(header);payload.Position=end;
        }
        var result=payload.ToArray();
        // The built-in Fn lighting selector cycles five slots. Populate each with
        // the selected look so an older selected slot cannot reference erased data.
        if(profile.Mode==1&&effects.Count==1)for(int i=1;i<5;i++)result.AsSpan(0,16).CopyTo(result.AsSpan(i*16,16));
        return result;
    }
    public static IReadOnlyList<WriteStep> StoredPlan(KeyboardProfile profile,IReadOnlyDictionary<int,uint> defaults,int slots=112)
    {
        if(profile.Mode is <1 or >4)throw new FormatException("Select an onboard layer before storing a profile.");
        var compiled=Compile(profile,defaults,slots,true);
        var steps=new List<WriteStep>();
        void Region(byte type,byte code,byte[] data,string name,int shift=16)
        {
            steps.Add(new(new Command(0x21,(byte)profile.Mode,type),"Clear "+name));
            foreach(var (offset,bytes) in Protocol.Chunks(data))steps.Add(new(new Command(code,(byte)profile.Mode,(uint)offset | (uint)bytes.Length<<shift,bytes),"Write "+name));
        }
        bool bindings=profile.Keys.Count>0||profile.FnKeys.Count>0;
        if(profile.Mode!=1&&bindings){Region(1,0x22,compiled.Keys,"base key assignments");Region(7,0x31,compiled.FnKeys,"Fn key assignments");}
        // This firmware's lighting is carried in region 6, including static RGB frames.
        // Region 3 is a legacy buffer and is not required by the effect renderer.
        if(profile.Mode!=1&&bindings)Region(4,0x25,compiled.MacroBytes,"macros");
        if(profile.Mode!=1)Region(5,0x26,compiled.KeyEffects.AsSpan(0,slots).ToArray(),"key lighting indices");
        Region(6,0x27,compiled.EffectBytes,"lighting effects",24);
        return steps;
    }
}
