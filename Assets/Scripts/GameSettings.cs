using UnityEngine;

public enum GraphicsQuality { Low, Medium, High }

/// <summary>How voice chat decides when you talk: while V is held, or whenever you speak.</summary>
public enum VoiceMode { HoldV, OpenMic }

/// <summary>
/// Display, graphics quality, frame limit, FPS counter, voice chat, mouse sensitivity and the crosshair, saved between sessions.
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
    const string CrosshairKey = "DesertStrike.crosshair.";

    // The crosshair, like Counter-Strike 2's: a colour (or your own), line length, thickness, gap, opacity, an
    // outline, a centre dot, a T shape, and whether it opens up with the spread (dynamic) or stays put (static).
    public static readonly string[] CrosshairColorNames = { "Green", "Yellow", "Red", "Blue", "Cyan", "White", "Custom" };
    static readonly Color[] CrosshairPresets =
    {
        new Color(0.35f, 1f, 0.35f), new Color(1f, 0.92f, 0.2f), new Color(1f, 0.27f, 0.27f),
        new Color(0.32f, 0.56f, 1f), new Color(0.3f, 1f, 1f), Color.white,
    };
    public static int CrosshairColorIndex { get; private set; }
    public static Color CrosshairCustom { get; private set; } = new Color(1f, 0.4f, 0.85f);
    public static float CrosshairLength { get; private set; } = 10f;
    public static float CrosshairThickness { get; private set; } = 2f;
    public static float CrosshairGap { get; private set; } = 3f;
    public static float CrosshairAlpha { get; private set; } = 0.9f;
    public static bool CrosshairOutline { get; private set; }
    public static bool CrosshairDot { get; private set; }
    public static bool CrosshairT { get; private set; }
    public static bool CrosshairDynamic { get; private set; } = true;

    public static Color CrosshairColor
    {
        get
        {
            var color = CrosshairColorIndex < CrosshairPresets.Length ? CrosshairPresets[CrosshairColorIndex] : CrosshairCustom;
            color.a = CrosshairAlpha;
            return color;
        }
    }

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
        // Phones and tablets start on Low, Macs (often fanless laptops) on Medium, everything else on High.
        var fallback = Application.isMobilePlatform ? GraphicsQuality.Low
            : Application.platform == RuntimePlatform.OSXPlayer || Application.platform == RuntimePlatform.OSXEditor
            ? GraphicsQuality.Medium
            : GraphicsQuality.High;
        Quality = (GraphicsQuality)Mathf.Clamp(PlayerPrefs.GetInt(QualityKey, (int)fallback), 0, 2);
        FrameLimit = PlayerPrefs.GetInt(FrameLimitKey, 60);
        ShowFps = PlayerPrefs.GetInt(ShowFpsKey, 0) == 1;
        // A phone's screen has many pixels for its small graphics chip: 3D at 75% to start with.
        RenderScale = Mathf.Clamp(PlayerPrefs.GetFloat(RenderScaleKey, Application.isMobilePlatform ? 0.75f : 1f), 0.5f, 1f);
        VoiceChat = PlayerPrefs.GetInt(VoiceKey, 1) == 1;
        Voice = (VoiceMode)Mathf.Clamp(PlayerPrefs.GetInt(VoiceModeKey, 0), 0, 1);
        PlayerController.MouseSensitivity = PlayerPrefs.GetFloat(SensitivityKey, 2f);
        LoadCrosshair();
        Apply();
    }

    static void LoadCrosshair()
    {
        CrosshairColorIndex = Mathf.Clamp(PlayerPrefs.GetInt(CrosshairKey + "color", 0), 0, CrosshairColorNames.Length - 1);
        CrosshairCustom = new Color(PlayerPrefs.GetFloat(CrosshairKey + "r", 1f), PlayerPrefs.GetFloat(CrosshairKey + "g", 0.4f),
                                    PlayerPrefs.GetFloat(CrosshairKey + "b", 0.85f));
        CrosshairLength = PlayerPrefs.GetFloat(CrosshairKey + "length", 10f);
        CrosshairThickness = PlayerPrefs.GetFloat(CrosshairKey + "thickness", 2f);
        CrosshairGap = PlayerPrefs.GetFloat(CrosshairKey + "gap", 3f);
        CrosshairAlpha = PlayerPrefs.GetFloat(CrosshairKey + "alpha", 0.9f);
        CrosshairOutline = PlayerPrefs.GetInt(CrosshairKey + "outline", 0) == 1;
        CrosshairDot = PlayerPrefs.GetInt(CrosshairKey + "dot", 0) == 1;
        CrosshairT = PlayerPrefs.GetInt(CrosshairKey + "t", 0) == 1;
        CrosshairDynamic = PlayerPrefs.GetInt(CrosshairKey + "dynamic", 1) == 1;
    }

    static void SaveCrosshair()
    {
        PlayerPrefs.SetInt(CrosshairKey + "color", CrosshairColorIndex);
        PlayerPrefs.SetFloat(CrosshairKey + "r", CrosshairCustom.r);
        PlayerPrefs.SetFloat(CrosshairKey + "g", CrosshairCustom.g);
        PlayerPrefs.SetFloat(CrosshairKey + "b", CrosshairCustom.b);
        PlayerPrefs.SetFloat(CrosshairKey + "length", CrosshairLength);
        PlayerPrefs.SetFloat(CrosshairKey + "thickness", CrosshairThickness);
        PlayerPrefs.SetFloat(CrosshairKey + "gap", CrosshairGap);
        PlayerPrefs.SetFloat(CrosshairKey + "alpha", CrosshairAlpha);
        PlayerPrefs.SetInt(CrosshairKey + "outline", CrosshairOutline ? 1 : 0);
        PlayerPrefs.SetInt(CrosshairKey + "dot", CrosshairDot ? 1 : 0);
        PlayerPrefs.SetInt(CrosshairKey + "t", CrosshairT ? 1 : 0);
        PlayerPrefs.SetInt(CrosshairKey + "dynamic", CrosshairDynamic ? 1 : 0);
    }

    /// <summary>Changes the crosshair (any value left null stays as it is) and saves it.</summary>
    public static void SetCrosshair(int? color = null, Color? custom = null, float? length = null, float? thickness = null,
                                    float? gap = null, float? alpha = null, bool? outline = null, bool? dot = null,
                                    bool? tShape = null, bool? dynamic = null)
    {
        if (color.HasValue) CrosshairColorIndex = Mathf.Clamp(color.Value, 0, CrosshairColorNames.Length - 1);
        if (custom.HasValue) CrosshairCustom = custom.Value;
        if (length.HasValue) CrosshairLength = Mathf.Clamp(length.Value, 0f, 30f);
        if (thickness.HasValue) CrosshairThickness = Mathf.Clamp(thickness.Value, 1f, 8f);
        if (gap.HasValue) CrosshairGap = Mathf.Clamp(gap.Value, -5f, 20f);
        if (alpha.HasValue) CrosshairAlpha = Mathf.Clamp(alpha.Value, 0.2f, 1f);
        if (outline.HasValue) CrosshairOutline = outline.Value;
        if (dot.HasValue) CrosshairDot = dot.Value;
        if (tShape.HasValue) CrosshairT = tShape.Value;
        if (dynamic.HasValue) CrosshairDynamic = dynamic.Value;
        SaveCrosshair();
    }

    /// <summary>-ds-crosshair-demo: a cyan T with a dot and an outline, static, for this run only (screenshots).</summary>
    public static void CrosshairDemoThisRun()
    {
        CrosshairColorIndex = 4;
        CrosshairLength = 7f;
        CrosshairThickness = 2f;
        CrosshairGap = 2f;
        CrosshairAlpha = 1f;
        CrosshairOutline = CrosshairDot = CrosshairT = true;
        CrosshairDynamic = false;
    }

    public static void ResetCrosshair()
    {
        foreach (string key in new[] { "color", "r", "g", "b", "length", "thickness", "gap", "alpha", "outline", "dot", "t", "dynamic" })
            PlayerPrefs.DeleteKey(CrosshairKey + key);
        LoadCrosshair();
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
