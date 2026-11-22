using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TankMod.Runtime;

/// <summary>
/// Builds a drivable vehicle item. The game's scooter already has the driving logic, the seat, the networking and the
/// shop entry, so we clone it, hide its model and put our own model in its place. Its colliders stay as they are.
/// </summary>
internal static class VehicleBuilder
{
    public static GameObject Build(VehicleSpec spec, GameObject template)
    {
        var root = Object.Instantiate(template, ValuableBuilder.Holder);
        root.name = spec.Name;

        var vehicle = root.GetComponent<ItemVehicle>();
        if (vehicle == null) throw new System.InvalidOperationException($"Template '{template.name}' has no ItemVehicle.");

        var baseMaterial = template.GetComponentInChildren<Renderer>(true)?.sharedMaterial;

        // Hide the scooter's own model, but keep what the game needs to show: the grab button and the hover effect. The
        // scooter's headlight and tail-light meshes are hidden (they would float in front of the tank). Particles are not mesh renderers and stay anyway.
        var keep = new System.Collections.Generic.HashSet<Renderer>();
        foreach (var r in new Renderer?[] { vehicle.grabMesh, vehicle.hoverEffectRenderer, vehicle.hoverEffectFrontRenderer, vehicle.hoverEffectBackRenderer })
        {
            if (r != null) keep.Add(r);
        }

        foreach (var seat in vehicle.seats ?? System.Array.Empty<ItemVehicle.VehicleSeat>())
        {
            if (seat.grabPointVisual != null)
            {
                foreach (var r in seat.grabPointVisual.GetComponentsInChildren<Renderer>(true)) keep.Add(r);
            }
        }

        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is not (MeshRenderer or SkinnedMeshRenderer)) continue;

            var lowered = renderer.gameObject.name.ToLowerInvariant();
            if (keep.Contains(renderer) || lowered.Contains("grab") || lowered.Contains("handle"))
            {
                TankModPlugin.Logger.LogInfo($"{spec.Name}: keeping scooter renderer '{renderer.gameObject.name}'.");
                continue;
            }

            renderer.enabled = false;
        }

        // The vehicle script moves, tilts and wobbles meshTransform, so the model goes under it.
        var parent = vehicle.meshTransform != null ? vehicle.meshTransform : root.transform;
        var visual = new GameObject(spec.Name + " Model").transform;
        visual.SetParent(parent, false);
        visual.localPosition = spec.VisualPosition;
        visual.localRotation = Quaternion.Euler(spec.VisualEuler);
        visual.localScale = spec.VisualScale;

        var meshes = ValuableBuilder.LoadMeshes($"{spec.Folder}/{spec.ObjFile}", false);
        foreach (var part in spec.Parts)
        {
            if (!meshes.TryGetValue(part.Part, out var mesh))
            {
                throw new System.Collections.Generic.KeyNotFoundException($"OBJ '{spec.ObjFile}' has no part '{part.Part}'. Parts: {string.Join(", ", meshes.Keys)}");
            }

            TankModPlugin.Logger.LogInfo($"{spec.Name}/{part.Part}: {mesh.vertexCount} vertices, size={mesh.bounds.size}");
            var go = new GameObject(part.Part);
            go.transform.SetParent(visual, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = ValuableBuilder.CreateMaterial(baseMaterial, part, spec.Folder, false);
        }

        if (vehicle.seats != null && vehicle.seats.Length > 0)
        {
            var seat = vehicle.seats[0];
            if (seat.seatTransform != null) seat.seatTransform.localPosition += spec.SeatOffset;
            if (seat.firstMountTransform != null) seat.firstMountTransform.localPosition += spec.SeatOffset;
        }
        else
        {
            TankModPlugin.Logger.LogWarning($"{spec.Name}: template vehicle has no seats.");
        }

        if (spec.MuzzlePosition is { } muzzlePosition) AddCannon(root, visual, muzzlePosition, spec.AmmoBars);

        if (spec.GrabButtonPosition is { } grabButton) root.AddComponent<GrabButtonPlacer>().Target = grabButton;
        StretchColliders(spec, root, vehicle);
        if (spec.Colliders.Length > 0) ReplaceBoxColliders(spec, root, vehicle, template);
        Tune(spec, vehicle);

        if (!string.IsNullOrEmpty(spec.PhysAttributePreset) && root.GetComponent<NotValuableObject>() is { } notValuable)
        {
            notValuable.physAttributePreset = ValuableBuilder.Preset("phys", spec.PhysAttributePreset, notValuable.physAttributePreset);
        }

        if (spec.IdleBrake) root.AddComponent<IdleBrake>();

        ItemBuilder.ApplyItem(spec, root);
        try
        {
            root.GetComponent<ItemAttributes>().icon = ItemBuilder.LoadIcon(spec.Folder);
        }
        catch (System.Exception e)
        {
            TankModPlugin.Logger.LogWarning($"Could not load an icon for '{spec.Name}': {e.Message}");
        }

        return root;
    }

    private static void StretchColliders(VehicleSpec spec, GameObject root, ItemVehicle vehicle)
    {
        var colliders = new System.Collections.Generic.HashSet<Collider>(root.GetComponentsInChildren<PhysGrabObjectCollider>(true).Select(c => c.GetComponent<Collider>()).Where(c => c != null));
        if (vehicle.vehicleColliders != null)
        {
            foreach (var c in vehicle.vehicleColliders) if (c != null) colliders.Add(c);
        }

        foreach (var c in colliders)
        {
            var t = c.transform;
            TankModPlugin.Logger.LogInfo($"{spec.Name}: collider '{t.name}' {c.GetType().Name} pos={t.localPosition} scale={t.localScale} rot={t.localEulerAngles} trigger={c.isTrigger}");
            if (c.isTrigger || spec.ColliderScale == Vector3.one) continue;

            // The stretch is meant in the vehicle's axes, but a collider can be rotated (the wheel capsules lie on their
            // side). Work out which of the collider's own axes lines up with each vehicle axis and scale that one.
            var rotation = Matrix4x4.Rotate(t.localRotation);
            var local = t.localScale;
            for (var i = 0; i < 3; i++)
            {
                var axis = (Vector3)rotation.GetColumn(i);
                var factor = new Vector3(axis.x * spec.ColliderScale.x, axis.y * spec.ColliderScale.y, axis.z * spec.ColliderScale.z).magnitude;
                local[i] *= factor;
            }

            t.localPosition = Vector3.Scale(t.localPosition, spec.ColliderScale);
            t.localScale = local;
        }

        // Keep the ground checks and wheel points where the (now longer) wheels are.
        foreach (var point in new Transform?[] { vehicle.frontGroundedTransform, vehicle.backGroundedTransform, vehicle.wheelFront, vehicle.wheelBack, vehicle.wheelFrontForcePoint, vehicle.wheelBackForcePoint })
        {
            if (point == null) continue;
            TankModPlugin.Logger.LogInfo($"{spec.Name}: wheel/ground point '{point.name}' parent='{point.parent?.name}' pos={point.localPosition}");
            if (point.parent == root.transform || point.parent == vehicle.meshTransform)
            {
                point.localPosition = Vector3.Scale(point.localPosition, spec.ColliderScale);
            }
        }
    }

    private static void Tune(VehicleSpec spec, ItemVehicle vehicle)
    {
        if (spec.LiftForce >= 0f) vehicle.liftForce = spec.LiftForce;
        if (spec.ForwardLiftMultiplier >= 0f) vehicle.forwardLiftMultiplier = spec.ForwardLiftMultiplier;
        if (spec.MaxForwardLift >= 0f) vehicle.maxForwardLift = spec.MaxForwardLift;
        if (spec.HyperMaxSpeed >= 0f) vehicle.hyperMaxSpeed = spec.HyperMaxSpeed;
        if (spec.MaxForwardSpeed >= 0f) vehicle.maxForwardSpeed = spec.MaxForwardSpeed;
        vehicle.hasDrifting = spec.HasDrifting;
        vehicle.enableRampBoost = spec.EnableRampBoost;
        vehicle.enableWheelPhysics = spec.WheelLiftPhysics;
    }

    private static void AddCannon(GameObject root, Transform visual, Vector3 muzzlePosition, int ammoBars)
    {
        var source = Resources.Load<GameObject>("Items/Item Cart Cannon")?.GetComponent<ItemCartCannon>();
        if (source == null)
        {
            TankModPlugin.Logger.LogWarning("Tank cannon: 'Items/Item Cart Cannon' not found, the tank will not be able to shoot.");
            return;
        }

        TankCannon.Source = source;

        var muzzle = new GameObject("Muzzle").transform;
        muzzle.SetParent(visual, false);
        muzzle.localPosition = muzzlePosition;
        muzzle.localRotation = Quaternion.identity;

        var cannon = root.AddComponent<TankCannon>();
        cannon.Muzzle = muzzle;

        if (ammoBars > 0 && root.GetComponent<ItemBattery>() is { } battery)
        {
            battery.batteryBars = ammoBars;
            cannon.UseBatteryAsAmmo = true;
        }
    }

    /// <summary>
    /// Swaps the scooter's box colliders for the spec's own (fitted to the new model). The wheel capsules stay: they are
    /// what the vehicle rolls on.
    /// </summary>
    private static void ReplaceBoxColliders(VehicleSpec spec, GameObject root, ItemVehicle vehicle, GameObject template)
    {
        var physicsMaterial = ValuableBuilder.GetPhysicsMaterial(template.GetComponentInChildren<Collider>(true));

        var removed = 0;
        foreach (var marker in root.GetComponentsInChildren<PhysGrabObjectCollider>(true).ToList())
        {
            if (marker == null || marker.GetComponent<BoxCollider>() is not { isTrigger: false }) continue;
            Object.DestroyImmediate(marker.gameObject);
            removed++;
        }

        var before = new System.Collections.Generic.HashSet<Collider>(root.GetComponentsInChildren<Collider>(true));
        ValuableBuilder.BuildColliders(spec.Colliders, root.transform, physicsMaterial);
        var added = root.GetComponentsInChildren<Collider>(true).Where(c => !before.Contains(c)).ToList();

        // The game keeps a list of the vehicle's own colliders (used to ignore collisions with the rider).
        var kept = (vehicle.vehicleColliders ?? System.Array.Empty<Collider>()).Where(c => c != null);
        vehicle.vehicleColliders = kept.Concat(added).ToArray();

        TankModPlugin.Logger.LogInfo($"{spec.Name}: replaced {removed} box colliders with {added.Count} fitted ones.");
    }
}
