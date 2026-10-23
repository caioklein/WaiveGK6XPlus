using System.Text.Json;

namespace Waive.Core;

// Logical indices address the switch/key tables. Location indices address RGB LEDs.
// The 96XS-RGB uses a 14x8 key matrix and a separate 22-column lighting layout.
public static class KeyboardLightingMap
{
    public sealed record Key(string KeyName,int LogicCode,int LocationCode);
    public static readonly IReadOnlyList<Key> Keys=Load();
    static readonly IReadOnlyDictionary<int,int> ToLed=Keys.ToDictionary(k=>k.LogicCode,k=>k.LocationCode);
    static readonly IReadOnlyDictionary<int,int> ToKey=Keys.ToDictionary(k=>k.LocationCode,k=>k.LogicCode);
    static Key[] Load()
    {
        using var stream=typeof(KeyboardLightingMap).Assembly.GetManifestResourceStream("Waive.Core.96xs-keymap.json")??throw new InvalidOperationException("The keyboard lighting map is missing.");
        return JsonSerializer.Deserialize<Key[]>(stream)??throw new InvalidOperationException("The keyboard lighting map is empty.");
    }
    public static int? LedForKey(int key)=>ToLed.TryGetValue(key,out var led)?led:null;
    public static int? KeyForLed(int led)=>ToKey.TryGetValue(led,out var key)?key:null;
    public static int? EffectLed(LightingEffect effect,int index)=>effect.CoordinateSpace=="Led"?index:LedForKey(index);
    public static IEnumerable<int> EffectLeds(LightingEffect effect,IEnumerable<int> indices)=>indices.Select(i=>EffectLed(effect,i)).Where(i=>i.HasValue).Select(i=>i!.Value);
}
