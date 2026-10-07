using UnityEngine;

public enum GraphicsQuality { Low, Medium, High }

/// <summary>How voice chat decides when you talk: while V is held, or whenever you speak.</summary>
public enum VoiceMode { HoldV, OpenMic }

/// <summary>
/// Display, graphics quality, frame limit, FPS counter, voice chat and mouse sensitivity, saved between sessions.
/// The frame limit matters most for laptops: without it the game draws as many frames as the
/// hardware can, which keeps a passively cooled MacBook Air at full load.
/// </summary>
public static class GameSettings
{
    public static readonly int[] FrameLimits = { 30, 60, 120, 0 };   // 0 = unlimited
    public static readonly float[] RenderScales = { 1f, 0.75f, 0.5f };

    const string QualityKey = "DesertStrike.quality";
    const string FrameLimitKey = "DesertStrike.fps";
    const string ShowFpsKey = "DesertStrike.showFps";
    const string SensitivityKey = "DesertStrike.sensitivity";
    const string RenderScaleKey = "DesertStrike.renderScale";
    const string VoiceKey = "DesertStrike.voice";
    const string VoiceModeKey = "DesertStrike.voiceMode";

    public static GraphicsQuality Quality { get; private set; } = GraphicsQuality.High;
    /// <summary>Most frames per second, or 0 for no limit.</summary>
    public static int FrameLimit { get; private set; } = 60;
    public static bool ShowFps { get; private set; }

    /// <summary>The 3D view is drawn at this fraction of the screen resolution and stretched (menus and HUD stay sharp).</summary>
    public static float RenderScale { get; private set; } = 1f;

    /// <summary>Voice chat in LAN games (hold V to talk, hear the others).</summary>
    public static bool VoiceChat { get; private set; } = true;

    public static VoiceMode Voice { get; private set; } = VoiceMode.HoldV;

    /// <summary>Fullscreen or a window (Unity remembers the choice and the window size between sessions).</summary>
    public static bool Fullscreen => Screen.fullScreenMode != FullScreenMode.Windowed;

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
        RenderScale = Mathf.Clamp(PlayerPrefs.GetFloat(RenderScaleKey, 1f), 0.5f, 1f);
        VoiceChat = PlayerPrefs.GetInt(VoiceKey, 1) == 1;
        Voice = (VoiceMode)Mathf.Clamp(PlayerPrefs.GetInt(VoiceModeKey, 0), 0, 1);
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

    public static void SetRenderScale(float scale)
    {
        RenderScale = scale;
        PlayerPrefs.SetFloat(RenderScaleKey, scale);
    }

    public static void SetVoiceChat(bool on)
    {
        VoiceChat = on;
        PlayerPrefs.SetInt(VoiceKey, on ? 1 : 0);
    }

    public static void SetVoiceMode(VoiceMode mode)
    {
        Voice = mode;
        PlayerPrefs.SetInt(VoiceModeKey, (int)mode);
    }

    /// <summary>Fullscreen at the screen's own resolution, or a window of 1280x720 (smaller on small screens).</summary>
    public static void SetFullscreen(bool fullscreen)
    {
        var desktop = Screen.currentResolution;
        if (fullscreen)
        {
            Screen.SetResolution(desktop.width, desktop.height, FullScreenMode.FullScreenWindow);
        }
        else
        {
            int width = Mathf.Min(1280, Mathf.RoundToInt(desktop.width * 0.8f));
            Screen.SetResolution(width, width * 9 / 16, FullScreenMode.Windowed);
        }
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

    public static void HideFpsThisRun() => ShowFps = false;

    public static void RenderScaleThisRun(float scale) => RenderScale = Mathf.Clamp(scale, 0.25f, 1f);

    static void Apply()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = FrameLimit > 0 ? FrameLimit : -1;

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
