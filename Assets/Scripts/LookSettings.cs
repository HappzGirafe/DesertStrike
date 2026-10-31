using UnityEngine;

/// <summary>
/// How fast the view turns while scoped: a share of the look sensitivity (<see cref="PlayerController.MouseSensitivity"/>,
/// which GameSettings keeps), for the mouse and for fingers alike. 35% to start with, as it always was before it
/// could be set. Saved on the device.
/// </summary>
public static class LookSettings
{
    public const float MinScopeShare = 0.1f, MaxScopeShare = 1f;
    const float DefaultScopeShare = 0.35f;
    const string ScopeShareKey = "DesertStrike.scopeSensitivity";

    static float? scopeShare;

    /// <summary>The share of the look sensitivity used while scoped (0.1 to 1).</summary>
    public static float ScopeShare => scopeShare ??= Read();

    /// <summary>Reads the saved setting (at start; it is also read the first time it is needed).</summary>
    public static void Load() => scopeShare = Read();

    public static void SetScopeShare(float share)
    {
        scopeShare = Sensible(share);
        PlayerPrefs.SetFloat(ScopeShareKey, scopeShare.Value);
    }

    static float Read() => Sensible(PlayerPrefs.GetFloat(ScopeShareKey, DefaultScopeShare));

    // Clamping lets a NaN through, and it would turn the scoped view into NaN.
    static float Sensible(float share) => float.IsFinite(share) ? Mathf.Clamp(share, MinScopeShare, MaxScopeShare) : DefaultScopeShare;
}
