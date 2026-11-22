using UnityEngine;

namespace TankMod.Runtime;

/// <summary>Spins its transform around the local Y axis while a player holds the valuable.</summary>
internal sealed class SpinWhenHeld : MonoBehaviour
{
    public float DegreesPerSecond = 90f;

    private PhysGrabObject? _grabObject;

    private void Awake()
    {
        _grabObject = GetComponentInParent<PhysGrabObject>();
    }

    private void Update()
    {
        if (_grabObject != null && _grabObject.grabbed)
        {
            transform.Rotate(0f, DegreesPerSecond * Time.deltaTime, 0f, Space.Self);
        }
    }
}
