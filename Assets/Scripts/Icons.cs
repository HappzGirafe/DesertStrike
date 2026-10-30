using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Small white pictures for the touch buttons and the HUD (a bullet for FIRE, an arrow for JUMP, a cross for health...),
/// painted at start from circles, bars and corners like the rest of the interface (no picture files). Each is 128 x 128,
/// smoothed at the edges, and drawn in any colour with <see cref="Draw"/>.
/// </summary>
public static class Icons
{
    const int Size = 128;
    static readonly Dictionary<string, Texture2D> made = new Dictionary<string, Texture2D>();

    public static void Draw(Rect rect, string name, Color color)
    {
        var texture = Get(name);
        if (texture == null || Event.current.type != EventType.Repaint) return;
        GUI.color = color;
        GUI.DrawTexture(rect, texture);
        GUI.color = Color.white;
    }

    public static Texture2D Get(string name)
    {
        if (made.TryGetValue(name, out var texture)) return texture;
        var shapes = new Painter();
        if (!Define(name, shapes)) return made[name] = null;
        return made[name] = shapes.Paint();
    }

    // Every picture, in a square from (0, 0) at the top left to (1, 1).
    static bool Define(string name, Painter p)
    {
        switch (name)
        {
            case "fire":
            case "ammo":
                p.Bullet(0.5f, 0.5f, 1f);
                return true;
            case "auto":
                p.Bullet(0.24f, 0.52f, 0.62f);
                p.Bullet(0.5f, 0.52f, 0.62f);
                p.Bullet(0.76f, 0.52f, 0.62f);
                return true;
            case "jump":
                p.Poly(new Vector2(0.5f, 0.13f), new Vector2(0.82f, 0.48f), new Vector2(0.18f, 0.48f));
                p.Box(0.39f, 0.46f, 0.61f, 0.85f, 0.03f);
                return true;
            case "crouch":
                p.Line(0.22f, 0.24f, 0.5f, 0.48f, 0.1f);
                p.Line(0.5f, 0.48f, 0.78f, 0.24f, 0.1f);
                p.Line(0.22f, 0.52f, 0.5f, 0.76f, 0.1f);
                p.Line(0.5f, 0.76f, 0.78f, 0.52f, 0.1f);
                return true;
            case "reload":
                p.Arc(0.5f, 0.53f, 0.28f, 0.09f, 25f, 295f);
                p.ArrowHead(0.5f, 0.53f, 0.28f, 295f, 0.15f);
                return true;
            case "gun":
                p.Box(0.14f, 0.3f, 0.86f, 0.46f, 0.03f);
                p.Poly(new Vector2(0.56f, 0.42f), new Vector2(0.76f, 0.42f), new Vector2(0.72f, 0.86f), new Vector2(0.52f, 0.86f));
                p.Arc(0.47f, 0.5f, 0.09f, 0.045f, 0f, 180f);
                return true;
            case "scope":
                p.Ring(0.5f, 0.5f, 0.31f, 0.065f);
                p.Line(0.5f, 0.08f, 0.5f, 0.37f, 0.055f);
                p.Line(0.5f, 0.63f, 0.5f, 0.92f, 0.055f);
                p.Line(0.08f, 0.5f, 0.37f, 0.5f, 0.055f);
                p.Line(0.63f, 0.5f, 0.92f, 0.5f, 0.055f);
                p.Circle(0.5f, 0.5f, 0.04f);
                return true;
            case "plant":
                p.Box(0.18f, 0.38f, 0.82f, 0.84f, 0.07f);
                p.Cut(q => Box(q, 0.28f, 0.48f, 0.62f, 0.62f, 0.02f));
                p.Cut(q => Circle(q, 0.71f, 0.55f, 0.055f));
                p.Arc(0.5f, 0.38f, 0.17f, 0.055f, 180f, 360f);
                return true;
            case "defuse":
                p.Line(0.3f, 0.16f, 0.6f, 0.6f, 0.09f);
                p.Line(0.7f, 0.16f, 0.4f, 0.6f, 0.09f);
                p.Ring(0.35f, 0.74f, 0.11f, 0.06f);
                p.Ring(0.65f, 0.74f, 0.11f, 0.06f);
                return true;
            case "take":
                p.Tray();
                p.Poly(new Vector2(0.5f, 0.1f), new Vector2(0.76f, 0.38f), new Vector2(0.24f, 0.38f));
                p.Box(0.42f, 0.36f, 0.58f, 0.7f, 0.02f);
                return true;
            case "drop":
                p.Tray();
                p.Box(0.42f, 0.1f, 0.58f, 0.44f, 0.02f);
                p.Poly(new Vector2(0.24f, 0.42f), new Vector2(0.76f, 0.42f), new Vector2(0.5f, 0.72f));
                return true;
            case "buy":
                p.Line(0.07f, 0.2f, 0.2f, 0.2f, 0.07f);
                p.Line(0.2f, 0.2f, 0.32f, 0.63f, 0.07f);
                p.Poly(new Vector2(0.24f, 0.29f), new Vector2(0.92f, 0.29f), new Vector2(0.82f, 0.63f), new Vector2(0.32f, 0.63f));
                p.Circle(0.38f, 0.79f, 0.075f);
                p.Circle(0.75f, 0.79f, 0.075f);
                return true;
            case "close":
                p.Line(0.24f, 0.24f, 0.76f, 0.76f, 0.12f);
                p.Line(0.76f, 0.24f, 0.24f, 0.76f, 0.12f);
                return true;
            case "mic":
                p.Box(0.37f, 0.1f, 0.63f, 0.56f, 0.13f);
                p.Arc(0.5f, 0.42f, 0.22f, 0.065f, 0f, 180f);
                p.Line(0.5f, 0.66f, 0.5f, 0.82f, 0.065f);
                p.Line(0.33f, 0.85f, 0.67f, 0.85f, 0.065f);
                return true;
            case "scores":
                foreach (float y in new[] { 0.28f, 0.5f, 0.72f })
                {
                    p.Circle(0.22f, y, 0.065f);
                    p.Line(0.38f, y, 0.82f, y, 0.085f);
                }
                return true;
            case "pause":
                p.Box(0.27f, 0.2f, 0.43f, 0.8f, 0.04f);
                p.Box(0.57f, 0.2f, 0.73f, 0.8f, 0.04f);
                return true;
            case "prev":
                p.Line(0.62f, 0.18f, 0.34f, 0.5f, 0.12f);
                p.Line(0.34f, 0.5f, 0.62f, 0.82f, 0.12f);
                return true;
            case "next":
                p.Line(0.38f, 0.18f, 0.66f, 0.5f, 0.12f);
                p.Line(0.66f, 0.5f, 0.38f, 0.82f, 0.12f);
                return true;
            case "stick":
                p.Poly(new Vector2(0.5f, 0.08f), new Vector2(0.64f, 0.26f), new Vector2(0.36f, 0.26f));
                p.Poly(new Vector2(0.5f, 0.92f), new Vector2(0.36f, 0.74f), new Vector2(0.64f, 0.74f));
                p.Poly(new Vector2(0.08f, 0.5f), new Vector2(0.26f, 0.36f), new Vector2(0.26f, 0.64f));
                p.Poly(new Vector2(0.92f, 0.5f), new Vector2(0.74f, 0.64f), new Vector2(0.74f, 0.36f));
                p.Circle(0.5f, 0.5f, 0.13f);
                return true;
            case "health":
                p.Box(0.37f, 0.14f, 0.63f, 0.86f, 0.06f);
                p.Box(0.14f, 0.37f, 0.86f, 0.63f, 0.06f);
                return true;
            case "armor":
                p.Poly(new Vector2(0.17f, 0.2f), new Vector2(0.5f, 0.1f), new Vector2(0.83f, 0.2f), new Vector2(0.83f, 0.5f),
                       new Vector2(0.5f, 0.9f), new Vector2(0.17f, 0.5f));
                return true;
            case "helmet":
                p.Add(q => Mathf.Max(Circle(q, 0.5f, 0.62f, 0.34f), q.y - 0.62f));
                p.Box(0.1f, 0.58f, 0.9f, 0.7f, 0.05f);
                return true;
            case "ring":
                p.Ring(0.5f, 0.5f, 0.475f, 0.035f);
                return true;
        }
        return false;
    }

    // ----------------------------------------------------------------- shapes, as distances (negative inside)

    static float Circle(Vector2 q, float x, float y, float r) => Vector2.Distance(q, new Vector2(x, y)) - r;

    static float Box(Vector2 q, float x0, float y0, float x1, float y1, float round)
    {
        Vector2 center = new Vector2((x0 + x1) / 2f, (y0 + y1) / 2f), half = new Vector2((x1 - x0) / 2f - round, (y1 - y0) / 2f - round);
        Vector2 d = new Vector2(Mathf.Abs(q.x - center.x) - half.x, Mathf.Abs(q.y - center.y) - half.y);
        return new Vector2(Mathf.Max(d.x, 0f), Mathf.Max(d.y, 0f)).magnitude + Mathf.Min(Mathf.Max(d.x, d.y), 0f) - round;
    }

    static float Segment(Vector2 q, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(q - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
        return Vector2.Distance(q, a + ab * t);
    }

    class Painter
    {
        readonly List<Func<Vector2, float>> adds = new List<Func<Vector2, float>>();
        readonly List<Func<Vector2, float>> cuts = new List<Func<Vector2, float>>();

        public void Add(Func<Vector2, float> shape) => adds.Add(shape);
        public void Cut(Func<Vector2, float> shape) => cuts.Add(shape);
        public void Circle(float x, float y, float r) => Add(q => Icons.Circle(q, x, y, r));
        public void Ring(float x, float y, float r, float width) => Add(q => Mathf.Abs(Icons.Circle(q, x, y, r)) - width / 2f);
        public void Box(float x0, float y0, float x1, float y1, float round) => Add(q => Icons.Box(q, x0, y0, x1, y1, round));
        public void Line(float x0, float y0, float x1, float y1, float width) =>
            Add(q => Segment(q, new Vector2(x0, y0), new Vector2(x1, y1)) - width / 2f);

        /// <summary>A filled convex shape through these corners (either way round).</summary>
        public void Poly(params Vector2[] corners)
        {
            float area = 0f;
            for (int i = 0; i < corners.Length; i++)
            {
                var a = corners[i];
                var b = corners[(i + 1) % corners.Length];
                area += a.x * b.y - b.x * a.y;
            }
            float turn = area > 0f ? 1f : -1f;
            Add(q =>
            {
                float d = float.MinValue;
                for (int i = 0; i < corners.Length; i++)
                {
                    var a = corners[i];
                    var b = corners[(i + 1) % corners.Length];
                    Vector2 edge = (b - a).normalized, outward = new Vector2(edge.y, -edge.x) * turn;
                    d = Mathf.Max(d, Vector2.Dot(q - a, outward));
                }
                return d;
            });
        }

        /// <summary>A bow of a circle from one angle to another (degrees; 0 to the right, 90 down), with round ends.</summary>
        public void Arc(float x, float y, float r, float width, float from, float to)
        {
            Vector2 c = new Vector2(x, y);
            Vector2 At(float degrees) => c + new Vector2(Mathf.Cos(degrees * Mathf.Deg2Rad), Mathf.Sin(degrees * Mathf.Deg2Rad)) * r;
            Vector2 start = At(from), end = At(to);
            Add(q =>
            {
                float angle = Mathf.Atan2(q.y - y, q.x - x) * Mathf.Rad2Deg;
                if (angle < from) angle += 360f;
                if (angle >= from && angle <= to) return Mathf.Abs(Vector2.Distance(q, c) - r) - width / 2f;
                return Mathf.Min(Vector2.Distance(q, start), Vector2.Distance(q, end)) - width / 2f;
            });
        }

        /// <summary>An arrow's head at the end of an arc (<see cref="Arc"/>), pointing on round it.</summary>
        public void ArrowHead(float x, float y, float r, float degrees, float size)
        {
            float a = degrees * Mathf.Deg2Rad;
            Vector2 point = new Vector2(x, y) + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            Vector2 along = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)), out_ = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            Poly(point + along * size, point - along * size * 0.15f + out_ * size * 0.75f, point - along * size * 0.15f - out_ * size * 0.75f);
        }

        /// <summary>A bullet standing up, its middle at (x, y).</summary>
        public void Bullet(float x, float y, float scale)
        {
            float w = 0.11f * scale, top = y - 0.33f * scale, body = y - 0.08f * scale, bottom = y + 0.34f * scale;
            Box(x - w, body, x + w, bottom - 0.04f * scale, 0.02f * scale);
            Poly(new Vector2(x - w, body + 0.01f), new Vector2(x + w, body + 0.01f), new Vector2(x + w * 0.65f, y - 0.24f * scale),
                 new Vector2(x, top), new Vector2(x - w * 0.65f, y - 0.24f * scale));
            Box(x - w * 1.25f, bottom - 0.07f * scale, x + w * 1.25f, bottom, 0.015f * scale);
            Cut(q => Icons.Box(q, x - w - 0.01f, bottom - 0.115f * scale, x + w + 0.01f, bottom - 0.095f * scale, 0f));
        }

        /// <summary>An open box seen from the side (TAKE and DROP).</summary>
        public void Tray()
        {
            Line(0.16f, 0.6f, 0.16f, 0.86f, 0.07f);
            Line(0.16f, 0.86f, 0.84f, 0.86f, 0.07f);
            Line(0.84f, 0.86f, 0.84f, 0.6f, 0.07f);
        }

        public Texture2D Paint()
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    var q = new Vector2((x + 0.5f) / Size, (y + 0.5f) / Size);
                    float d = float.MaxValue;
                    foreach (var shape in adds) d = Mathf.Min(d, shape(q));
                    foreach (var cut in cuts) d = Mathf.Max(d, -cut(q));
                    float alpha = Mathf.Clamp01(0.5f - d * Size);
                    pixels[(Size - 1 - y) * Size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));   // textures go bottom up
                }
            texture.SetPixels32(pixels);
            texture.Apply(true);
            return texture;
        }
    }
}
