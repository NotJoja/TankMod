using System.Collections.Generic;
using System.Linq;
using REPOLib.Modules;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TankMod.Runtime;

/// <summary>
/// Builds a networked valuable prefab at runtime and registers it with REPOLib.
/// The vanilla template provides the PhotonView, PhysGrabObject, Rigidbody, ValuableObject etc.
/// We only replace its visuals and colliders and set the presets.
/// </summary>
internal static class ValuableBuilder
{
    private static readonly System.Text.RegularExpressions.Regex DataMapPattern = new(
        "normal|bump|metal|occlusion|emission|emissive|detail|mask|height|parallax|spec|rough|gloss|\\bao\\b|_ao",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static GameObject? _holder;

    // Inactive, persistent parent so prefabs never run Awake/Start and survive scene loads.
    internal static Transform Holder
    {
        get
        {
            if (_holder == null)
            {
                _holder = new GameObject("TankMod Prefabs");
                _holder.SetActive(false);
                Object.DontDestroyOnLoad(_holder);
            }

            return _holder.transform;
        }
    }

    internal enum Result
    {
        Registered,
        Failed,
        TemplateMissing,
    }

    public static Result Register(ValuableSpec spec, int attempt)
    {
        try
        {
            var template = FindTemplate(spec, attempt);
            if (template == null) return Result.TemplateMissing;

            var prefab = Build(spec, template);
            Valuables.RegisterValuable(prefab, spec.LevelPresets.ToList());
            TankModPlugin.Logger.LogInfo($"Registered valuable '{spec.Name}' (template '{template.name}', attempt {attempt}).");
            return Result.Registered;
        }
        catch (System.Exception e)
        {
            TankModPlugin.Logger.LogError($"Failed to register valuable '{spec.Name}': {e}");
            return Result.Failed;
        }
    }

    // The game's resources may not be usable the moment plugins load, so this is retried by the caller.
    private static GameObject? FindTemplate(ValuableSpec spec, int attempt)
    {
        var exact = Resources.Load<GameObject>(spec.Template);
        if (exact != null) return exact;

        foreach (var folder in new[] { "Valuables/01 Tiny", "Valuables/02 Small", "Valuables" })
        {
            var candidate = Resources.LoadAll<GameObject>(folder).FirstOrDefault(IsPlainValuable);
            if (candidate != null) return candidate;
        }

        // Any valuable prefab asset the game already has in memory (not scene instances).
        var loaded = Resources.FindObjectsOfTypeAll<ValuableObject>().Where(v => !v.gameObject.scene.IsValid()).Select(v => v.gameObject).FirstOrDefault();
        if (loaded != null) return loaded;

        if (attempt == 0 || attempt % 120 == 0)
        {
            TankModPlugin.Logger.LogWarning(
                $"No valuable template yet (attempt {attempt}): '{spec.Template}' not found; " +
                $"LoadAll('Valuables')={Resources.LoadAll<GameObject>("Valuables").Length}, " +
                $"loaded ValuableObjects={Resources.FindObjectsOfTypeAll<ValuableObject>().Length}.");
        }

        return null;
    }

    private static bool IsPlainValuable(GameObject go) =>
        go.GetComponent<ValuableObject>() != null && go.GetComponent<Rigidbody>() != null;

    private static GameObject Build(ValuableSpec spec, GameObject template)
    {

        var root = Object.Instantiate(template, Holder);
        root.name = spec.Name;

        // Borrow shared resources from the template before throwing its visuals away.
        var baseMaterial = template.GetComponentInChildren<Renderer>(true)?.sharedMaterial;
        var physicsMaterial = GetPhysicsMaterial(template.GetComponentInChildren<Collider>(true));

        foreach (var child in root.transform.Cast<Transform>().ToList())
        {
            Object.DestroyImmediate(child.gameObject);
        }

        var objectNode = new GameObject("Object").transform;
        objectNode.SetParent(root.transform, false);
        objectNode.localPosition = spec.ObjectPosition;
        objectNode.localRotation = Quaternion.Euler(spec.ObjectEuler);

        BuildVisuals(spec, objectNode, baseMaterial);
        BuildColliders(spec, objectNode, physicsMaterial);
        ApplyValuableSettings(spec, root);

        return root;
    }

    private static void BuildVisuals(ValuableSpec spec, Transform objectNode, Material? baseMaterial)
    {
        var merged = spec.Parts.Any(p => p.Part == "*");
        var meshes = LoadMeshes($"{spec.Folder}/{spec.ObjFile}", merged);

        var visual = new GameObject(spec.Name.ToLowerInvariant()).transform;
        visual.SetParent(objectNode, false);
        visual.localPosition = spec.VisualPosition;
        visual.localRotation = Quaternion.Euler(spec.VisualRotationEuler);
        visual.localScale = spec.VisualScale;

        if (spec.SpinWhenHeldSpeed != 0f) visual.gameObject.AddComponent<SpinWhenHeld>().DegreesPerSecond = spec.SpinWhenHeldSpeed;

        foreach (var part in spec.Parts)
        {
            if (!meshes.TryGetValue(part.Part, out var mesh))
            {
                throw new KeyNotFoundException($"OBJ '{spec.ObjFile}' has no part '{part.Part}'. Parts: {string.Join(", ", meshes.Keys)}");
            }

            TankModPlugin.Logger.LogInfo($"{spec.Name}/{part.Part}: {mesh.vertexCount} vertices, {mesh.triangles.Length / 3} triangles, uv={(mesh.uv.Length > 0)}, size={mesh.bounds.size}");

            var go = new GameObject(part.Part);
            go.transform.SetParent(visual, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = CreateMaterial(baseMaterial, part, spec.Folder, spec.Shiny);
        }
    }

    // Models and textures can be shared between valuables (all three dildos use one model, for example).
    private static readonly Dictionary<string, Dictionary<string, Mesh>> MeshCache = new();
    private static readonly Dictionary<string, Texture2D> TextureCache = new();

    internal static Dictionary<string, Mesh> LoadMeshes(string path, bool merged)
    {
        var key = $"{path}|{merged}";
        if (!MeshCache.TryGetValue(key, out var meshes))
        {
            MeshCache[key] = meshes = ObjLoader.Load(EmbeddedAssets.ReadText(path), merged);
        }

        return meshes;
    }

    private static Texture2D LoadTexture(string path)
    {
        if (!TextureCache.TryGetValue(path, out var texture))
        {
            TextureCache[path] = texture = EmbeddedAssets.LoadTexture(path);
        }

        return texture;
    }

    private static bool _shaderLogged;
    private static bool _shineLogged;

    private static Shader? _standardShader;
    private static bool _standardChecked;

    // Unity's Standard shader: lit by the game's lights and matte, like the materials of the old Unity project.
    private static Shader? StandardShader
    {
        get
        {
            if (!_standardChecked)
            {
                _standardChecked = true;
                _standardShader = Shader.Find("Standard");
                TankModPlugin.Logger.LogInfo(_standardShader != null
                    ? "Using the Standard shader for non-shiny valuables."
                    : "Standard shader not available, non-shiny valuables fall back to the template's ShinyMetal.");
            }

            return _standardShader;
        }
    }

    private static Material CreateStandardMaterial(Shader shader, MeshPartSpec part, string folder)
    {
        var material = new Material(shader) { name = part.Part };

        if (string.IsNullOrEmpty(part.Texture))
        {
            material.color = part.Color;
        }
        else
        {
            material.color = Color.white;
            material.mainTexture = LoadTexture($"{folder}/{part.Texture}");
        }

        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.1f);
        if (material.HasProperty("_GlossMapScale")) material.SetFloat("_GlossMapScale", 0.1f);

        TankModPlugin.Logger.LogInfo($"Material '{part.Part}': Standard, {(string.IsNullOrEmpty(part.Texture) ? "colour " + part.Color : "texture " + part.Texture)}");
        return material;
    }

    internal static Material CreateMaterial(Material? baseMaterial, MeshPartSpec part, string folder, bool shiny)
    {
        if (!shiny && StandardShader is { } standard)
        {
            return CreateStandardMaterial(standard, part, folder);
        }

        var material = baseMaterial != null ? new Material(baseMaterial) : new Material(Shader.Find("Standard"));
        material.name = part.Part;

        var shader = material.shader;
        if (!_shaderLogged)
        {
            _shaderLogged = true;
            var props = Enumerable.Range(0, shader.GetPropertyCount())
                .Select(i => $"{shader.GetPropertyName(i)}:{shader.GetPropertyType(i)}");
            TankModPlugin.Logger.LogInfo($"Template shader '{shader.name}' properties: {string.Join(", ", props)}");
        }

        if (_shaderLogged && !_shineLogged && material.HasProperty("_ShineColor"))
        {
            _shineLogged = true;
            TankModPlugin.Logger.LogInfo($"Template shine: _ShineColor={material.GetColor("_ShineColor")}, _Shine_Remap={material.GetVector("_Shine_Remap")}, _T_Remap={material.GetVector("_T_Remap")}");
        }

        // Find the 2D colour slot and clear the 2D data maps (normal, ...). Cubemaps such as _Reflection are left
        // alone: they come from the template and give the surface its shine.
        string? albedoSlot = null;
        var candidates = new List<string>();
        for (var i = 0; i < shader.GetPropertyCount(); i++)
        {
            if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
            if (shader.GetPropertyTextureDimension(i) != UnityEngine.Rendering.TextureDimension.Tex2D) continue;

            var slot = shader.GetPropertyName(i);
            if (slot == "_texcoord") continue; // Amplify's UV placeholder, not a real map
            if (DataMapPattern.IsMatch(slot))
            {
                // An empty normal slot can read as a tilted normal; use a flat one so lighting stays correct.
                material.SetTexture(slot, slot.IndexOf("normal", System.StringComparison.OrdinalIgnoreCase) >= 0 ? FlatNormalTexture : null);
                continue;
            }
            candidates.Add(slot);
        }

        foreach (var preferred in new[] { "_Texture", "_MainTex", "_BaseMap", "_Albedo" })
        {
            if (candidates.Contains(preferred)) { albedoSlot = preferred; break; }
        }

        albedoSlot ??= candidates.FirstOrDefault();

        foreach (var keyword in new[] { "_NORMALMAP", "_METALLICGLOSSMAP", "_SPECGLOSSMAP", "_PARALLAXMAP", "_EMISSION", "_DETAIL_MULX2" })
        {
            material.DisableKeyword(keyword);
        }

        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.3f);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.3f);
        if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", Color.black);

        // Flat colours become a tiny solid texture, since this shader has no colour property to tint with.
        var texture = string.IsNullOrEmpty(part.Texture) ? SolidTexture(part.Color) : LoadTexture($"{folder}/{part.Texture}");

        if (albedoSlot != null) material.SetTexture(albedoSlot, texture);

        // The shine comes from the reflection cubemap and the shine colour. Switch both off unless wanted.
        if (!shiny)
        {
            if (material.HasProperty("_ShineColor")) material.SetColor("_ShineColor", Color.black);
            if (material.HasProperty("_Reflection")) material.SetTexture("_Reflection", null);
        }
        if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);

        TankModPlugin.Logger.LogInfo($"Material '{part.Part}': albedo '{(string.IsNullOrEmpty(part.Texture) ? "solid " + part.Color : part.Texture)}' -> slot '{albedoSlot}' ({texture.width}x{texture.height})");
        return material;
    }

    private static Texture2D? _flatNormal;

    // (0.5, 0.5, 1) is "no bump" in a tangent-space normal map.
    private static Texture2D FlatNormalTexture
    {
        get
        {
            if (_flatNormal == null)
            {
                _flatNormal = new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { name = "flat normal" };
                _flatNormal.SetPixels(Enumerable.Repeat(new Color(0.5f, 0.5f, 1f, 1f), 16).ToArray());
                _flatNormal.Apply(false, true);
            }

            return _flatNormal;
        }
    }

    private static readonly Dictionary<Color, Texture2D> SolidCache = new();

    private static Texture2D SolidTexture(Color color)
    {
        if (SolidCache.TryGetValue(color, out var cached)) return cached;

        var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = $"solid {color}", filterMode = FilterMode.Point };
        texture.SetPixels(Enumerable.Repeat(color, 16).ToArray());
        texture.Apply(false, true);
        return SolidCache[color] = texture;
    }

    // The collider material type was renamed between Unity versions (PhysicMaterial / PhysicsMaterial), so it is
    // copied via reflection instead of referencing the type, which would break at runtime on the other version.
    private static readonly System.Reflection.PropertyInfo? SharedMaterialProperty = typeof(Collider).GetProperty("sharedMaterial");

    internal static object? GetPhysicsMaterial(Collider? collider)
    {
        return collider == null ? null : SharedMaterialProperty?.GetValue(collider);
    }

    private static void SetPhysicsMaterial(Collider collider, object? material)
    {
        if (material != null) SharedMaterialProperty?.SetValue(collider, material);
    }

    private static void BuildColliders(ValuableSpec spec, Transform objectNode, object? physicsMaterial)
    {
        BuildColliders(spec.Colliders, objectNode, physicsMaterial);
    }

    internal static void BuildColliders(ColliderSpec[] colliders, Transform objectNode, object? physicsMaterial)
    {
        var layer = LayerMask.NameToLayer("PhysGrabObject");

        for (var i = 0; i < colliders.Length; i++)
        {
            var c = colliders[i];
            var go = new GameObject($"Valuable Collider ({i})") { layer = layer == -1 ? 0 : layer, tag = "Phys Grab Object" };
            go.transform.SetParent(objectNode, false);
            go.transform.localPosition = c.Position;
            go.transform.localRotation = Quaternion.Euler(c.Euler);
            go.transform.localScale = c.Scale;

            Collider collider;
            if (c.Kind == ColliderKind.Box)
            {
                var box = go.AddComponent<BoxCollider>();
                box.size = c.Size;
                box.center = c.Center;
                collider = box;
                go.AddComponent<PhysGrabObjectBoxCollider>();
            }
            else
            {
                var capsule = go.AddComponent<CapsuleCollider>();
                capsule.radius = c.Radius;
                capsule.height = c.Height;
                capsule.direction = c.Direction;
                capsule.center = c.Center;
                collider = capsule;
                go.AddComponent<PhysGrabObjectCapsuleCollider>();
            }

            SetPhysicsMaterial(collider, physicsMaterial);
            go.AddComponent<PhysGrabObjectCollider>().colliderID = c.ColliderId;
        }
    }

    
    private static void ApplyValuableSettings(ValuableSpec spec, GameObject root)
    {
        var valuable = root.GetComponent<ValuableObject>();
        if (valuable == null)
            throw new System.InvalidOperationException("Template has no ValuableObject.");

        valuable.durabilityPreset = Preset("durability", spec.DurabilityPreset, valuable.durabilityPreset);
        valuable.physAttributePreset = Preset("phys", spec.PhysAttributePreset, valuable.physAttributePreset);
        valuable.audioPreset = Preset("audio", spec.PhysAudioPreset, valuable.audioPreset);

        // Replace the impact sounds if a custom WAV file is configured.
        if (!string.IsNullOrEmpty(spec.ImpactAudioPath))
        {
            var originalAudio = valuable.audioPreset;

            if (originalAudio == null)
                throw new System.InvalidOperationException("No original PhysAudio preset available.");

            var clip = EmbeddedAssets.LoadWavPcm16(spec.ImpactAudioPath);

            valuable.audioPreset = CreateImpactAudioPreset(
                originalAudio,
                spec.Name,
                clip
            );

            TankModPlugin.Logger.LogInfo(
                $"Loaded custom impact audio for '{spec.Name}': {spec.ImpactAudioPath}"
            );
        }

        if (spec.ValueMin >= 0f)
        {
            // A custom value range instead of one of the game's value presets.
            var value = ScriptableObject.CreateInstance<Value>();
            value.name = $"{spec.Name} value";
            value.valueMin = spec.ValueMin;
            value.valueMax = spec.ValueMax >= spec.ValueMin ? spec.ValueMax : spec.ValueMin;
            value.hideFlags = HideFlags.DontUnloadUnusedAsset;
            valuable.valuePreset = value;
        }
        else
        {
            valuable.valuePreset = Preset("value", spec.ValuePreset, valuable.valuePreset);
        }

        valuable.audioPresetPitch = spec.AudioPitch;
        valuable.volumeType = spec.Volume;

        if (spec.ParticleColors != null)
            valuable.particleColors = spec.ParticleColors;
    }

    
    private static PhysAudio CreateImpactAudioPreset(
        PhysAudio original,
        string name,
        AudioClip clip)
    {
        var custom = ScriptableObject.CreateInstance<PhysAudio>();

        custom.name = $"{name} custom audio";
        custom.hideFlags = HideFlags.DontUnloadUnusedAsset;

        // Custom sound for every impact strength.
        custom.impactLight = CloneCustomSound(original.impactLight, clip);
        custom.impactMedium = CloneCustomSound(original.impactMedium, clip);
        custom.impactHeavy = CloneCustomSound(original.impactHeavy, clip);

        // Custom sound when breaking.
        custom.breakLight = CloneCustomSound(original.breakLight, clip);
        custom.breakMedium = CloneCustomSound(original.breakMedium, clip);
        custom.breakHeavy = CloneCustomSound(original.breakHeavy, clip);

        // Custom sound when destroyed.
        custom.destroy = CloneCustomSound(original.destroy, clip);

        return custom;
    }

    private static Sound CloneCustomSound(Sound original, AudioClip clip)
    {
        var sound = CloneSound(original);

        sound.Sounds = new[] { clip };
        sound.Type = AudioManager.AudioType.MaterialImpact;

        // Increase volume.
        sound.Volume = 1f;
        sound.VolumeRandom = 0f;

        return sound;
    }

    private static Sound CloneSound(Sound original)
    {
        var sound = new Sound();
        Sound.CopySound(original, sound);
        return sound;
    }



    // Presets are ScriptableObjects the game loads lazily. If one is not loaded yet, take it from a vanilla valuable
    // that uses it. Falling back to the template's preset is the last resort, because it gives the wrong sounds.
    internal static T Preset<T>(string kind, string name, T fallback) where T : Object
    {
        if (!string.IsNullOrEmpty(name))
        {
            var found = PresetLibrary.Find<T>(kind, name);
            if (found != null) return found;
            TankModPlugin.Logger.LogWarning($"{typeof(T).Name} preset '{name}' not found, using the template's.");
        }

        return fallback;
    }
}
