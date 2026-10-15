using System;
using System.Collections.Generic;
using UnityEngine;

public enum TouchButton { Fire, Jump, Crouch, Reload, Switch, Scope, Use, Drop, Buy, Scores, Pause, Talk, Previous, Next }

/// <summary>
/// On-screen controls for phones and tablets: a stick that appears where the left thumb touches (push it a little
/// to walk quietly), dragging anywhere on the right to look around, and buttons: FIRE (drag it to aim while
/// shooting), JUMP, CROUCH (on/off), RELOAD, a weapon switch, SCOPE for the AWP and select-fire pistols, PLANT or
/// DEFUSE and DROP when the bomb matters, BUY in the spawn, MIC in LAN games, the scoreboard and pause; arrows to
/// switch players while spectating. On by itself on phones and tablets; -ds-touch shows it on a PC, where the mouse
/// acts as one finger. Laid out for a 1080-high screen like the rest of the HUD.
/// </summary>
[DefaultExecutionOrder(-100)]   // read the fingers before the player moves
public class TouchControls : MonoBehaviour
{
    public static bool Active { get; private set; }
    public static Vector2 Move { get; private set; }
    public static Vector2 LookPixels { get; private set; }
    public static bool Crouched { get; private set; }

    static readonly HashSet<TouchButton> held = new HashSet<TouchButton>();
    static readonly HashSet<TouchButton> down = new HashSet<TouchButton>();

    public static bool Held(TouchButton button) => held.Contains(button);

    /// <summary>A new life starts standing (CROUCH is an on/off button).</summary>
    public static void StandUp() => Crouched = false;
    public static bool Down(TouchButton button) => down.Contains(button);

    const float RefHeight = 1080f;
    const float StickRadius = 120f;

    enum Role { Ignored, Stick, Look, Button }

    class Finger
    {
        public Role Role;
        public TouchButton Button;
        public Vector2 Last;
    }

    struct Pad
    {
        public TouchButton Button;
        public Vector2 Center;
        public float Radius;
        public string Label;
        public bool Lit;   // drawn highlighted (crouched, a request to act)
    }

    readonly Dictionary<int, Finger> fingers = new Dictionary<int, Finger>();
    readonly List<Pad> pads = new List<Pad>();
    Vector2 stickOrigin, stickNow;
    bool stickHeld;
    float width;
    float safeLeft, safeRight;   // the part of the screen no notch or rounded corner covers (1080p units)
    Texture2D disc;
    GUIStyle labelStyle;

    void Awake()
    {
        Active = Application.isMobilePlatform || Array.IndexOf(Environment.GetCommandLineArgs(), "-ds-touch") >= 0;
        if (Application.isMobilePlatform) Screen.sleepTimeout = SleepTimeout.NeverSleep;   // no dimming mid-round
    }

    // ----------------------------------------------------------------- reading the fingers

    void Update()
    {
        down.Clear();
        held.Clear();
        LookPixels = Vector2.zero;
        Move = Vector2.zero;
        if (!Active) return;

        width = Screen.width / (Screen.height / RefHeight);
        var safe = Screen.safeArea;
        safeLeft = safe.xMin / (Screen.height / RefHeight);
        safeRight = safe.xMax / (Screen.height / RefHeight);
        var gm = GameManager.Instance;
        bool playing = gm != null && !gm.IsPaused
                       && (gm.State == MatchState.Freeze || gm.State == MatchState.Live || gm.State == MatchState.RoundEnd);
        if (!playing)
        {
            fingers.Clear();
            stickHeld = false;
            if (gm == null || gm.State == MatchState.Menu) Crouched = false;
            return;
        }
        Layout(gm);

        foreach (var (id, phase, screen) in Touches())
        {
            var position = new Vector2(screen.x, Screen.height - screen.y) / (Screen.height / RefHeight);
            switch (phase)
            {
                case TouchPhase.Began:
                    fingers[id] = Begin(position, gm);
                    break;
                case TouchPhase.Moved:
                case TouchPhase.Stationary:
                    if (!fingers.TryGetValue(id, out var finger)) break;
                    // Dragging to look: anywhere on the right, and on FIRE (aim while shooting).
                    if (finger.Role == Role.Look || (finger.Role == Role.Button && finger.Button == TouchButton.Fire))
                        LookPixels += new Vector2(position.x - finger.Last.x, finger.Last.y - position.y);
                    if (finger.Role == Role.Stick) stickNow = position;
                    finger.Last = position;
                    break;
                default:   // Ended, Canceled
                    if (fingers.TryGetValue(id, out var gone) && gone.Role == Role.Stick) stickHeld = false;
                    fingers.Remove(id);
                    break;
            }
        }

        foreach (var finger in fingers.Values)
            if (finger.Role == Role.Button) held.Add(finger.Button);
        if (stickHeld)
        {
            Vector2 offset = (stickNow - stickOrigin) / StickRadius;
            Move = Vector2.ClampMagnitude(new Vector2(offset.x, -offset.y), 1f);
        }
    }

    Finger Begin(Vector2 position, GameManager gm)
    {
        var finger = new Finger { Last = position };
        foreach (var pad in pads)
        {
            if (Vector2.Distance(position, pad.Center) > pad.Radius * 1.15f) continue;
            finger.Role = Role.Button;
            finger.Button = pad.Button;
            down.Add(pad.Button);
            if (pad.Button == TouchButton.Crouch) Crouched = !Crouched;
            return finger;
        }
        if (gm.BuyMenuOpen) return finger;   // the buy menu takes the touches (only BUY closes it from here)
        if (position.x < width * 0.45f && position.y > 260f && !stickHeld)
        {
            finger.Role = Role.Stick;
            stickHeld = true;
            // The stick appears under the thumb, kept whole on the screen.
            stickOrigin = new Vector2(Mathf.Clamp(position.x, safeLeft + StickRadius + 20f, width * 0.45f),
                                      Mathf.Clamp(position.y, 260f, RefHeight - StickRadius - 20f));
            stickNow = position;
            return finger;
        }
        finger.Role = Role.Look;
        return finger;
    }

    // Real touches on a phone; on a PC (-ds-touch) the mouse is one finger.
    static IEnumerable<(int id, TouchPhase phase, Vector2 position)> Touches()
    {
        if (Input.touchCount > 0)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                var touch = Input.GetTouch(i);
                yield return (touch.fingerId, touch.phase, touch.position);
            }
            yield break;
        }
        if (Application.isMobilePlatform) yield break;
        if (Input.GetMouseButtonDown(0)) yield return (-1, TouchPhase.Began, Input.mousePosition);
        else if (Input.GetMouseButton(0)) yield return (-1, TouchPhase.Moved, Input.mousePosition);
        else if (Input.GetMouseButtonUp(0)) yield return (-1, TouchPhase.Ended, Input.mousePosition);
    }

    // ----------------------------------------------------------------- which buttons, where

    void Layout(GameManager gm)
    {
        pads.Clear();
        // Right-hand buttons keep clear of a notch on the right, left-hand ones of one on the left.
        float w = safeRight, h = RefHeight, l = safeLeft;
        var me = gm.Player != null ? gm.Player.Self : null;   // null: watching the bots
        void Add(TouchButton button, float x, float y, float radius, string label, bool lit = false) =>
            pads.Add(new Pad { Button = button, Center = new Vector2(x, y), Radius = radius, Label = label, Lit = lit });

        Add(TouchButton.Pause, width / 2f + 290f, 40f, 36f, "II");
        Add(TouchButton.Scores, width / 2f - 290f, 40f, 36f, "TAB");

        if (me == null || !me.IsAlive)
        {
            Add(TouchButton.Previous, width / 2f - 330f, h - 110f, 55f, "<");
            Add(TouchButton.Next, width / 2f + 330f, h - 110f, 55f, ">");
            return;
        }
        if (gm.BuyMenuOpen)
        {
            Add(TouchButton.Buy, l + 110f, 560f, 62f, "CLOSE", true);
            return;
        }

        Add(TouchButton.Fire, w - 290f, h - 370f, 105f, "FIRE");
        Add(TouchButton.Jump, w - 110f, h - 250f, 68f, "JUMP");
        Add(TouchButton.Crouch, w - 470f, h - 200f, 58f, "CROUCH", Crouched);
        Add(TouchButton.Reload, w - 110f, h - 420f, 58f, "R");
        Add(TouchButton.Switch, w - 120f, h - 580f, 52f, "GUN");
        var weapon = me.Current?.Data;
        if (weapon != null && (weapon.ZoomFov > 0f || weapon.SelectFire))
            Add(TouchButton.Scope, w - 470f, h - 390f, 55f, weapon.ZoomFov > 0f ? "SCOPE" : "AUTO");

        var bomb = gm.Bomb;
        if (bomb.State == BombState.Carried && bomb.Carrier == me)
        {
            Add(TouchButton.Use, w - 470f, h - 560f, 58f, "PLANT", gm.Map.SiteAt(me.transform.position) != null);
            Add(TouchButton.Drop, w - 300f, h - 560f, 44f, "DROP");
        }
        else if (bomb.State == BombState.Planted && me.Team == Team.Swat)
            Add(TouchButton.Use, w - 470f, h - 560f, 58f, "DEFUSE",
                Vector3.Distance(me.transform.position, bomb.Position) < BombManager.DefuseRadius);

        if (gm.CanShop(me)) Add(TouchButton.Buy, l + 110f, 560f, 62f, "BUY", true);
        var voice = VoiceChat.Instance;
        if (voice != null && voice.InSession && GameSettings.VoiceChat && GameSettings.Voice == VoiceMode.HoldV)
            Add(TouchButton.Talk, l + 110f, 430f, 52f, "MIC");
    }

    // ----------------------------------------------------------------- drawing (called by GameUI with its 1080p matrix)

    public void Draw()
    {
        if (!Active || pads.Count == 0) return;
        if (disc == null) disc = MakeDisc(128);
        if (labelStyle == null)
            labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false };

        if (stickHeld)
        {
            Disc(stickOrigin, StickRadius, new Color(1f, 1f, 1f, 0.12f));
            Vector2 knob = stickOrigin + Vector2.ClampMagnitude(stickNow - stickOrigin, StickRadius);
            Disc(knob, 52f, new Color(1f, 1f, 1f, 0.45f));
        }
        else if (pads.Exists(p => p.Button == TouchButton.Fire))
        {
            var hint = new Vector2(safeLeft + 260f, RefHeight - 330f);
            Disc(hint, StickRadius, new Color(1f, 1f, 1f, 0.06f));
            Disc(hint, 52f, new Color(1f, 1f, 1f, 0.16f));
        }

        foreach (var pad in pads)
        {
            bool pressed = held.Contains(pad.Button);
            Color fill = pad.Button == TouchButton.Fire ? new Color(0.85f, 0.25f, 0.2f, pressed ? 0.75f : 0.45f)
                       : pad.Lit ? new Color(0.85f, 0.55f, 0.15f, pressed ? 0.85f : 0.6f)
                       : new Color(0f, 0f, 0f, pressed ? 0.6f : 0.35f);
            Disc(pad.Center, pad.Radius, fill);
            labelStyle.fontSize = Mathf.RoundToInt(Mathf.Clamp(pad.Radius * 0.36f, 14f, 30f));
            labelStyle.normal.textColor = new Color(1f, 1f, 1f, 0.9f);
            GUI.Label(new Rect(pad.Center.x - pad.Radius, pad.Center.y - pad.Radius, pad.Radius * 2f, pad.Radius * 2f), pad.Label, labelStyle);
        }
    }

    void Disc(Vector2 center, float radius, Color color)
    {
        GUI.color = color;
        GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), disc);
        GUI.color = Color.white;
    }

    static Texture2D MakeDisc(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        float r = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                byte a = (byte)(255f * Mathf.Clamp01(r - d));   // soft one-pixel edge
                // A slightly lighter ring at the edge so buttons read on bright maps.
                byte v = (byte)(d > r - 4f ? 255 : 230);
                pixels[y * size + x] = new Color32(v, v, v, a);
            }
        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }
}
