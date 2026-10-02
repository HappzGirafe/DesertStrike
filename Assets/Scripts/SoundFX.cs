using System;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

/// <summary>Procedurally generated sound effects, so the project needs no audio files.</summary>
public static class SoundFX
{
    const int SampleRate = 44100;
    const float TwoPi = Mathf.PI * 2f;

    public static AudioClip Shot, KnifeSwing, Reload, Step, HitMarker, Headshot, Hurt, Buy, RoundStart, DryFire,
                            RocketLaunch, Explosion, Beep;

    static readonly System.Random noise = new System.Random(7);

    public static void Init()
    {
        if (Shot != null) return;
        Shot = Make("Shot", 0.45f, 0.35f, t => Noise() * Mathf.Exp(-t * 22f) * 0.8f
                                              + Mathf.Sin(t * TwoPi * 70f) * Mathf.Exp(-t * 14f) * 0.7f);
        KnifeSwing = Make("Knife", 0.18f, 0.2f, t => Noise() * Mathf.Exp(-Mathf.Pow((t - 0.07f) / 0.03f, 2f)) * 0.5f);
        Reload = Make("Reload", 0.7f, 0.6f, t => Noise() * (Click(t, 0f) + Click(t, 0.2f) + Click(t, 0.55f)) * 0.6f);
        Step = Make("Step", 0.15f, 0.08f, t => Noise() * Mathf.Exp(-t * 35f) * 0.6f);
        HitMarker = Make("Hit", 0.1f, 1f, t => Mathf.Sin(t * TwoPi * 1800f) * Mathf.Exp(-t * 50f) * 0.35f);
        Headshot = Make("Headshot", 0.25f, 1f, t => Mathf.Sin(t * TwoPi * 2600f) * Mathf.Exp(-t * 22f) * 0.4f
                                                     + Mathf.Sin(t * TwoPi * 1300f) * Mathf.Exp(-t * 30f) * 0.3f);
        Hurt = Make("Hurt", 0.3f, 0.3f, t => Mathf.Sin(t * TwoPi * 110f) * Mathf.Exp(-t * 12f) * 0.6f
                                             + Noise() * Mathf.Exp(-t * 30f) * 0.3f);
        Buy = Make("Buy", 0.2f, 1f, t => t < 0.08f
            ? Mathf.Sin(t * TwoPi * 880f) * Mathf.Exp(-t * 20f) * 0.3f
            : Mathf.Sin(t * TwoPi * 1320f) * Mathf.Exp(-(t - 0.08f) * 20f) * 0.3f);
        RoundStart = Make("RoundStart", 0.3f, 1f, t => (t < 0.1f || (t > 0.15f && t < 0.25f) ? 1f : 0f)
                                                         * Mathf.Sin(t * TwoPi * 1000f) * 0.2f);
        DryFire = Make("DryFire", 0.08f, 0.7f, t => Noise() * Click(t, 0f) * 0.5f);
        RocketLaunch = Make("RocketLaunch", 0.6f, 0.15f, t => Noise() * Mathf.Exp(-t * 6f) * Mathf.Min(1f, t * 40f) * 0.9f);
        Explosion = Make("Explosion", 1.4f, 0.08f, t => Noise() * Mathf.Exp(-t * 3.5f) * 1.6f
                                                        + Mathf.Sin(t * TwoPi * 40f) * Mathf.Exp(-t * 4f) * 0.8f);
        Beep = Make("Beep", 0.09f, 1f, t => Mathf.Sin(t * TwoPi * 2100f) * Mathf.Min(1f, (0.09f - t) * 60f) * 0.35f);
    }

    public static void PlayShot(WeaponData weapon, Vector3 position, bool own)
    {
        if (weapon.Explosive)
            Play(RocketLaunch, position, own ? 0.7f : 1f, Random.Range(0.95f, 1.05f), !own, 120f);
        else if (weapon.IsMelee)
            Play(KnifeSwing, position, 0.5f, Random.Range(0.9f, 1.1f), !own, 15f);
        else
            Play(Shot, position, own ? 0.5f : 0.9f, weapon.ShotPitch * Random.Range(0.95f, 1.05f), !own, 110f);
    }

    public static void Play(AudioClip clip, Vector3 position, float volume, float pitch = 1f, bool spatial = true, float maxDistance = 60f)
    {
        if (clip == null) return;
        var go = new GameObject("Sfx");
        go.transform.position = position;
        var source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = volume;
        source.pitch = pitch;
        source.spatialBlend = spatial ? 1f : 0f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 2f;
        source.maxDistance = maxDistance;
        source.dopplerLevel = 0f;
        source.Play();
        Object.Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
    }

    static float Noise() => (float)(noise.NextDouble() * 2.0 - 1.0);

    static float Click(float t, float at) => t >= at ? Mathf.Exp(-(t - at) * 350f) : 0f;

    static AudioClip Make(string name, float seconds, float lowpass, Func<float, float> wave)
    {
        int length = Mathf.CeilToInt(seconds * SampleRate);
        var data = new float[length];
        float filtered = 0f;
        for (int i = 0; i < length; i++)
        {
            filtered += (wave(i / (float)SampleRate) - filtered) * lowpass;
            data[i] = Mathf.Clamp(filtered, -1f, 1f);
        }
        var clip = AudioClip.Create(name, length, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
