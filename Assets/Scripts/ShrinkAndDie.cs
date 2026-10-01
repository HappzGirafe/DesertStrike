using UnityEngine;

/// <summary>Puffs an effect up and fades it out by shrinking, then destroys it.</summary>
public class ShrinkAndDie : MonoBehaviour
{
    public float Lifetime = 0.25f;

    float age;
    Vector3 startScale;

    void Start() => startScale = transform.localScale;

    void Update()
    {
        age += Time.deltaTime;
        float t = age / Lifetime;
        if (t >= 1f)
        {
            Destroy(gameObject);
            return;
        }
        transform.localScale = startScale * (1f + t) * (1f - t);
    }
}
