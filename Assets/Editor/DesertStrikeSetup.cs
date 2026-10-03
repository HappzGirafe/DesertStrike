using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Editor helpers: creates the game scene and builds the Windows and macOS versions of the game.</summary>
[InitializeOnLoad]
public static class DesertStrikeSetup
{
    public const string ScenePath = "Assets/Scenes/DesertStrike.unity";

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

    [MenuItem("Desert Strike/Recreate Game Scene")]
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
        PlayerSettings.productName = "Desert Strike";
        AssetDatabase.SaveAssets();
        Debug.Log("[DesertStrike] Scene created at " + ScenePath);
    }

    [MenuItem("Desert Strike/Build Windows Game")]
    public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Builds/Windows/DesertStrike.exe");

    /// <summary>A development build for measuring where frame time goes (see ProfileReport).</summary>
    public static void BuildWindowsProfiling() =>
        Build(BuildTarget.StandaloneWindows64, "Builds/WindowsProfiling/DesertStrike.exe", BuildOptions.Development);

    /// <summary>
    /// Builds a macOS app (Intel and Apple Silicon) — needs Unity's "Mac Build Support" module. A Mac build made
    /// on Windows is not code-signed; see README for the one Terminal command Mac players run once.
    /// </summary>
    [MenuItem("Desert Strike/Build macOS Game")]
    public static void BuildMac()
    {
        SetMacArchitecture("x64ARM64");
        // Retina would draw 4 times the pixels (2560x1600 on a MacBook Air), which is what heats it up most.
        PlayerSettings.macRetinaSupport = false;
        Build(BuildTarget.StandaloneOSX, "Builds/macOS/DesertStrike.app");
    }

    static void Build(BuildTarget target, string path, BuildOptions options = BuildOptions.None)
    {
        // CPU and GPU frame times for the FPS counter (FrameTimingManager).
        PlayerSettings.enableFrameTimingStats = true;
        if (!File.Exists(ScenePath)) CreateScene();
        EnsureMaterials();
        SkinIndexBuilder.Build();
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = path,
            target = target,
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
        AssetDatabase.SaveAssets();
    }
}
