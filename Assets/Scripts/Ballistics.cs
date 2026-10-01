using System.Collections.Generic;
using UnityEngine;

/// <summary>Hitscan shooting and line-of-sight checks.</summary>
public static class Ballistics
{
    // Characters live on the built-in "Ignore Raycast" layer so sight checks only see the map,
    // while bullets (which use an explicit all-layers mask) still hit them.
    public const int CharacterLayer = 2;
    public const int EnvironmentMask = ~(1 << CharacterLayer);

    static readonly RaycastHit[] hits = new RaycastHit[32];
    static readonly IComparer<RaycastHit> byDistance =
        Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));

    public static Vector3 ApplySpread(Vector3 direction, float degrees)
    {
        if (degrees <= 0f) return direction;
        Vector2 offset = Random.insideUnitCircle * Mathf.Tan(degrees * Mathf.Deg2Rad);
        return (Quaternion.LookRotation(direction) * new Vector3(offset.x, offset.y, 1f)).normalized;
    }

    public static void Fire(Combatant shooter, Vector3 origin, Vector3 direction, WeaponData weapon, Vector3 tracerFrom)
    {
        Vector3 end = origin + direction * weapon.Range;
        if (FirstHit(origin, direction, weapon.Range, shooter, out var hit))
        {
            end = hit.point;
            var target = hit.collider.GetComponentInParent<Combatant>();
            if (target != null)
            {
                var result = target.TakeHit(shooter, weapon, hit.point, out bool headshot);
                if (result != HitResult.None)
                {
                    Effects.Blood(hit.point);
                    if (shooter.IsPlayer) GameManager.Instance.ShowHitMarker(headshot, result == HitResult.Kill);
                }
            }
            else if (!weapon.IsMelee)
            {
                Effects.BulletHole(hit.point, hit.normal);
            }
        }
        if (!weapon.IsMelee) Effects.Tracer(tracerFrom, end);
    }

    /// <summary>Nearest hit along the ray on any layer, skipping <paramref name="ignore"/>'s own colliders.</summary>
    public static bool FirstHit(Vector3 origin, Vector3 direction, float distance, Combatant ignore, out RaycastHit hit)
    {
        int count = Physics.RaycastNonAlloc(origin, direction, hits, distance, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, 0, count, byDistance);
        for (int i = 0; i < count; i++)
        {
            if (ignore != null && hits[i].collider.GetComponentInParent<Combatant>() == ignore) continue;
            hit = hits[i];
            return true;
        }
        hit = default;
        return false;
    }

    public static bool HasLineOfSight(Vector3 from, Vector3 to) =>
        !Physics.Linecast(from, to, EnvironmentMask, QueryTriggerInteraction.Ignore);

    /// <summary>True when a living teammate stands between the shooter and the point.</summary>
    public static bool FriendInLine(Combatant shooter, Vector3 to)
    {
        Vector3 from = shooter.EyePosition;
        Vector3 delta = to - from;
        int count = Physics.RaycastNonAlloc(from, delta.normalized, hits, delta.magnitude, 1 << CharacterLayer, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var other = hits[i].collider.GetComponentInParent<Combatant>();
            if (other != null && other != shooter && other.IsAlive && other.Team == shooter.Team) return true;
        }
        return false;
    }
}
