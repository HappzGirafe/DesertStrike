using UnityEngine;

/// <summary>
/// RPG rocket: flies straight and explodes on impact (or after a few seconds) with splash damage.
/// On a network client rockets are visual only; the host's rocket does the damage.
/// </summary>
public class Rocket : MonoBehaviour
{
    const float Lifetime = 4f;
    const float SelfDamageScale = 0.5f;

    Combatant shooter;
    WeaponData weapon;
    Vector3 velocity;
    float explodeAt;
    bool visualOnly;
    Transform spinning;   // a skin's projectile turns while it flies

    public static void Launch(Combatant shooter, Vector3 position, Vector3 direction, WeaponData weapon, bool visualOnly = false)
    {
        if (!visualOnly) GameManager.Instance.Net.RecordRocket(shooter, position, direction, weapon);

        var go = new GameObject("Rocket");
        go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
        // A skin can bring its own projectile (the Web skin fires a spider-web net instead of a rocket).
        var projectile = WeaponModels.BuildProjectile(WeaponSkins.SkinOf(shooter, weapon), go.transform);
        if (projectile != null)
        {
            var spinner = go.AddComponent<Rocket>();
            spinner.spinning = projectile;
            spinner.Setup(shooter, weapon, direction, visualOnly);
            return;
        }
        Effects.Shape(PrimitiveType.Cylinder, go.transform, Vector3.zero, new Vector3(0.09f, 0.25f, 0.09f),
                      new Color(0.3f, 0.35f, 0.25f), euler: new Vector3(90f, 0f, 0f));
        Effects.Shape(PrimitiveType.Sphere, go.transform, new Vector3(0f, 0f, 0.26f), new Vector3(0.11f, 0.11f, 0.18f),
                      new Color(0.22f, 0.22f, 0.22f));
        var flame = Effects.Shape(PrimitiveType.Sphere, go.transform, new Vector3(0f, 0f, -0.32f), new Vector3(0.12f, 0.12f, 0.3f), Color.white);
        flame.GetComponent<Renderer>().sharedMaterial = Effects.Glow(new Color(1f, 0.6f, 0.2f));

        if (GameSettings.DynamicLights)
        {
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.6f, 0.25f);
            light.intensity = 2f;
            light.range = 5f;
        }

        go.AddComponent<Rocket>().Setup(shooter, weapon, direction, visualOnly);
    }

    void Setup(Combatant shooter, WeaponData weapon, Vector3 direction, bool visualOnly)
    {
        this.shooter = shooter;
        this.weapon = weapon;
        velocity = direction * weapon.ProjectileSpeed;
        explodeAt = Time.time + Lifetime;
        this.visualOnly = visualOnly;
    }

    void Update()
    {
        if (spinning != null) spinning.Rotate(0f, 0f, 240f * Time.deltaTime, Space.Self);
        Vector3 step = velocity * Time.deltaTime;
        if (Ballistics.FirstHit(transform.position, velocity.normalized, step.magnitude, shooter, out var hit))
        {
            Explode(hit.point + hit.normal * 0.2f);
            return;
        }
        transform.position += step;
        if (Time.time >= explodeAt) Explode(transform.position);
    }

    void Explode(Vector3 point)
    {
        Destroy(gameObject);
        Effects.Explosion(point, weapon.BlastRadius);
        SoundFX.Play(SoundFX.Explosion, point, 1f, Random.Range(0.9f, 1.05f), true, 150f);
        // Visual-only (network client), or the match was restarted while the rocket was in the air.
        if (visualOnly || shooter == null) return;

        GameManager.Instance.ReportNoise(point, shooter.Team, 60f);
        foreach (var target in GameManager.Instance.Combatants)
        {
            if (!target.IsAlive) continue;
            float distance = Vector3.Distance(point, target.ChestPosition);
            if (distance > weapon.BlastRadius) continue;
            if (!Ballistics.HasLineOfSight(point, target.ChestPosition) && !Ballistics.HasLineOfSight(point, target.HeadPosition)) continue;

            float damage = weapon.Damage * (1f - distance / weapon.BlastRadius);
            if (target == shooter) damage *= SelfDamageScale;
            var result = target.TakeExplosion(shooter, weapon, damage);
            if (result == HitResult.None || target == shooter) continue;
            Effects.Blood(target.ChestPosition);
            GameManager.Instance.Net.RecordBlood(target.ChestPosition);
            GameManager.Instance.ReportHit(shooter, false, result == HitResult.Kill);
        }
    }
}
