using UnityEngine;

/// <summary>
/// Puffs an effect up and fades it out by shrinking, then destroys it, or parks it in the
/// <see cref="EffectPool"/> when <see cref="PoolKind"/> is set.
/// </summary>
public class ShrinkAndDie : MonoBehaviour
{
    public float Lifetime = 0.25f;
    public string PoolKind;

    float age;
    Vector3 startScale;

    /// <summary>Starts the effect again from full size (for a pooled object that was handed out again).</summary>
    public void Restart(Vector3 scale)
    {
        age = 0f;
        startScale = scale;
        transform.localScale = scale;
    }

    void Awake() => startScale = transform.localScale;

    void Update()
    {
        age += Time.deltaTime;
        float t = age / Lifetime;
        if (t >= 1f)
        {
            if (string.IsNullOrEmpty(PoolKind)) Destroy(gameObject);
            else EffectPool.Return(gameObject, PoolKind);
            return;
        }
        transform.localScale = startScale * (1f + t) * (1f - t);
    }
}
