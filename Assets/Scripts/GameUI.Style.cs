using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The look of the menus: rounded boxes, outlines, soft glows and gradients, all from small textures made at start
/// (no UI assets), and a smooth hover for anything that asks (<see cref="Hover"/>).
/// </summary>
public partial class GameUI
{
    static readonly Dictionary<int, GUIStyle> roundStyles = new Dictionary<int, GUIStyle>();
    static readonly Dictionary<int, GUIStyle> ringStyles = new Dictionary<int, GUIStyle>();
    static readonly Dictionary<int, GUIStyle> glowStyles = new Dictionary<int, GUIStyle>();
    static Texture2D fadeDown, fadeRight;
    readonly Dictionary<int, float> hovers = new Dictionary<int, float>();

    static bool Repainting => Event.current.type == EventType.Repaint;

    /// <summary>A box with round corners (radius in screen units, at most half the box).</summary>
    static void Round(Rect rect, Color color, float radius = 10f)
    {
        if (!Repainting || color.a <= 0f) return;
        int r = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(radius, rect.height / 2f, rect.width / 2f)), 1, 64);
        Draw(Sliced(roundStyles, r, () => RoundTexture(r, 0f, r)), rect, color);
    }

    /// <summary>The edge of a round box only (1.5 units wide).</summary>
    static void Outline(Rect rect, Color color, float radius = 10f)
    {
        if (!Repainting || color.a <= 0f) return;
        int r = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(radius, rect.height / 2f, rect.width / 2f)), 2, 64);
        Draw(Sliced(ringStyles, r, () => RoundTexture(r, 1.5f, r)), rect, color);
    }

    /// <summary>A soft light round a box, <paramref name="spread"/> units out.</summary>
    static void Glow(Rect rect, Color color, float spread = 16f)
    {
        if (!Repainting || color.a <= 0f) return;
        int s = Mathf.Clamp(Mathf.RoundToInt(spread), 4, 48);
        Draw(Sliced(glowStyles, s, () => GlowTexture(s)), new Rect(rect.x - s, rect.y - s, rect.width + 2f * s, rect.height + 2f * s), color);
    }

    /// <summary>A colour fading out downwards (to the right, or upwards).</summary>
    static void Fade(Rect rect, Color color, bool toRight = false, bool up = false)
    {
        if (!Repainting || color.a <= 0f) return;
        if (fadeDown == null)
        {
            fadeDown = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            fadeRight = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int i = 0; i < 64; i++)
            {
                float a = 1f - Mathf.SmoothStep(0f, 1f, i / 63f);
                fadeDown.SetPixel(0, 63 - i, new Color(1f, 1f, 1f, a));   // textures go bottom up
                fadeRight.SetPixel(i, 0, new Color(1f, 1f, 1f, a));
            }
            fadeDown.Apply();
            fadeRight.Apply();
        }
        GUI.color = color;
        if (up) GUI.DrawTextureWithTexCoords(rect, fadeDown, new Rect(0f, 1f, 1f, -1f));
        else GUI.DrawTexture(rect, toRight ? fadeRight : fadeDown);
        GUI.color = Color.white;
    }

    /// <summary>A light sheen on the top half of a box (buttons, cards).</summary>
    static void Sheen(Rect rect, float strength) => Fade(new Rect(rect.x + 2f, rect.y + 1f, rect.width - 4f, rect.height * 0.55f), new Color(1f, 1f, 1f, strength));

    /// <summary>A panel behind a screen or a part of the HUD: dark, round, with a faint edge.</summary>
    static void Panel(Rect rect, float radius = 18f)
    {
        radius = Mathf.Min(radius, rect.height / 4f + 4f);
        Round(rect, new Color(0.035f, 0.04f, 0.05f, 0.8f), radius);
        Fade(new Rect(rect.x + 2f, rect.y + 2f, rect.width - 4f, Mathf.Min(140f, rect.height * 0.5f)), new Color(1f, 1f, 1f, 0.035f));
        Outline(rect, new Color(1f, 1f, 1f, 0.08f), radius);
    }

    /// <summary>
    /// 0 to 1, easing towards 1 while the pointer is over <paramref name="rect"/> (or <paramref name="on"/>), for
    /// smooth hover effects. Keyed by the box, so it needs no id.
    /// </summary>
    float Hover(Rect rect, bool? on = null)
    {
        int key = Mathf.RoundToInt(rect.x) * 73856093 ^ Mathf.RoundToInt(rect.y) * 19349663 ^ Mathf.RoundToInt(rect.width) * 83492791;
        hovers.TryGetValue(key, out float value);
        if (!Repainting) return value;
        bool target = on ?? (rect.Contains(Event.current.mousePosition) && !GameInput.Touch);
        value = Mathf.MoveTowards(value, target ? 1f : 0f, Time.unscaledDeltaTime * 8f);
        if (hovers.Count > 2000) hovers.Clear();
        hovers[key] = value;
        return value;
    }

    static GUIStyle Sliced(Dictionary<int, GUIStyle> cache, int size, System.Func<Texture2D> make)
    {
        if (!cache.TryGetValue(size, out var style) || style.normal.background == null)
        {
            style = new GUIStyle { border = new RectOffset(size, size, size, size) };
            style.normal.background = make();
            cache[size] = style;
        }
        return style;
    }

    static void Draw(GUIStyle style, Rect rect, Color color)
    {
        GUI.color = color;
        GUI.backgroundColor = Color.white;
        style.Draw(rect, GUIContent.none, false, false, false, false);
        GUI.color = Color.white;
    }

    // A white box with round corners (or, with a ring width, only its edge), smoothed over one pixel; 9-sliced.
    static Texture2D RoundTexture(int radius, float ring, int border)
    {
        int size = border * 2 + 2;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var pixels = new Color32[size * size];
        float half = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Distance outside the rounded box (negative inside).
                float qx = Mathf.Abs(x + 0.5f - half) - (half - radius), qy = Mathf.Abs(y + 0.5f - half) - (half - radius);
                float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
                float a = Mathf.Clamp01(0.5f - outside);
                if (ring > 0f) a *= Mathf.Clamp01(outside + ring + 0.5f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }

    // A box of light that fades out over `spread` pixels on every side.
    static Texture2D GlowTexture(int spread)
    {
        int size = spread * 2 + 2;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var pixels = new Color32[size * size];
        float half = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float qx = Mathf.Max(Mathf.Abs(x + 0.5f - half) - 1f, 0f), qy = Mathf.Max(Mathf.Abs(y + 0.5f - half) - 1f, 0f);
                float t = Mathf.Clamp01(1f - new Vector2(qx, qy).magnitude / (spread - 1f));
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(t * t * 255f));
            }
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }
}
