using UnityEngine;

/// <summary>
/// Client side of everyone else in a network match: glides the body towards where the host's
/// snapshots say it is, and stands it back up when the host respawns it.
/// </summary>
[RequireComponent(typeof(Combatant))]
public class PuppetController : MonoBehaviour
{
    public Combatant Self { get; private set; }
    public CharacterBody Body { get; private set; }

    Vector3 targetPosition;
    Quaternion targetRotation;
    int spawnCount = -1;

    public void Setup()
    {
        Self = GetComponent<Combatant>();
        Body = gameObject.AddComponent<CharacterBody>();
        Body.Setup();
        targetPosition = transform.position;
        targetRotation = transform.rotation;
        if (!Self.IsAlive) Body.ForceDead();
    }

    public void ApplyState(Vector3 position, float yaw, int spawn)
    {
        targetPosition = position;
        targetRotation = Quaternion.Euler(0f, yaw, 0f);
        if (spawn == spawnCount) return;

        // Respawned (or first seen): jump straight there.
        spawnCount = spawn;
        transform.SetPositionAndRotation(position, targetRotation);
        if (Self.IsAlive) Body.Revive();
    }

    void Update()
    {
        float blend = 1f - Mathf.Exp(-18f * Time.deltaTime);
        transform.SetPositionAndRotation(Vector3.Lerp(transform.position, targetPosition, blend),
                                         Quaternion.Slerp(transform.rotation, targetRotation, blend));
    }
}
