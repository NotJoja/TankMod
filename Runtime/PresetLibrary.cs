using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TankMod.Runtime;

/// <summary>Finds the game's preset ScriptableObjects (durability, value, physics, audio) by name.</summary>
internal static class PresetLibrary
{
    public static T? Find<T>(string kind, string name) where T : Object
    {
        if (string.IsNullOrEmpty(name)) return null;

        var loaded = Resources.FindObjectsOfTypeAll<T>().FirstOrDefault(p => p.name == name);
        if (loaded != null) return loaded;

        // Not loaded yet: borrow it from a vanilla valuable that references it.
        if (!PresetSources.Map.TryGetValue((kind, name), out var path)) return null;

        var source = Resources.Load<GameObject>(path)?.GetComponent<ValuableObject>();
        if (source == null) return null;

        Object? preset = kind switch
        {
            "durability" => source.durabilityPreset,
            "value" => source.valuePreset,
            "phys" => source.physAttributePreset,
            "audio" => source.audioPreset,
            _ => null,
        };

        return preset as T;
    }
}
