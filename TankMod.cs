using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using TankMod.Items;
using TankMod.Runtime;
using UnityEngine;

namespace TankMod;

[BepInPlugin("Joja.TankMod", "TankMod", "1.0.0")]
[BepInDependency("REPOLib", BepInDependency.DependencyFlags.HardDependency)]
public class TankModPlugin : BaseUnityPlugin
{
    internal static TankModPlugin Instance { get; private set; } = null!;
    internal new static ManualLogSource Logger => Instance._logger;
    private ManualLogSource _logger => base.Logger;
    internal Harmony? Harmony { get; set; }

    private void Awake()
    {
        Instance = this;

        // Prevent the plugin from being deleted
        gameObject.transform.parent = null;
        gameObject.hideFlags = HideFlags.HideAndDontSave;

        Harmony ??= new Harmony(Info.Metadata.GUID);
        Harmony.PatchAll();

        StartCoroutine(RegisterWhenReady());

        Logger.LogInfo($"{Info.Metadata.GUID} v{Info.Metadata.Version} has loaded!");
    }

    // Retries every frame for a while: the game's prefabs may not be loadable the instant plugins load.
    private static IEnumerator RegisterWhenReady()
    {
        var pendingItems = new List<ItemSpec> { Tank.Spec };

        for (var attempt = 0; attempt < 1200 && pendingItems.Count > 0; attempt++)
        {
            pendingItems.RemoveAll(spec => ItemBuilder.Register(spec, attempt) != ValuableBuilder.Result.TemplateMissing);
            if (pendingItems.Count > 0) yield return null;
        }

        foreach (var spec in pendingItems)
        {
            Logger.LogError($"Gave up registering item '{spec.Name}': no vanilla item template became available.");
        }
    }

    private void OnDestroy()
    {
        Harmony?.UnpatchSelf();
    }
}
