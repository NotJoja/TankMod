using System.Reflection;
using Photon.Pun;
using UnityEngine;

namespace TankMod.Runtime;

/// <summary>
/// Fires the game's own cart-cannon shot (tracer, hit effects and damage) from the tank's barrel when the rider presses
/// the interact button (right click, which would otherwise honk). Network flow, like the cart cannon: the rider asks the
/// host, the host works out where the shot lands and tells everyone to draw it.
/// </summary>
internal sealed class TankCannon : MonoBehaviour
{
    /// <summary>The vanilla cart cannon that supplies the bullet prefab, curve and sounds. Set by the builder.</summary>
    public static ItemCartCannon? Source;

    public Transform? Muzzle;
    public float Cooldown = 1.5f;
    public float Range = 60f;
    public float RecoilImpulse = 20f;

    // The cart cannon's shot does 500 to enemies; the shotgun does 50 per pellet. The tank hits much harder.
    public int EnemyDamage = 2000;
    public int PlayerDamage = 75;
    public float EnemyStunTime = 8f;
    public float EnemyHitForce = 70f;
    public float PhysHitForce = 80f;
    public float PlayerHitForce = 60f;
    public float BlastScale = 1.75f;

    /// <summary>One battery bar per shot (the battery is chargeable like the guns' ones).</summary>
    public bool UseBatteryAsAmmo;

    private static readonly FieldInfo? LocalSeatField = typeof(ItemVehicle).GetField("localSeatIndex", BindingFlags.Instance | BindingFlags.NonPublic);

    private ItemVehicle? _vehicle;
    private PhotonView? _view;
    private PhysGrabObject? _grabObject;
    private ParticleScriptExplosion? _explosion;
    private ItemBattery? _battery;
    private float _cooldownTimer;

    private void Awake()
    {
        _vehicle = GetComponent<ItemVehicle>();
        _view = GetComponent<PhotonView>();
        _grabObject = GetComponent<PhysGrabObject>();
        _explosion = GetComponent<ParticleScriptExplosion>();
        _battery = GetComponent<ItemBattery>();
    }

    private static readonly FieldInfo? VehicleBatteryField = typeof(ItemVehicle).GetField("itemBattery", BindingFlags.Instance | BindingFlags.NonPublic);

    private System.Collections.IEnumerator Start()
    {
        yield return null; // the vehicle script picks up its battery in its own Awake/Start; wait until it has

        // The scooter treats its battery as health (it burns and explodes at 0). Let go of it so it is only ammo.
        if (UseBatteryAsAmmo && _vehicle != null && VehicleBatteryField != null)
        {
            VehicleBatteryField.SetValue(_vehicle, null);
            TankModPlugin.Logger.LogInfo("Tank cannon: battery detached from the vehicle, used as ammo.");
        }
    }

    private bool HasAmmo()
    {
        return !UseBatteryAsAmmo || _battery == null || _battery.batteryLifeInt > 0;
    }

    private void Update()
    {
        if (_cooldownTimer > 0f) _cooldownTimer -= Time.deltaTime;
        if (_vehicle == null || Muzzle == null) return;

        var seat = LocalSeatField != null ? (int)LocalSeatField.GetValue(_vehicle) : -1;
        if (seat < 0 || _cooldownTimer > 0f) return;
        if (!SemiFunc.InputDown(InputKey.Interact)) return;
        if (!HasAmmo()) return; // out of shots: charge it

        _cooldownTimer = Cooldown;
        if (SemiFunc.IsMasterClientOrSingleplayer()) Shoot();
        else _view?.RPC(nameof(TankFireRequestRPC), RpcTarget.MasterClient);
    }

    [PunRPC]
    public void TankFireRequestRPC(PhotonMessageInfo info = default)
    {
        if (SemiFunc.IsMasterClientOrSingleplayer()) Shoot();
    }

    // Host only: find where the shot lands, push the tank back, tell everybody.
    private void Shoot()
    {
        if (Muzzle == null || _view == null || !HasAmmo()) return;
        if (UseBatteryAsAmmo) _battery?.RemoveFullBar(1);

        var origin = Muzzle.position;
        var direction = Muzzle.forward;
        var end = origin + direction * Range;
        var surface = false;
        if (Physics.Raycast(origin, direction, out var hit, Range, (int)SemiFunc.LayerMaskGetVisionObstruct() + LayerMask.GetMask("Enemy")))
        {
            end = hit.point;
            surface = true;
        }

        if (_grabObject != null && _grabObject.rb != null)
        {
            _grabObject.rb.AddForceAtPosition(-direction * RecoilImpulse, origin, ForceMode.Impulse);
        }

        if (SemiFunc.IsMultiplayer()) _view.RPC(nameof(TankShootRPC), RpcTarget.All, end, true, surface);
        else TankShootRPC(end, true, surface);
    }

    [PunRPC]
    public void TankShootRPC(Vector3 endPosition, bool hit, bool surfaceHit, PhotonMessageInfo info = default)
    {
        if (!SemiFunc.MasterOnlyRPC(info)) return;
        if (Source == null || Source.bulletPrefab == null || Muzzle == null)
        {
            TankModPlugin.Logger.LogWarning("Tank cannon: no bullet prefab available.");
            return;
        }

        var bullet = Instantiate(Source.bulletPrefab, Muzzle.position, Muzzle.rotation).GetComponent<ItemGunBullet>();
        bullet.hitPosition = endPosition;
        bullet.bulletHit = hit;
        // These two exist in the game but not in the older libraries this project compiles against.
        SetField(bullet, "surfaceHit", surfaceHit);
        SetField(bullet, "surfaceImpactStrength", 0f);
        bullet.shootLineWidthCurve = Source.animationCurve;

        Source.soundHit.Play(endPosition);
        Source.shootSound.Play(Muzzle.position);
        GameDirector.instance.CameraShake.ShakeDistance(6f, 3f, 12f, Muzzle.position, 0.15f);
        GameDirector.instance.CameraImpact.ShakeDistance(10f, 3f, 12f, Muzzle.position, 0.08f);
        _explosion?.PlayExplosionSoundMedium(endPosition);

        // Credit the rider with the damage, and make the shot hit much harder than the cart cannon's.
        var hurt = bullet.GetComponentInChildren<HurtCollider>(true);
        if (hurt != null)
        {
            hurt.enemyDamage = EnemyDamage;
            hurt.playerDamage = PlayerDamage;
            hurt.enemyStunTime = EnemyStunTime;
            hurt.enemyHitForce = EnemyHitForce;
            hurt.physHitForce = PhysHitForce;
            hurt.playerHitForce = PlayerHitForce;
            hurt.transform.localScale *= BlastScale;
        }

        var rider = _vehicle != null && _vehicle.seats.Length > 0 ? _vehicle.seats[0].seatedPlayer : null;
        if (hurt != null && rider != null)
        {
            hurt.playerCausingHurt = rider;
            hurt.playerHitFullRuckus = true;
        }

        bullet.ActivateAll();
    }

    private static void SetField(object target, string name, object value)
    {
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.SetValue(target, value);
    }
}
