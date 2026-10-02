using UnityEngine;

public enum GraphicsQuality { Low, Medium, High }

/// <summary>
/// Graphics quality, frame limit, FPS counter and mouse sensitivity, saved between sessions.
/// The frame limit matters most for laptops: without it the game draws as many frames as the
/// hardware can, which keeps a passively cooled MacBook Air at full load.
/// </summary>
public static class GameSettings
{
    public static readonly int[] FrameLimits = { 30, 60, 120 };

    const string QualityKey = "DesertStrike.quality";
    const string FrameLimitKey = "DesertStrike.fps";
    const string ShowFpsKey = "DesertStrike.showFps";
    const string SensitivityKey = "DesertStrike.sensitivity";

    public static GraphicsQuality Quality { get; private set; } = GraphicsQuality.High;
    public static int FrameLimit { get; private set; } = 60;
    public static bool ShowFps { get; private set; }

    /// <summary>Real lights for muzzle flashes and rockets (each one makes nearby objects draw again).</summary>
    public static bool DynamicLights => Quality == GraphicsQuality.High;

    public static void Load()
    {
        // Macs (often fanless laptops) start on Medium; everything else on High.
        var fallback = Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor
            ? GraphicsQuality.Medium
            : GraphicsQuality.High;
        Quality = (GraphicsQuality)Mathf.Clamp(PlayerPrefs.GetInt(QualityKey, (int)fallback), 0, 2);
        FrameLimit = PlayerPrefs.GetInt(FrameLimitKey, 60);
        ShowFps = PlayerPrefs.GetInt(ShowFpsKey, 0) == 1;
        PlayerController.MouseSensitivity = PlayerPrefs.GetFloat(SensitivityKey, 2f);
        Apply();
    }

    public static void SetQuality(GraphicsQuality quality)
    {
        Quality = quality;
        PlayerPrefs.SetInt(QualityKey, (int)quality);
        Apply();
    }

    public static void SetFrameLimit(int fps)
    {
        FrameLimit = fps;
        PlayerPrefs.SetInt(FrameLimitKey, fps);
        Apply();
    }

    public static void SetShowFps(bool show)
    {
        ShowFps = show;
        PlayerPrefs.SetInt(ShowFpsKey, show ? 1 : 0);
    }

    public static void SetSensitivity(float sensitivity)
    {
        PlayerController.MouseSensitivity = sensitivity;
        PlayerPrefs.SetFloat(SensitivityKey, sensitivity);
    }

    /// <summary>Uses these settings for this run only, without saving them (command-line tests).</summary>
    public static void Override(GraphicsQuality quality, int frameLimit)
    {
        Quality = quality;
        FrameLimit = frameLimit;
        Apply();
    }

    public static void ShowFpsThisRun() => ShowFps = true;

    static void Apply()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = FrameLimit;

        switch (Quality)
        {
            case GraphicsQuality.Low:
                QualitySettings.shadows = ShadowQuality.Disable;
                QualitySettings.pixelLightCount = 0;
                QualitySettings.antiAliasing = 0;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
                QualitySettings.globalTextureMipmapLimit = 1;   // half-size textures
                break;
            case GraphicsQuality.Medium:
                QualitySettings.shadows = ShadowQuality.HardOnly;
                QualitySettings.shadowDistance = 55f;
                QualitySettings.shadowCascades = 2;
                QualitySettings.shadowResolution = ShadowResolution.Medium;
                QualitySettings.pixelLightCount = 1;
                QualitySettings.antiAliasing = 0;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
                QualitySettings.globalTextureMipmapLimit = 0;
                break;
            default:
                QualitySettings.shadows = ShadowQuality.All;
                QualitySettings.shadowDistance = 120f;
                QualitySettings.shadowCascades = 4;
                QualitySettings.shadowResolution = ShadowResolution.High;
                QualitySettings.pixelLightCount = 4;
                QualitySettings.antiAliasing = 2;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
                QualitySettings.globalTextureMipmapLimit = 0;
                break;
        }
    }
}
