using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The main camera's finishing pass (Resources/Shaders/Post): bloom, colour grading that keeps the maps' cartoon
/// colours vivid (exposure, a gentle filmic shoulder, an S-curve, saturation and vibrance, warm highlights and cool
/// shadows), a soft vignette and FXAA, in each map's own look (<see cref="MapLighting"/>). It follows the graphics
/// quality (<see cref="FollowQuality"/>): off on Low, so the camera draws straight to its target with no extra buffer;
/// Medium adds a small bloom, then grades and smooths the edges (FXAA) in one full-size pass; High a wider bloom
/// (MSAA smooths the edges there). With a lower 3D resolution (<see cref="RenderScaler"/>) it draws into that picture.
/// Off on servers without graphics, and where the graphics chip cannot run the shader.
/// </summary>
[RequireComponent(typeof(Camera))]
public class PostEffects : MonoBehaviour
{
    // The shader's passes.
    const int PrefilterPass = 0, DownPass = 1, UpPass = 2, GradePass = 3, GradeBloomPass = 4, FxaaGradePass = 5, FxaaGradeBloomPass = 6;
    const int MaxSizes = 5;
    const float SoftKnee = 0.4f;   // below the bloom threshold, this share of it still glows a little (no hard edge)
    const float Scatter = 0.7f;    // how much each smaller (wider) size of the bloom counts against the one above it

    static readonly int BloomTexId = Shader.PropertyToID("_BloomTex");
    static readonly int ThresholdId = Shader.PropertyToID("_Threshold");
    static readonly int GradeId = Shader.PropertyToID("_Grade");
    static readonly int ShoulderId = Shader.PropertyToID("_Shoulder");
    static readonly int HighlightTintId = Shader.PropertyToID("_HighlightTint");
    static readonly int ShadowTintId = Shader.PropertyToID("_ShadowTint");
    static readonly int BloomStrengthId = Shader.PropertyToID("_BloomStrength");
    static readonly int ScatterId = Shader.PropertyToID("_Scatter");
    static readonly int VignetteId = Shader.PropertyToID("_Vignette");

    // The ones that can run (the main camera's), for FollowQuality.
    static readonly List<PostEffects> all = new List<PostEffects>();

    Camera cam;
    Material material;
    RenderTextureFormat bloomFormat;
    MapLighting look;
    bool hasLook;
    float bloomStrength;
    // The bloom's sizes on the way down (half, quarter...) and back up: taken and given back every frame.
    readonly RenderTexture[] down = new RenderTexture[MaxSizes];
    readonly RenderTexture[] up = new RenderTexture[MaxSizes];

    /// <summary>Whether this graphics chip runs the shader (never on servers without graphics).</summary>
    public bool Supported => material != null;

    /// <summary>
    /// Turns the post effects on or off for the graphics quality (GameSettings calls it whenever that changes): off
    /// on Low. Only being off saves the extra buffer, since its OnRenderImage alone makes the camera draw into one.
    /// </summary>
    public static void FollowQuality()
    {
        foreach (var post in all) post.Follow();
    }

    void Follow() => enabled = Supported && GameSettings.Quality != GraphicsQuality.Low;

    void Awake()
    {
        cam = GetComponent<Camera>();
        bool headless = Application.isBatchMode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
        var shader = headless ? null : Resources.Load<Shader>("Shaders/Post");
        if (shader == null || !shader.isSupported)
        {
            // The online game servers draw nothing; a chip that cannot run it keeps the plain picture. Turned off,
            // the camera draws straight to its target as without this component.
            enabled = false;
            return;
        }
        material = new Material(shader) { name = "Post Effects", hideFlags = HideFlags.DontSave };
        // The bloom in a float format where the chip can draw into one (no banding in dark glows), else 8 bits.
        bloomFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB111110Float) ? RenderTextureFormat.RGB111110Float
            : SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf) ? RenderTextureFormat.ARGBHalf
            : RenderTextureFormat.Default;
        material.SetFloat(ScatterId, Scatter);
        if (hasLook) ApplyLook();
        all.Add(this);
        Follow();
    }

    void OnDestroy()
    {
        all.Remove(this);
        ReleaseBloom();
        if (material != null) Destroy(material);
    }

    /// <summary>Uses a map's look (bloom, grading, vignette); set with each map's lighting.</summary>
    public void SetLook(MapLighting lighting)
    {
        look = lighting;
        hasLook = true;
        if (material != null) ApplyLook();
    }

    void ApplyLook()
    {
        // A map without a look of its own keeps its colours as they are (FXAA still smooths the edges).
        bool own = look.Exposure > 0f;
        float threshold = Mathf.Clamp(own ? look.BloomThreshold : 1f, 0.05f, 1f);
        float knee = threshold * SoftKnee + 0.00001f;
        material.SetVector(ThresholdId, new Vector4(threshold, threshold - knee, knee * 2f, 0.25f / knee));
        material.SetVector(GradeId, own ? new Vector4(look.Exposure, Mathf.Clamp(look.Contrast - 1f, -1f, 1f), look.Saturation, look.Vibrance)
                                        : new Vector4(1f, 0f, 1f, 0f));
        // The filmic shoulder only as far as exposure and bloom push light past white; with neither, none at all.
        material.SetFloat(ShoulderId, own ? Mathf.Clamp01((look.Exposure - 1f) * 10f + look.Bloom) : 0f);
        material.SetVector(HighlightTintId, own ? Toning(look.HighlightTint) : Vector4.zero);
        material.SetVector(ShadowTintId, own ? Toning(look.ShadowTint) : Vector4.zero);
        material.SetFloat(VignetteId, own ? look.Vignette : 0f);
        bloomStrength = own ? look.Bloom : 0f;
        material.SetFloat(BloomStrengthId, bloomStrength);
    }

    // A split-toning tint as a shift of colour with no brightness of its own, times its alpha (how much).
    static Vector4 Toning(Color tint)
    {
        float luma = tint.r * 0.299f + tint.g * 0.587f + tint.b * 0.114f;
        return new Vector4((tint.r - luma) * tint.a, (tint.g - luma) * tint.a, (tint.b - luma) * tint.a, 0f);
    }

    void OnRenderImage(RenderTexture source, RenderTexture destination)
    {
        if (material == null)
        {
            Graphics.Blit(source, destination);
            return;
        }
        // The bloom's first size and FXAA read between the pixels, so the picture must be filtered.
        source.filterMode = FilterMode.Bilinear;
        // Only Medium and High get here (off on Low): High's bloom reaches wider.
        int sizes = GameSettings.Quality == GraphicsQuality.High ? MaxSizes : 3;
        bool bloom = bloomStrength > 0f && Bloom(source, sizes);
        // FXAA in the same pass as the grading, where MSAA does not smooth the edges already: no extra full-size pass.
        bool fxaa = !(cam.allowMSAA && QualitySettings.antiAliasing > 1);
        int pass = fxaa ? (bloom ? FxaaGradeBloomPass : FxaaGradePass) : (bloom ? GradeBloomPass : GradePass);
        Graphics.Blit(source, destination, material, pass);
        ReleaseBloom();
    }

    /// <summary>
    /// The bright parts at half size, then smaller and smaller (each a blur of the last), then back up, each size
    /// blended with the blurred one below it. The result (half size) is left in _BloomTex. False when the picture
    /// is too small for it.
    /// </summary>
    bool Bloom(RenderTexture source, int sizes)
    {
        int width = source.width / 2, height = source.height / 2, count = 0;
        RenderTexture last = source;
        while (count < sizes && width >= 4 && height >= 4)
        {
            var size = RenderTexture.GetTemporary(width, height, 0, bloomFormat);
            size.filterMode = FilterMode.Bilinear;
            Graphics.Blit(last, size, material, count == 0 ? PrefilterPass : DownPass);
            down[count++] = last = size;
            width /= 2;
            height /= 2;
        }
        if (count == 0) return false;
        for (int i = count - 2; i >= 0; i--)
        {
            var size = RenderTexture.GetTemporary(down[i].width, down[i].height, 0, bloomFormat);
            size.filterMode = FilterMode.Bilinear;
            material.SetTexture(BloomTexId, down[i]);
            Graphics.Blit(last, size, material, UpPass);
            up[i] = last = size;
        }
        material.SetTexture(BloomTexId, last);
        return true;
    }

    void ReleaseBloom()
    {
        for (int i = 0; i < MaxSizes; i++)
        {
            if (down[i] != null) RenderTexture.ReleaseTemporary(down[i]);
            if (up[i] != null) RenderTexture.ReleaseTemporary(up[i]);
            down[i] = up[i] = null;
        }
    }
}
