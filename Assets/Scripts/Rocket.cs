using UnityEngine;

/// <summary>RPG rocket: flies straight and explodes on impact (or after a few seconds) with splash damage.</summary>
public class Rocket : MonoBehaviour
{
    const float Lifetime = 4f;
    const float SelfDamageScale = 0.5f;

    Combatant shooter;
    WeaponData weapon;
    Vector3 velocity;
    float explodeAt;

    public static void Launch(Combatant shooter, Vector3 position, Vector3 direction, WeaponData weapon)
    {
        var go = new GameObject("Rocket");
        go.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
        Effects.Shape(PrimitiveType.Cylinder, go.transform, Vector3.zero, new Vector3(0.09f, 0.25f, 0.09f),
                      new Color(0.3f, 0.35f, 0.25f), euler: new Vector3(90f, 0f, 0f));
        Effects.Shape(PrimitiveType.Sphere, go.transform, new Vector3(0f, 0f, 0.26f), new Vector3(0.11f, 0.11f, 0.18f),
                      new Color(0.22f, 0.22f, 0.22f));
        var flame = Effects.Shape(PrimitiveType.Sphere, go.transform, new Vector3(0f, 0f, -0.32f), new Vector3(0.12f, 0.12f, 0.3f), Color.white);
        flame.GetComponent<Renderer>().sharedMaterial = Effects.Glow(new Color(1f, 0.6f, 0.2f));

        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.6f, 0.25f);
        light.intensity = 2f;
        light.range = 5f;

        var rocket = go.AddComponent<Rocket>();
        rocket.shooter = shooter;
        rocket.weapon = weapon;
        rocket.velocity = direction * weapon.ProjectileSpeed;
        rocket.explodeAt = Time.time + Lifetime;
    }

    void Update()
    {
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
        if (shooter == null) return; // the match was restarted while the rocket was in the air

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
            if (shooter.IsPlayer) GameManager.Instance.ShowHitMarker(false, result == HitResult.Kill);
        }
    }
}
