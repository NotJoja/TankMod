using UnityEngine;

namespace TankMod.Runtime;

/// <summary>Everything needed to rebuild one of the old Unity melee item prefabs in code.</summary>
internal class ItemSpec
{
    public string Name = "";
    public string ItemName = "";
    public string Description = "";

    /// <summary>Vanilla item prefab (Resources path) cloned for its melee, battery and networking logic.</summary>
    public string Template = "Items/Item Melee Sledge Hammer";

    public string Folder = "";
    public string ObjFile = "";

    /// <summary>Visual used for both the healthy and the broken state.</summary>
    public MeshPartSpec Part = new();

    public Vector3 MeshPosition;
    public Vector3 MeshEuler;
    public Vector3 MeshScale = Vector3.one;

    /// <summary>Replaces the template's physics colliders.</summary>
    public ColliderSpec[] Colliders = System.Array.Empty<ColliderSpec>();

    public Vector3? ForceGrabPoint;

    // Values below 0 keep the template's value.
    public float RigidbodyMass = -1f;
    public float DurabilityDrain = -1f;
    public float TorqueStrength = -1f;
    public Gradient? ParticleColors;

    // Shop entry (SemiFunc enums, kept as numbers like in the old Item asset).
    public string ValuePreset = "Value Expensive";
    public int ItemType = 7; // melee
    public int EmojiIcon = 16;
    public int ItemVolume = 2; // large
    public int SecretShopType = 1;
    public int MaxAmount = 1;
    public int MaxAmountInShop = 1;
    public bool MaxPurchase = false;
    public int MaxPurchaseAmount = 1;
    public bool PhysicalItem = true;

    /// <summary>
    /// Own price range (the Value asset's min/max). The shop shows roughly Max / 1000 * 4, so 50000 is about 200.
    /// Below 0 uses the preset / the template's.
    /// </summary>
    public float ValueMin = -1f;

    public float ValueMax = -1f;

    /// <summary>Keep the cloned template's type, icon emoji, volume and amounts (used for vehicles).</summary>
    public bool KeepTemplateShopSettings;
}
