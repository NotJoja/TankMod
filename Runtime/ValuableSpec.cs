
using System.Linq;
using UnityEngine;

namespace TankMod.Runtime;

internal enum ColliderKind
{
    Box,
    Capsule,
}

/// <summary>A collider on a child object, mirroring one "Valuable Collider" from the old Unity prefab.</summary>
internal sealed class ColliderSpec
{
    public ColliderKind Kind;
    public Vector3 Position;
    public Vector3 Scale = Vector3.one;
    public Vector3 Euler = Vector3.zero;
    public int ColliderId = 0;

    // Box
    public Vector3 Size = Vector3.one;
    public Vector3 Center;

    // Capsule
    public float Radius = 0.5f;
    public float Height = 2f;
    public int Direction = 1; // 0 = X, 1 = Y, 2 = Z
}

/// <summary>One mesh group from the OBJ plus the texture that goes on it.</summary>
internal sealed class MeshPartSpec
{
    /// <summary>OBJ group name, or "*" to merge every group of the file into one mesh.</summary>
    public string Part = "*";

    /// <summary>Albedo texture under the valuable's folder; empty for a flat colour.</summary>
    public string Texture = "";

    public Color Color = Color.white;
}

/// <summary>Everything needed to rebuild one of the old Unity prefabs in code.</summary>
internal sealed class ValuableSpec
{
    public string Name = "";

    /// <summary>Vanilla prefab (Resources path) cloned for its networking/physics components.</summary>
    public string Template = "Valuables/01 Tiny/Valuable Diamond";

    /// <summary>Folder under Assets, e.g. "piwo".</summary>
    public string Folder = "";

    public string ObjFile = "";
    public MeshPartSpec[] Parts = System.Array.Empty<MeshPartSpec>();
    public ColliderSpec[] Colliders = System.Array.Empty<ColliderSpec>();

    // Transform of the "Object" child (the old prefab rotated this) and of the visual node under it.
    public Vector3 ObjectPosition;
    public Vector3 ObjectEuler;
    public Vector3 VisualPosition;
    public Vector3 VisualRotationEuler = Vector3.zero;
    public Vector3 VisualScale = Vector3.one;

    /// <summary>Spin the visual around Y (degrees per second) while it is held. 0 = off.</summary>
    public float SpinWhenHeldSpeed = 0f;

    // ValuableObject settings
    public ValuableVolume.Type Volume = ValuableVolume.Type.Small;
    public string DurabilityPreset = "Durability Default";
    public string ValuePreset = "";

    /// <summary>Custom dollar range. When ValueMin is 0 or more it replaces ValuePreset.</summary>
    public float ValueMin = -1f;
    public float ValueMax = -1f;
    public string PhysAttributePreset = "";
    public string PhysAudioPreset = "";

    /// <summary>Optional custom WAV file for impact sounds, relative to the Assets folder.</summary>
    public string ImpactAudioPath = "";

    public float AudioPitch = 1f;

    /// <summary>Keeps the game's metallic shine (reflection map and shine colour). Off by default.</summary>
    public bool Shiny;
    public Gradient? ParticleColors;

    /// <summary>REPOLib level-valuable presets this valuable is added to.</summary>
    public string[] LevelPresets = { "Valuables - Generic" };
}

internal static class Gradients
{
    /// <summary>Builds a particle colour gradient; each key is (r, g, b, time 0..1).</summary>
    public static Gradient Make(GradientMode mode, params (float r, float g, float b, float t)[] keys)
    {
        return new Gradient
        {
            colorKeys = keys.Select(k => new GradientColorKey(new Color(k.r, k.g, k.b), k.t)).ToArray(),
            alphaKeys = new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) },
            mode = mode,
        };
    }
}
