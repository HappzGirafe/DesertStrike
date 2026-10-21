using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Editor helpers: creates the game scene and builds the Windows, macOS, Android and iOS versions of the game.</summary>
[InitializeOnLoad]
public static class DesertStrikeSetup
{
    public const string ScenePath = "Assets/Scenes/DesertStrike.unity";

    /// <summary>The game's name: window title, Mac app name, where its settings are saved.</summary>
    public const string GameName = "Low Strike";

    static DesertStrikeSetup()
    {
        // Open the game scene instead of an empty untitled scene when the project is opened.
        EditorApplication.delayCall += () =>
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var active = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(active.path) && !active.isDirty && File.Exists(ScenePath))
                EditorSceneManager.OpenScene(ScenePath);
        };
    }

    [MenuItem("Low Strike/Recreate Game Scene")]
    public static void CreateScene()
    {
        EnsureMaterials();
        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        var camera = GameObject.Find("Main Camera");
        if (camera != null) Object.DestroyImmediate(camera);
        new GameObject("Game").AddComponent<GameManager>();

        // Fog is set at runtime too, but it must be on in the saved scene or builds strip the fog shaders.
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.84f, 0.77f, 0.64f);
        RenderSettings.fogStartDistance = 60f;
        RenderSettings.fogEndDistance = 260f;

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        PlayerSettings.productName = GameName;
        AssetDatabase.SaveAssets();
        Debug.Log("[DesertStrike] Scene created at " + ScenePath);
    }

    [MenuItem("Low Strike/Build Windows Game")]
    public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/LowStrike.exe");

    /// <summary>Linux (x86-64) for players: Builds/Linux/LowStrike.x86_64 next to LowStrike_Data (needs Unity's
    /// "Linux Build Support (Mono)" module). Players make it runnable once: chmod +x LowStrike.x86_64.</summary>
    [MenuItem("Low Strike/Build Linux Game")]
    public static void BuildLinux() => Build(BuildTarget.StandaloneLinux64, "Builds/Linux/LowStrike.x86_64");

    /// <summary>The online rooms' game host for a Linux server: a dedicated-server build (no window, graphics or sound;
    /// needs "Linux Dedicated Server Build Support"). It hosts rooms like the Windows game started with -ds-server.</summary>
    [MenuItem("Low Strike/Build Linux Server")]
    public static void BuildLinuxServer() =>
        Build(BuildTarget.StandaloneLinux64, "Builds/LinuxServer/LowStrikeServer.x86_64", BuildOptions.None, StandaloneBuildSubtarget.Server);

    /// <summary>A development build for measuring where frame time goes (see ProfileReport).</summary>
    public static void BuildWindowsProfiling() =>
        Build(BuildTarget.StandaloneWindows64, "Builds/WindowsProfiling/LowStrike.exe", BuildOptions.Development);

    /// <summary>
    /// Builds a macOS app (Intel and Apple Silicon) — needs Unity's "Mac Build Support" module. A Mac build made
    /// on Windows is not code-signed; see README for the one Terminal command Mac players run once.
    /// </summary>
    [MenuItem("Low Strike/Build macOS Game")]
    public static void BuildMac()
    {
        SetMacArchitecture("x64ARM64");
        // Retina would draw 4 times the pixels (2560x1600 on a MacBook Air), which is what heats it up most.
        PlayerSettings.macRetinaSupport = false;
        // Voice chat: macOS asks the player once, the first time they hold V in a LAN game, and shows this text.
        // (Unity keeps one setting for this on Apple platforms; it becomes NSMicrophoneUsageDescription.)
        PlayerSettings.iOS.microphoneUsageDescription = "Low Strike uses the microphone for voice chat in LAN and online games while you hold V.";
        Build(BuildTarget.StandaloneOSX, "Builds/macOS/Low Strike.app");
    }

    /// <summary>
    /// Android phones and tablets: an .apk to copy to the phone and install (needs Unity's "Android Build Support"
    /// module). 64-bit and 32-bit ARM; signed with Unity's debug key, which is fine for installing it yourself.
    /// </summary>
    [MenuItem("Low Strike/Build Android Game")]
    public static void BuildAndroid()
    {
        SetMobileSettings();
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, BundleId);
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
        // -ds-arm64-only: a smaller test build for 64-bit phones (nearly all of them); releases carry both.
        bool arm64Only = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-ds-arm64-only") >= 0;
        PlayerSettings.Android.targetArchitectures = arm64Only ? AndroidArchitecture.ARM64 : AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7;
        PlayerSettings.Android.bundleVersionCode = MobileVersionCode;
        PlayerSettings.Android.forceInternetPermission = true;   // LAN games and friends
        EditorUserBuildSettings.buildAppBundle = false;           // an .apk, not a Play Store bundle
        // Pictures compressed as ASTC (every phone since about 2015). Set every time: after building for other
        // platforms this saved choice was once found on PVRTC, which Unity 6 no longer supports, so every picture
        // went into the .apk uncompressed (134 MB instead of 48).
        EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
        PlayerSettings.Android.textureCompressionFormats = new[] { TextureCompressionFormat.ASTC };
        var probe = AssetImporter.GetAtPath("Assets/Resources/Skins/AK-47/Pink Scribble/grip.png") as TextureImporter;
        Debug.Log($"[DesertStrike] Android pictures: {string.Join(", ", PlayerSettings.Android.textureCompressionFormats)}, " +
                  $"subtarget {EditorUserBuildSettings.androidBuildSubtarget}, a skin picture as {probe?.GetAutomaticFormat("Android")}");
        Build(BuildTarget.Android, "Builds/Android/LowStrike-Android.apk");
    }

    /// <summary>
    /// iPhone and iPad: an Xcode project (needs Unity's "iOS Build Support" module). Apple only lets Xcode on a Mac
    /// build, sign and install the app itself (see README).
    /// </summary>
    [MenuItem("Low Strike/Build iOS Xcode Project")]
    public static void BuildIOS()
    {
        SetMobileSettings();
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);
        PlayerSettings.iOS.buildNumber = MobileVersionCode.ToString();
        PlayerSettings.iOS.targetOSVersionString = "15.0";
        PlayerSettings.iOS.microphoneUsageDescription = "Low Strike uses the microphone for voice chat in LAN and online games while you hold MIC.";
        Build(BuildTarget.iOS, "Builds/iOS");
    }

    const string BundleId = "com.happzgirafe.lowstrike";
    // The game's version (Application.version): shown by the systems, and online rooms need the one the server has.
    const string GameVersion = "1.20";
    const int MobileVersionCode = 120;

    // Phones: always landscape (either way round).
    static void SetMobileSettings()
    {
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
    }

    static void Build(BuildTarget target, string path, BuildOptions options = BuildOptions.None,
                      StandaloneBuildSubtarget subtarget = StandaloneBuildSubtarget.Player)
    {
        PlayerSettings.productName = GameName;
        PlayerSettings.bundleVersion = GameVersion;
        // Online game hosts (-ds-server) report to the online API on the same PC over plain HTTP (127.0.0.1). Players'
        // games use HTTPS only: OnlineClient refuses other addresses except this computer's (tests).
        PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
        // CPU and GPU frame times for the FPS counter (FrameTimingManager).
        PlayerSettings.enableFrameTimingStats = true;
        // Settings > Display > Window: the window can be resized by dragging its edges.
        PlayerSettings.resizableWindow = true;
        // Build Settings' "Asset Import Overrides" are for quick test builds: a release never forces pictures
        // uncompressed or smaller. (Found forced uncompressed once after building for Linux: a 128 MB .apk.)
        if (EditorUserBuildSettings.overrideTextureCompression != OverrideTextureCompression.NoOverride || EditorUserBuildSettings.overrideMaxTextureSize != 0)
            Debug.Log($"[DesertStrike] Import overrides were {EditorUserBuildSettings.overrideTextureCompression}, max size {EditorUserBuildSettings.overrideMaxTextureSize}: cleared");
        EditorUserBuildSettings.overrideTextureCompression = OverrideTextureCompression.NoOverride;
        EditorUserBuildSettings.overrideMaxTextureSize = 0;
        if (!File.Exists(ScenePath)) CreateScene();
        EnsureMaterials();
        SkinIndexBuilder.Build();
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = path,
            target = target,
            subtarget = (int)subtarget,
            options = options,
        });
        Debug.Log("[DesertStrike] Build " + report.summary.result + ": " + report.summary.outputPath);
        if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }

    // The setting lives in the Mac Build Support module's assembly, so it is set by name.
    static void SetMacArchitecture(string architecture)
    {
        foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            var property = assembly.GetType("UnityEditor.OSXStandalone.UserBuildSettings")
                ?.GetProperty("architecture", BindingFlags.Public | BindingFlags.Static);
            if (property == null) continue;
            property.SetValue(null, System.Enum.Parse(property.PropertyType, architecture));
            Debug.Log("[DesertStrike] macOS architecture: " + architecture);
            return;
        }
        Debug.LogWarning("[DesertStrike] Could not set the macOS architecture; Unity's default is used.");
    }

    /// <summary>
    /// Saves the Standard-shader materials that Effects copies at runtime. Without saved materials
    /// a build strips the shader and everything renders magenta.
    /// </summary>
    static void EnsureMaterials()
    {
        const string folder = "Assets/Resources/DesertStrike";
        Directory.CreateDirectory(folder);
        var shader = Shader.Find("Standard");

        if (AssetDatabase.LoadAssetAtPath<Material>(folder + "/Base.mat") == null)
            AssetDatabase.CreateAsset(new Material(shader), folder + "/Base.mat");

        if (AssetDatabase.LoadAssetAtPath<Material>(folder + "/Glow.mat") == null)
        {
            var glow = new Material(shader);
            glow.EnableKeyword("_EMISSION");
            glow.SetColor("_EmissionColor", Color.white);
            glow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            AssetDatabase.CreateAsset(glow, folder + "/Glow.mat");
        }

        // Cut-out (webs, the crane's lattice), cut-out that glows (pumpkin faces) and see-through (painted letters):
        // the Standard shader's own settings for those rendering modes.
        EnsureTransparentMaterial(shader, folder + "/Cutout.mat", fade: false, glow: false);
        EnsureTransparentMaterial(shader, folder + "/CutoutGlow.mat", fade: false, glow: true);
        EnsureTransparentMaterial(shader, folder + "/Fade.mat", fade: true, glow: false);
        AssetDatabase.SaveAssets();
    }

    static void EnsureTransparentMaterial(Shader shader, string path, bool fade, bool glow)
    {
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return;
        var material = new Material(shader);
        material.SetFloat("_Mode", fade ? 2f : 1f);
        material.SetOverrideTag("RenderType", fade ? "Transparent" : "TransparentCutout");
        material.SetInt("_SrcBlend", (int)(fade ? UnityEngine.Rendering.BlendMode.SrcAlpha : UnityEngine.Rendering.BlendMode.One));
        material.SetInt("_DstBlend", (int)(fade ? UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha : UnityEngine.Rendering.BlendMode.Zero));
        material.SetInt("_ZWrite", fade ? 0 : 1);
        if (fade) material.EnableKeyword("_ALPHABLEND_ON");
        else material.EnableKeyword("_ALPHATEST_ON");
        material.renderQueue = (int)(fade ? UnityEngine.Rendering.RenderQueue.Transparent : UnityEngine.Rendering.RenderQueue.AlphaTest);
        if (glow)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", Color.white);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        AssetDatabase.CreateAsset(material, path);
    }
}
