using HarmonyLib;
using TankMod.Runtime;

namespace TankMod;

// Right click (interact) honks a scooter horn. On the tank it fires the cannon instead, so no honk.
[HarmonyPatch(typeof(ItemVehicle), "PlayHonk")]
internal static class NoTankHonkPatch
{
    private static bool Prefix(ItemVehicle __instance)
    {
        return __instance.GetComponent<TankCannon>() == null;
    }
}
