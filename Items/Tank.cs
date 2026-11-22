using TankMod.Runtime;
using UnityEngine;

namespace TankMod.Items;

// A tank (model: "Tank" by amanpatel9725 on Sketchfab, CC-BY-4.0) driven with the game's scooter logic.
//
// The model is about 8 x 3 x 2.5 m (barrel included). Everything below is worked out from model measurements and one
// scale, so changing Scale resizes the model, the collision boxes, the seat and the grab button together.
//   Hull + tracks : model x +-1.49, y 0..1.15, z -3.99..1.82   (the barrel sticks out to z 3.99)
//   Turret        : model x +-1.20, y 1.15..2.5, z -2.49..1.46
//   Hull centre is 1.085 behind the model's middle, so the model is moved forward by that to centre the hull.
// 0.35 makes the hull about 2.0 x 1.0 m, roughly a shopping cart (1.25 x 2.0 m), so it fits the shop's checkout area.
internal static class Tank
{
    private const float Scale = 0.35f;

    // The scooter's wheel capsules reach about 0.2 below its origin; the model's bottom is put there.
    private const float GroundOffset = -0.22f;

    private const float HullCentreBehind = 1.085f;

    // Model coordinates -> vehicle coordinates.
    private static Vector3 ToVehicle(float x, float y, float z)
    {
        return new Vector3(x * Scale, y * Scale + GroundOffset, z * Scale + HullCentreBehind * Scale);
    }

    private static Vector3 Size(float x, float y, float z)
    {
        return new Vector3(x * Scale, y * Scale, z * Scale);
    }

    public static ItemSpec Spec => new VehicleSpec
    {
        Name = "Tank",
        ItemName = "Tank",
        Description = "A tank.",
        Template = "Items/Item Vehicle Semiscooter",
        KeepTemplateShopSettings = true,

        // Shop price ~200 (the shop shows Max / 1000 * 4: 48000..52000 gives 192..208).
        ValueMin = 48000f,
        ValueMax = 52000f,

        // 7 shots, one battery bar each, rechargeable like the guns.
        AmmoBars = 7,

        Folder = "tank",
        ObjFile = "tank.obj",
        Parts = new[]
        {
            new MeshPartSpec { Part = "body", Texture = "body.jpeg" },
            new MeshPartSpec { Part = "metal1", Texture = "metal1.jpeg" },
            new MeshPartSpec { Part = "wheel1", Texture = "wheel1.jpeg" },
        },

        VisualScale = Vector3.one * Scale,
        VisualPosition = new Vector3(0f, GroundOffset, HullCentreBehind * Scale),

        // The scooter's wheel capsules are about 3.1 m long; stretch them to the tank's wheelbase.
        ColliderScale = new Vector3(1f, 1f, 1.28f * Scale / 0.5f),

        // A tank drives on the ground: no hover lift, no ramp launches, no drifting, no hyper speed.
        LiftForce = 0f,
        ForwardLiftMultiplier = 0f,
        MaxForwardLift = 0f,
        HyperMaxSpeed = 0f,
        HasDrifting = false,
        EnableRampBoost = false,
        WheelLiftPhysics = false,

        // As heavy as the wizard cube (mass 6, the scooter has 1), and it stays put while nobody is in it.
        PhysAttributePreset = "PhysAttribute Heavy++",
        IdleBrake = true,

        // Collision boxes fitted to the hull and the turret. The barrel has none, so it cannot snag walls.
        Colliders = new[]
        {
            new ColliderSpec { Kind = ColliderKind.Box, Position = ToVehicle(0f, 0.66f, -1.085f), Size = Size(2.98f, 0.97f, 5.8f) },
            new ColliderSpec { Kind = ColliderKind.Box, Position = ToVehicle(0f, 1.825f, -0.515f), Size = Size(2.4f, 1.35f, 3.96f) },

            // The engine deck and fuel drums at the back stand taller than the hull box (model y up to 1.67, vehicle y 0.365).
            // This gives the grab button a solid surface at the height where you can see it.
            new ColliderSpec { Kind = ColliderKind.Box, Position = new Vector3(0f, 0.27f, -0.76f), Size = new Vector3(0.9f, 0.19f, 0.52f) },
        },

        // Barrel tip in the model's own coordinates (slightly past the end of the barrel). Right click fires.
        MuzzlePosition = new Vector3(-0.052f, 1.891f, 4.1f),

        // The player sits on top of the turret (its top is at model y 2.5), a little behind its middle.
        SeatOffset = new Vector3(0f, 2.5f * Scale + GroundOffset + 0.25f - 0.629f, -0.02f),

        // You grab a vehicle by aiming at one of its solid colliders; the hold point must then land inside the small
        // 'Semi Grab Area' box (about 0.38 x 0.29 x 0.33). So the button has to sit ON a collider surface: the top of the
        // engine-deck box above (y = 0.365), on the rear deck behind the turret. The visible handle goes to the same spot,
        // slightly above the deck so it is not buried in the model.
        GrabButtonPosition = new Vector3(0f, 0.39f, -0.64f),
    };
}
