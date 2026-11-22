using System.Reflection;
using UnityEngine;

namespace TankMod.Runtime;

/// <summary>
/// The scooter's physics are built to coast and hover. While nobody is riding or holding the vehicle this adds friction
/// so it stays put (horizontal speed is damped and it cannot be pushed upwards). While someone rides it, gravity is
/// restored and the climb speed capped, so it cannot drift into the air.
/// </summary>
internal sealed class IdleBrake : MonoBehaviour
{
    public float HorizontalDamping = 4f;
    public float AngularDamping = 4f;
    public float MaxUpwardSpeed = 0.5f;

    /// <summary>While someone rides it: how fast it may rise. Hills and stairs need some, drifting into the air does not.</summary>
    public float SeatedMaxUpwardSpeed = 2f;

    private ItemVehicle? _vehicle;
    private PhysGrabObject? _grabObject;
    private Rigidbody? _body;

    // The project compiles against an older UnityEngine where this is called 'velocity'; the game (Unity 6) calls it
    // 'linearVelocity'. Look it up by name so it works with both.
    private static readonly PropertyInfo? VelocityProperty =
        typeof(Rigidbody).GetProperty("linearVelocity") ?? typeof(Rigidbody).GetProperty("velocity");

    private void Awake()
    {
        _vehicle = GetComponent<ItemVehicle>();
        _grabObject = GetComponent<PhysGrabObject>();
        _body = GetComponent<Rigidbody>();
    }

    private float _logTimer;
    private int _logCount;

    private static object? Field(object target, string name)
    {
        return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(target);
    }

    // Temporary: print what the tank is doing while somebody rides it, to find out why it drifts upwards.
    private void LogState()
    {
        _logTimer -= Time.fixedDeltaTime;
        if (_logTimer > 0f || _logCount >= 80 || _vehicle == null || _body == null) return;
        _logTimer = 0.5f;
        _logCount++;
        var mesh = _vehicle.meshTransform;
        TankModPlugin.Logger.LogInfo(
            $"TankDebug #{_logCount}: y={_body.position.y:F2} vel={VelocityProperty?.GetValue(_body)} mass={_body.mass} gravity={_body.useGravity} " +
            $"front={Field(_vehicle, "isFrontGrounded")} back={Field(_vehicle, "isBackGrounded")} airborne={Field(_vehicle, "isVehicleAirborne")} " +
            $"slopeValid={Field(_vehicle, "groundSlopeValid")} slope={Field(_vehicle, "groundSlopePitchAngle")} " +
            $"meshLocal={(mesh != null ? mesh.localPosition.ToString("F2") : "none")} meshRot={(mesh != null ? mesh.localEulerAngles.ToString("F1") : "none")} " +
            $"state={_vehicle.currentState}");
    }

    private void FixedUpdate()
    {
        if (_vehicle == null || _body == null || _body.isKinematic) return;
        if (!SemiFunc.IsMasterClientOrSingleplayer()) return; // the host simulates the physics

        var seated = false;
        foreach (var seat in _vehicle.seats)
        {
            if (seat.seatedPlayer != null) seated = true;
        }

        if (VelocityProperty == null) return;
        var velocity = (Vector3)VelocityProperty.GetValue(_body);

        if (seated)
        {
            LogState();
            // The game switches gravity off for a vehicle that has a rider (the scooter is held down by wheel and seat
            // forces instead), and the tank slowly drifted upwards. Give it its gravity back and cap how fast it can rise.
            _body.AddForce(Physics.gravity * _body.mass, ForceMode.Force);
            if (velocity.y > SeatedMaxUpwardSpeed)
            {
                velocity.y = SeatedMaxUpwardSpeed;
                VelocityProperty.SetValue(_body, velocity);
            }

            return;
        }

        // Nobody rides it; leave it alone while somebody is holding it with the grab beam.
        if (_grabObject != null && _grabObject.grabbed) return;

        var factor = Mathf.Clamp01(1f - HorizontalDamping * Time.fixedDeltaTime);
        velocity.x *= factor;
        velocity.z *= factor;
        if (velocity.y > MaxUpwardSpeed) velocity.y = MaxUpwardSpeed;
        VelocityProperty.SetValue(_body, velocity);
        _body.angularVelocity *= Mathf.Clamp01(1f - AngularDamping * Time.fixedDeltaTime);
    }
}
