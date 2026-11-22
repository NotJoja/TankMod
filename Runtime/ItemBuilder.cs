using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TankMod.Runtime;

/// <summary>
/// Builds a networked melee shop item at runtime and registers it with REPOLib.
/// A vanilla melee item provides the PhotonView, ItemMelee, ItemBattery, hurt collider and so on.
/// We replace its visuals and colliders and point it at a new <see cref="Item"/> entry.
/// </summary>
internal static class ItemBuilder
{
    private static readonly string[] MeshNodes = { "Mesh Healthy", "Mesh Broken" };

    public static ValuableBuilder.Result Register(ItemSpec spec, int attempt)
    {
        try
        {
            var template = Resources.Load<GameObject>(spec.Template);
            if (template == null)
            {
                if (attempt == 0 || attempt % 120 == 0)
                {
                    TankModPlugin.Logger.LogWarning($"Item template '{spec.Template}' not found yet (attempt {attempt}).");
                }

                return ValuableBuilder.Result.TemplateMissing;
            }

            var prefab = spec is VehicleSpec vehicle ? VehicleBuilder.Build(vehicle, template) : Build(spec, template);
            REPOLib.Modules.Items.RegisterItem(prefab.GetComponent<ItemAttributes>());
            TankModPlugin.Logger.LogInfo($"Registered item '{spec.ItemName}' (template '{template.name}', attempt {attempt}).");
            return ValuableBuilder.Result.Registered;
        }
        catch (System.Exception e)
        {
            TankModPlugin.Logger.LogError($"Failed to register item '{spec.Name}': {e}");
            return ValuableBuilder.Result.Failed;
        }
    }

    private static GameObject Build(ItemSpec spec, GameObject template)
    {
        var root = Object.Instantiate(template, ValuableBuilder.Holder);
        root.name = spec.Name;

        var baseMaterial = template.GetComponentInChildren<Renderer>(true)?.sharedMaterial;
        var physicsMaterial = ValuableBuilder.GetPhysicsMaterial(template.GetComponentInChildren<Collider>(true));

        // Physics colliders: drop the template's and add the old prefab's. The hurt collider is a trigger with its own
        // script and is left alone.
        foreach (var collider in root.GetComponentsInChildren<PhysGrabObjectCollider>(true))
        {
            Object.DestroyImmediate(collider.gameObject);
        }

        ValuableBuilder.BuildColliders(spec.Colliders, root.transform, physicsMaterial);

        ReplaceMeshes(root, spec, baseMaterial);

        if (spec.ForceGrabPoint is { } grab)
        {
            var node = FindDeep(root.transform, "Force Grab Point");
            if (node != null) node.localPosition = grab;
            else TankModPlugin.Logger.LogWarning("Template has no 'Force Grab Point'.");
        }

        ApplyTuning(spec, root);
        ApplyItem(spec, root);
        try
        {
            root.GetComponent<ItemAttributes>().icon = LoadIcon(spec.Folder);
        }
        catch (System.Exception e)
        {
            TankModPlugin.Logger.LogWarning($"Could not load an icon for '{spec.Name}': {e.Message}");
        }

        return root;
    }

    private static (Mesh, Material) ReplaceMeshes(GameObject root, ItemSpec spec, Material? baseMaterial)
    {
        var meshes = ValuableBuilder.LoadMeshes($"{spec.Folder}/{spec.ObjFile}", spec.Part.Part == "*");
        if (!meshes.TryGetValue(spec.Part.Part, out var mesh))
        {
            throw new System.Collections.Generic.KeyNotFoundException($"OBJ '{spec.ObjFile}' has no part '{spec.Part.Part}'. Parts: {string.Join(", ", meshes.Keys)}");
        }

        var material = ValuableBuilder.CreateMaterial(baseMaterial, spec.Part, spec.Folder, false);
        TankModPlugin.Logger.LogInfo($"{spec.Name}: {mesh.vertexCount} vertices, size={mesh.bounds.size}");

        // ItemMelee looks these two nodes up by name and swaps them when the item breaks.
        foreach (var nodeName in MeshNodes)
        {
            var node = FindDeep(root.transform, nodeName);
            if (node == null) throw new System.InvalidOperationException($"Template has no '{nodeName}' node.");

            foreach (var child in node.Cast<Transform>().ToList()) Object.DestroyImmediate(child.gameObject);
            foreach (var old in node.GetComponents<Component>().Where(c => c is MeshFilter or MeshRenderer).ToList()) Object.DestroyImmediate(old);

            node.localPosition = spec.MeshPosition;
            node.localRotation = Quaternion.Euler(spec.MeshEuler);
            node.localScale = spec.MeshScale;

            node.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            node.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        return (mesh, material);
    }

    private static void ApplyTuning(ItemSpec spec, GameObject root)
    {
        if (spec.RigidbodyMass >= 0f && root.GetComponent<Rigidbody>() is { } body) body.mass = spec.RigidbodyMass;

        var melee = root.GetComponent<ItemMelee>();
        if (melee != null)
        {
            if (spec.DurabilityDrain >= 0f) melee.durabilityDrainOnEnemiesAndPVP = spec.DurabilityDrain;
            if (spec.TorqueStrength >= 0f) melee.torqueStrength = spec.TorqueStrength;
        }

        var notValuable = root.GetComponent<NotValuableObject>();
        if (notValuable != null && spec.ParticleColors != null) notValuable.particleColors = spec.ParticleColors;
    }

    internal static void ApplyItem(ItemSpec spec, GameObject root)
    {
        var attributes = root.GetComponent<ItemAttributes>();
        if (attributes == null || attributes.item == null) throw new System.InvalidOperationException("Template has no ItemAttributes/Item.");

        // A copy of the template's Item (it keeps the colour preset), re-labelled as ours.
        var item = Object.Instantiate(attributes.item);
        item.name = spec.Name;
        item.itemName = spec.ItemName;
        item.itemNameLocalized = null!;
        item.description = spec.Description;
        if (!spec.KeepTemplateShopSettings)
        {
            item.itemType = (SemiFunc.itemType)spec.ItemType;
            item.emojiIcon = (SemiFunc.emojiIcon)spec.EmojiIcon;
            item.itemVolume = (SemiFunc.itemVolume)spec.ItemVolume;
            item.itemSecretShopType = (SemiFunc.itemSecretShopType)spec.SecretShopType;
            item.maxAmount = spec.MaxAmount;
            item.maxAmountInShop = spec.MaxAmountInShop;
            item.maxPurchase = spec.MaxPurchase;
            item.maxPurchaseAmount = spec.MaxPurchaseAmount;
            item.physicalItem = spec.PhysicalItem;
            item.value = ValuableBuilder.Preset("value", spec.ValuePreset, item.value);
        }

        item.disabled = false;

        if (spec.ValueMin >= 0f)
        {
            var value = ScriptableObject.CreateInstance<Value>();
            value.name = spec.Name + " Value";
            value.valueMin = spec.ValueMin;
            value.valueMax = spec.ValueMax >= spec.ValueMin ? spec.ValueMax : spec.ValueMin;
            value.hideFlags = HideFlags.DontUnloadUnusedAsset;
            item.value = value;
        }
        item.hideFlags = HideFlags.DontUnloadUnusedAsset;

        attributes.item = item;
    }

    /// <summary>Loads the pre-rendered icon (Assets/&lt;folder&gt;/icon.png) as a sprite for the shop and inventory.</summary>
    internal static Sprite LoadIcon(string folder)
    {
        var texture = EmbeddedAssets.LoadTexture($"{folder}/icon.png");
        texture.wrapMode = TextureWrapMode.Clamp;
        var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
        sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return sprite;
    }

    internal static Transform? FindDeep(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            var found = FindDeep(child, name);
            if (found != null) return found;
        }

        return null;
    }
}
