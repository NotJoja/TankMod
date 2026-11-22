using System.Collections;
using System.Linq;
using UnityEngine;

namespace TankMod.Runtime;

/// <summary>
/// Moves the vehicle's grab button to <see cref="Target"/> (the vehicle's own coordinates). The button is spread over
/// several objects (the visible Cart Handle, the Semi Grab Area you aim at, the grab point you are pulled to, the
/// seat's grab-point visual); they all move by the same amount. It runs once the game has set the vehicle up.
/// </summary>
internal sealed class GrabButtonPlacer : MonoBehaviour
{
    public Vector3 Target;

    private IEnumerator Start()
    {
        yield return null; // let the game's own Awake/Start finish first

        var vehicle = GetComponent<ItemVehicle>();
        if (vehicle == null) yield break;

        var handle = Find("Cart Handle");
        var area = Find("Semi Grab Area");
        var seat = vehicle.seats != null && vehicle.seats.Length > 0 ? vehicle.seats[0] : null;

        var origin = handle != null ? handle.position : seat?.grabPoint != null ? seat.grabPoint.position : transform.position;
        var deltaLocal = Target - transform.InverseTransformPoint(origin);
        var delta = transform.TransformVector(deltaLocal);

        var moved = new System.Collections.Generic.List<Transform>();
        foreach (var t in new[] { handle, area, seat?.grabPoint, seat?.grabPointVisual })
        {
            if (t == null || moved.Any(m => t == m || t.IsChildOf(m))) continue;
            var before = transform.InverseTransformPoint(t.position);
            moved.Add(t);
            t.position += delta;
            TankModPlugin.Logger.LogInfo($"GrabButton: '{t.name}' (parent '{t.parent?.name}') {before:F2} -> {transform.InverseTransformPoint(t.position):F2} (vehicle coordinates)");
        }

        TankModPlugin.Logger.LogInfo($"GrabButton: target {Target:F2}, vehicle scale {transform.lossyScale:F2}");
    }

    private Transform? Find(string name)
    {
        return ItemBuilder.FindDeep(transform, name);
    }
}
