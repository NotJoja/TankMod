using UnityEngine;

namespace TankMod.Runtime;

/// <summary>A drivable shop item built on the game's own vehicle (the semiscooter): own model, same driving logic.</summary>
internal sealed class VehicleSpec : ItemSpec
{
    /// <summary>Model groups of the OBJ and their textures. One mesh renderer per entry.</summary>
    public MeshPartSpec[] Parts = System.Array.Empty<MeshPartSpec>();

    public Vector3 VisualPosition;
    public Vector3 VisualEuler;
    public Vector3 VisualScale = Vector3.one;

    /// <summary>Moves the seat, the mount-in start point and the grab point (all relative to where they are on the scooter).</summary>
    public Vector3 SeatOffset;
    /// <summary>
    /// Shots the cannon holds (battery bars, one per shot; chargeable like the guns). The scooter uses its battery as
    /// health, so this detaches it from the vehicle (which makes the vehicle indestructible). 0 = unlimited shots.
    /// </summary>
    public int AmmoBars;

    /// <summary>Barrel tip, in the model's own coordinates. Set to give the vehicle a cannon (right click fires).</summary>
    public Vector3? MuzzlePosition;

    /// <summary>Where the grab button should sit, in the vehicle's own coordinates. Null leaves it where the scooter has it.</summary>
    public Vector3? GrabButtonPosition;

    /// <summary>Stretches the scooter's physics colliders (position and size) to fit the new model.</summary>
    public Vector3 ColliderScale = Vector3.one;

    // Driving tuning. Values below 0 keep the scooter's value.
    public float LiftForce = -1f;
    public float ForwardLiftMultiplier = -1f;
    public float MaxForwardLift = -1f;
    public float HyperMaxSpeed = -1f;
    public float MaxForwardSpeed = -1f;
    public bool HasDrifting = true;
    public bool EnableRampBoost = true;

    /// <summary>The scooter's wheels push it up in proportion to speed (part of how it hovers). Off for ground vehicles.</summary>
    public bool WheelLiftPhysics = true;

    /// <summary>Phys attribute preset (sets the mass); empty keeps the scooter's.</summary>
    public string PhysAttributePreset = "";

    /// <summary>Add friction while nobody is riding or holding it.</summary>
    public bool IdleBrake;
}
