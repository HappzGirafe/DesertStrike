using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Editor helpers: creates the game scene and builds a Windows version of the game.</summary>
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
    public static void BuildWindows()
    {
        if (!File.Exists(ScenePath)) CreateScene();
        EnsureMaterials();
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = "Builds/Windows/DesertStrike.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        });
        Debug.Log("[DesertStrike] Build " + report.summary.result + ": " + report.summary.outputPath);
        if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded) EditorApplication.Exit(1);
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
