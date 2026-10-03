using UnityEngine;

/// <summary>
/// Draws the 3D view at a fraction of the screen resolution (<see cref="GameSettings.RenderScale"/>) and stretches
/// it over the screen, so the graphics chip colours far fewer pixels while menus and the HUD stay sharp.
/// At 100% the camera draws straight to the screen as usual.
/// </summary>
public class RenderScaler : MonoBehaviour
{
    static RenderTexture target;

    void LateUpdate()
    {
        var cam = GameManager.Instance != null ? GameManager.Instance.MainCamera : null;
        if (cam == null) return;

        float scale = GameSettings.RenderScale;
        if (scale >= 0.99f)
        {
            if (target != null) Release(cam);
            return;
        }

        int width = Mathf.Max(64, Mathf.RoundToInt(Screen.width * scale));
        int height = Mathf.Max(64, Mathf.RoundToInt(Screen.height * scale));
        int samples = Mathf.Max(1, QualitySettings.antiAliasing);
        if (target != null && (target.width != width || target.height != height || target.antiAliasing != samples)) Release(cam);
        if (target == null)
        {
            target = new RenderTexture(width, height, 24)
            {
                name = "Scaled View",
                filterMode = FilterMode.Bilinear,
                antiAliasing = samples,
            };
        }
        cam.targetTexture = target;
    }

    /// <summary>Puts the scaled 3D view on the screen. Called first thing in the HUD's repaint.</summary>
    public static void Present()
    {
        if (target == null) return;
        GUI.matrix = Matrix4x4.identity;
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), target, ScaleMode.StretchToFill, false);
    }

    static void Release(Camera cam)
    {
        cam.targetTexture = null;
        target.Release();
        Destroy(target);
        target = null;
    }
}
