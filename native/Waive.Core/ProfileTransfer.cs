using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Waive.Core;

public static class ProfileTransfer
{
    public static IReadOnlyList<KeyboardProfile> Import(string path)
    {
        var extension=Path.GetExtension(path).ToLowerInvariant();
        if(extension==".waive")return [ProfileStore.Read(path)];
        if(extension==".gpr")
        {
            using var zip=ZipFile.OpenRead(path);
            if(zip.Entries.Count>512 || zip.Entries.Sum(e=>e.Length)>16*1024*1024)throw new FormatException("Profile archive exceeds supported limits.");
            var contents=new List<PackedContent>();
            foreach(var entry in zip.Entries.Where(e=>new[]{".cmf",".cms",".le"}.Contains(Path.GetExtension(e.Name).ToLowerInvariant())))
            { if(entry.Length>8*1024*1024)throw new FormatException("An archive entry is too large.");using var stream=entry.Open();using var memory=new MemoryStream();stream.CopyTo(memory);contents.Add(LegacyCodec.Decode(memory.ToArray())); }
            var macros=contents.Where(c=>c.Type==5).Select(c=>LegacyImport.Macro(c.Body)).ToList();
            var effects=contents.Where(c=>c.Type==2).Select(c=>LegacyImport.Effect(JsonNode.Parse(c.Body)!.AsObject())).ToList();
            var profiles=contents.Where(c=>c.Type==1).Select(c=>LegacyImport.Profile(JsonNode.Parse(c.Body)!.AsObject())).ToArray();
            foreach(var profile in profiles){Resolve(profile,macros,effects);profile.Validate();}
            if(profiles.Length==0)throw new FormatException("The archive contains no profiles.");return profiles;
        }
        JsonObject json;
        if(extension==".cmf"){var content=LegacyCodec.Decode(File.ReadAllBytes(path));if(content.Type!=1)throw new FormatException("Choose a profile CMF file.");json=JsonNode.Parse(content.Body)!.AsObject();}
        else json=JsonNode.Parse(File.ReadAllText(path))?.AsObject()??throw new FormatException("Empty profile.");
        if(json["KeySet"] is null)return [ProfileStore.Read(path)];
        var legacy=LegacyImport.Profile(json);
        var modelFolder=Path.GetDirectoryName(Path.GetFullPath(path))!;
        if(Path.GetFileName(modelFolder)=="656801936")legacy.ModelId=656801936;
        var account=Directory.GetParent(modelFolder)?.Parent?.FullName;
        var libraryMacros=new List<MacroDefinition>();var libraryEffects=new List<LightingEffect>();
        if(account is not null)
        {
            foreach(var macro in legacy.Macros.ToArray())
            {
                var file=Find(Path.Combine(account,"Macro"),macro.Id,".cms");
                if(file is not null)libraryMacros.Add(LegacyImport.Macro(LegacyCodec.Decode(File.ReadAllBytes(file)).Body));
            }
            foreach(var id in legacy.KeyEffects.Values.Concat(legacy.EffectId is Guid effect?new[]{effect}:[]).Distinct())
            {
                var file=Find(Path.Combine(account,"LE"),id,".le");
                if(file is not null)libraryEffects.Add(LegacyImport.Effect(JsonNode.Parse(LegacyCodec.Decode(File.ReadAllBytes(file)).Body)!.AsObject()));
            }
        }
        Resolve(legacy,libraryMacros,libraryEffects);legacy.Validate();return [legacy];
    }
    static string? Find(string folder,Guid id,string extension)=>Directory.Exists(folder)?Directory.EnumerateFiles(folder,"*"+extension).FirstOrDefault(file=>Guid.TryParse(Path.GetFileNameWithoutExtension(file),out var value)&&value==id):null;
    static void Resolve(KeyboardProfile profile,List<MacroDefinition> macros,List<LightingEffect> effects)
    {
        foreach(var macro in macros){profile.Macros.RemoveAll(m=>m.Id==macro.Id);profile.Macros.Add(macro);}
        foreach(var effect in effects){profile.Effects.RemoveAll(e=>e.Id==effect.Id);profile.Effects.Add(effect);}
    }
    public static void Export(string path,KeyboardProfile profile,IReadOnlyDictionary<int,uint> defaults)
    {
        profile.Validate();
        if(Path.GetExtension(path).Equals(".waive",StringComparison.OrdinalIgnoreCase)){File.WriteAllText(path,JsonSerializer.Serialize(profile,ProfileStore.Json));return;}
        var json=LegacyImport.ProfileJson(profile,new Dictionary<int,uint>(defaults)).ToJsonString();
        var cmf=LegacyCodec.Encode(1,"PROFILE",json);
        if(Path.GetExtension(path).Equals(".cmf",StringComparison.OrdinalIgnoreCase)){File.WriteAllBytes(path,cmf);return;}
        using var stream=new FileStream(path,FileMode.Create,FileAccess.Write);using var zip=new ZipArchive(stream,ZipArchiveMode.Create);
        void Entry(string name,byte[] bytes){using var output=zip.CreateEntry(name).Open();output.Write(bytes);}
        Entry(profile.Id.ToString("D").ToUpperInvariant()+".cmf",cmf);
        foreach(var macro in profile.Macros)Entry("Macro/"+macro.Id.ToString("D").ToUpperInvariant()+".cms",LegacyCodec.Encode(5,"MACRO",LegacyImport.MacroText(macro)));
        foreach(var effect in profile.Effects)Entry("LE/"+effect.Id.ToString("D").ToUpperInvariant()+".le",LegacyCodec.Encode(2,"light",LegacyImport.EffectJson(effect).ToJsonString()));
    }
}
