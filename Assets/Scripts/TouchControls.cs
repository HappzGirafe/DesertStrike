using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using UnityEngine;

public enum TouchButton { Fire, Jump, Crouch, Reload, Switch, Scope, Use, Drop, Buy, Scores, Pause, Talk, Previous, Next }

/// <summary>
/// On-screen controls for phones and tablets: a stick that appears where the left thumb touches (push it a little
/// to walk quietly), dragging anywhere on the right to look around, and buttons: FIRE (drag it to aim while
/// shooting), JUMP, CROUCH (on/off), RELOAD, a weapon switch, SCOPE for the AWP and select-fire pistols, PLANT or
/// DEFUSE and DROP when the bomb matters, BUY in the spawn, MIC in LAN games, the scoreboard and pause; arrows to
/// switch players while spectating. On by itself on phones and tablets; -ds-touch shows it on a PC, where the mouse
/// acts as one finger. Laid out for a 1080-high screen like the rest of the HUD. Every button can be moved and
/// resized in Settings > TOUCH CONTROLS (<see cref="DrawEditor"/>); that layout is saved on the device.
/// </summary>
[DefaultExecutionOrder(-100)]   // read the fingers before the player moves
public class TouchControls : MonoBehaviour
{
    public static bool Active { get; private set; }

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern int WebTouchScreen();
#else
    static int WebTouchScreen() => 0;
#endif
    static bool? touchScreen;

    /// <summary>
    /// A phone or tablet: the app on one, or the browser version on one (the browser is asked, as an iPad's browser
    /// calls itself a Mac).
    /// </summary>
    public static bool TouchScreen => touchScreen ??= Application.isMobilePlatform || WebTouchScreen() == 1;
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
        var args = Environment.GetCommandLineArgs();
        Active = TouchScreen || Array.IndexOf(args, "-ds-touch") >= 0;
        if (TouchScreen) Screen.sleepTimeout = SleepTimeout.NeverSleep;   // no dimming mid-round
        LoadLayout();
        if (Array.IndexOf(args, "-ds-touch-demo") >= 0) DemoLayout();
    }

    // ----------------------------------------------------------------- where the buttons are

    // Buttons the player moved or resized: centre as a share of the screen's width and height (so the layout fits
    // other screens too) and a size factor.
    static readonly Dictionary<TouchButton, Vector3> custom = new Dictionary<TouchButton, Vector3>();
    const string LayoutKey = "DesertStrike.touch.";

    /// <summary>The buttons the layout editor shows (the spectator arrows keep their place).</summary>
    static readonly (TouchButton Button, string Label)[] EditorButtons =
    {
        (TouchButton.Fire, "fire"), (TouchButton.Jump, "jump"), (TouchButton.Crouch, "crouch"), (TouchButton.Reload, "reload"),
        (TouchButton.Switch, "gun"), (TouchButton.Scope, "scope"), (TouchButton.Use, "plant"), (TouchButton.Drop, "drop"),
        (TouchButton.Buy, "buy"), (TouchButton.Talk, "mic"), (TouchButton.Scores, "scores"), (TouchButton.Pause, "pause"),
    };

    // Where each button sits until moved, and its size (1080p units; l and r: the safe area's left and right edge).
    (Vector2 Center, float Radius) DefaultPlace(TouchButton button)
    {
        float h = RefHeight, l = safeLeft, r = safeRight;
        switch (button)
        {
            case TouchButton.Pause: return (new Vector2(width / 2f + 290f, 40f), 36f);
            case TouchButton.Scores: return (new Vector2(width / 2f - 290f, 40f), 36f);
            case TouchButton.Previous: return (new Vector2(width / 2f - 330f, h - 110f), 55f);
            case TouchButton.Next: return (new Vector2(width / 2f + 330f, h - 110f), 55f);
            case TouchButton.Fire: return (new Vector2(r - 290f, h - 370f), 105f);
            case TouchButton.Jump: return (new Vector2(r - 110f, h - 250f), 68f);
            case TouchButton.Crouch: return (new Vector2(r - 470f, h - 200f), 58f);
            case TouchButton.Reload: return (new Vector2(r - 110f, h - 420f), 58f);
            case TouchButton.Switch: return (new Vector2(r - 120f, h - 580f), 52f);
            case TouchButton.Scope: return (new Vector2(r - 470f, h - 390f), 55f);
            case TouchButton.Use: return (new Vector2(r - 470f, h - 560f), 58f);
            case TouchButton.Drop: return (new Vector2(r - 300f, h - 560f), 44f);
            case TouchButton.Buy: return (new Vector2(l + 110f, 560f), 62f);
            default: return (new Vector2(l + 110f, 430f), 52f);   // MIC
        }
    }

    /// <summary>Where a button is now: where the player put it, or its default place.</summary>
    (Vector2 Center, float Radius) Place(TouchButton button)
    {
        var (center, radius) = DefaultPlace(button);
        if (custom.TryGetValue(button, out var placed))
        {
            center = new Vector2(placed.x * width, placed.y * RefHeight);
            radius *= placed.z;
        }
        return (center, radius);
    }

    public static float SizeOf(TouchButton button) => custom.TryGetValue(button, out var placed) ? placed.z : 1f;

    void SetPlace(TouchButton button, Vector2 center, float size, bool save = true)
    {
        custom[button] = new Vector3(center.x / width, center.y / RefHeight, size);
        if (save)
            PlayerPrefs.SetString(LayoutKey + button, string.Format(CultureInfo.InvariantCulture, "{0:0.####};{1:0.####};{2:0.##}",
                                                                  center.x / width, center.y / RefHeight, size));
    }

    static void LoadLayout()
    {
        custom.Clear();
        foreach (TouchButton button in Enum.GetValues(typeof(TouchButton)))
        {
            string[] parts = PlayerPrefs.GetString(LayoutKey + button, "").Split(';');
            if (parts.Length == 3
                && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float size))
                custom[button] = new Vector3(Mathf.Clamp01(x), Mathf.Clamp01(y), Mathf.Clamp(size, 0.5f, 2f));
        }
    }

    /// <summary>Every button back to its default place and size.</summary>
    public static void ResetLayout()
    {
        foreach (TouchButton button in Enum.GetValues(typeof(TouchButton))) PlayerPrefs.DeleteKey(LayoutKey + button);
        custom.Clear();
    }

    // -ds-touch-demo: FIRE on the left and a big JUMP, for this run only (screenshots).
    static void DemoLayout()
    {
        custom[TouchButton.Fire] = new Vector3(0.12f, 0.45f, 1f);
        custom[TouchButton.Jump] = new Vector3(0.93f, 0.72f, 1.4f);
    }

    void Measure()
    {
        float unit = Screen.height / RefHeight;
        width = Screen.width / unit;
        var safe = Screen.safeArea;
        safeLeft = safe.xMin / unit;
        safeRight = safe.xMax / unit;
    }

    // ----------------------------------------------------------------- reading the fingers

    void Update()
    {
        down.Clear();
        held.Clear();
        LookPixels = Vector2.zero;
        Move = Vector2.zero;
        if (!Active) return;

        Measure();
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
        if (TouchScreen) yield break;   // the mouse a phone's browser makes of a finger: the finger counts already
        if (Input.GetMouseButtonDown(0)) yield return (-1, TouchPhase.Began, Input.mousePosition);
        else if (Input.GetMouseButton(0)) yield return (-1, TouchPhase.Moved, Input.mousePosition);
        else if (Input.GetMouseButtonUp(0)) yield return (-1, TouchPhase.Ended, Input.mousePosition);
    }

    // ----------------------------------------------------------------- which buttons, where

    void Layout(GameManager gm)
    {
        pads.Clear();
        var me = gm.Player != null ? gm.Player.Self : null;   // null: watching the bots
        void Add(TouchButton button, string label, bool lit = false)
        {
            var (center, radius) = Place(button);
            pads.Add(new Pad { Button = button, Center = center, Radius = radius, Label = label, Lit = lit });
        }

        Add(TouchButton.Pause, "pause");
        Add(TouchButton.Scores, "scores");

        if (me == null || !me.IsAlive)
        {
            Add(TouchButton.Previous, "prev");
            Add(TouchButton.Next, "next");
            return;
        }
        if (gm.BuyMenuOpen)
        {
            Add(TouchButton.Buy, "close", true);
            return;
        }

        Add(TouchButton.Fire, "fire");
        Add(TouchButton.Jump, "jump");
        Add(TouchButton.Crouch, "crouch", Crouched);
        Add(TouchButton.Reload, "reload");
        Add(TouchButton.Switch, "gun");
        var weapon = me.Current?.Data;
        if (weapon != null && (weapon.ZoomFov > 0f || weapon.SelectFire))
            Add(TouchButton.Scope, weapon.ZoomFov > 0f ? "scope" : "auto");

        var bomb = gm.Bomb;
        bool carrier = bomb.State == BombState.Carried && bomb.Carrier == me;
        bool onSite = carrier && gm.Map.SiteAt(me.transform.position) != null;
        bool nearBomb = bomb.State == BombState.Planted && me.Team == Team.Swat
                        && Vector3.Distance(me.transform.position, bomb.Position) < BombManager.DefuseRadius;
        bool nearGun = gm.Drops.Nearest(me, WeaponDrops.SwapRadius) != null;
        if (onSite || (carrier && !nearGun)) Add(TouchButton.Use, "plant", onSite);
        else if (nearBomb || (bomb.State == BombState.Planted && me.Team == Team.Swat && !nearGun)) Add(TouchButton.Use, "defuse", nearBomb);
        else if (nearGun) Add(TouchButton.Use, "take", true);
        // DROP: the gun in hand, or the bomb with the knife in hand.
        bool knife = me.Current == null || me.Current.Data.Slot == WeaponSlot.Knife;
        if (!knife || carrier) Add(TouchButton.Drop, "drop");

        if (gm.CanShop(me)) Add(TouchButton.Buy, "buy", true);
        var voice = VoiceChat.Instance;
        if (voice != null && voice.InSession && GameSettings.VoiceChat && GameSettings.Voice == VoiceMode.HoldV)
            Add(TouchButton.Talk, "mic");
    }

    // ----------------------------------------------------------------- drawing (called by GameUI with its 1080p matrix)

    void PrepareDrawing()
    {
        if (disc == null) disc = MakeDisc(128);
        if (labelStyle == null)
            labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false };
    }

    // A button's picture (Icons), a bit smaller than the button; a word only for a name with no picture.
    void PadLabel(Vector2 center, float radius, string label)
    {
        if (Icons.Get(label) != null)
        {
            float size = radius * 1.12f;
            Icons.Draw(new Rect(center.x - size / 2f, center.y - size / 2f, size, size), label, new Color(1f, 1f, 1f, 0.92f));
            return;
        }
        label = Loc.T(label);
        labelStyle.fontSize = Mathf.RoundToInt(Mathf.Clamp(radius * 0.36f, 14f, 30f));
        // A longer word in another language: smaller, to stay inside the button.
        var content = new GUIContent(label);
        while (labelStyle.fontSize > 11 && labelStyle.CalcSize(content).x > radius * 1.9f) labelStyle.fontSize--;
        labelStyle.normal.textColor = new Color(1f, 1f, 1f, 0.9f);
        GUI.Label(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), label, labelStyle);
    }

    public void Draw()
    {
        if (!Active || pads.Count == 0) return;
        PrepareDrawing();

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
            Icons.Draw(new Rect(pad.Center.x - pad.Radius, pad.Center.y - pad.Radius, pad.Radius * 2f, pad.Radius * 2f), "ring",
                       new Color(1f, 1f, 1f, pressed ? 0.6f : 0.3f));
            PadLabel(pad.Center, pad.Radius, pad.Label);
        }
    }

    // ----------------------------------------------------------------- the layout editor (Settings > TOUCH CONTROLS)

    TouchButton? dragging;
    Vector2 dragOffset;

    /// <summary>The button tapped last in the editor (its size can be changed).</summary>
    public TouchButton? Selected { get; private set; }

    /// <summary>
    /// Draws every button where it is and lets the player drag them (called by GameUI with its 1080p matrix, before
    /// its own panel so a touch on a button wins). Moving or resizing saves at once.
    /// </summary>
    public void DrawEditor()
    {
        Measure();
        PrepareDrawing();
        var hint = new Vector2(safeLeft + 260f, RefHeight - 330f);
        Disc(hint, StickRadius, new Color(1f, 1f, 1f, 0.08f));
        PadLabel(hint, StickRadius * 0.6f, "stick");

        foreach (var (button, label) in EditorButtons)
        {
            var (center, radius) = Place(button);
            bool selected = Selected == button;
            if (selected) Disc(center, radius + 8f, new Color(1f, 0.78f, 0.35f, 0.9f));
            Disc(center, radius, button == TouchButton.Fire ? new Color(0.85f, 0.25f, 0.2f, 0.7f)
                                : selected ? new Color(0.85f, 0.55f, 0.15f, 0.85f) : new Color(0f, 0f, 0f, 0.55f));
            PadLabel(center, radius, label);
        }

        var e = Event.current;
        if (e.type == EventType.MouseDown)
        {
            for (int i = EditorButtons.Length - 1; i >= 0; i--)   // the one drawn on top first
            {
                var button = EditorButtons[i].Button;
                var (center, radius) = Place(button);
                if (Vector2.Distance(e.mousePosition, center) > radius * 1.1f) continue;
                Selected = dragging = button;
                dragOffset = center - e.mousePosition;
                e.Use();
                break;
            }
        }
        else if (e.type == EventType.MouseDrag && dragging is TouchButton moved)
        {
            float radius = Place(moved).Radius;
            var center = e.mousePosition + dragOffset;
            center.x = Mathf.Clamp(center.x, radius, width - radius);
            center.y = Mathf.Clamp(center.y, radius, RefHeight - radius);
            SetPlace(moved, center, SizeOf(moved));
            e.Use();
        }
        else if (e.type == EventType.MouseUp && dragging != null)
        {
            dragging = null;
            e.Use();
        }
    }

    /// <summary>Resizes the selected button (0.6 to 1.6 of its normal size).</summary>
    public void ResizeSelected(float size)
    {
        if (Selected is not TouchButton button) return;
        Measure();
        SetPlace(button, Place(button).Center, Mathf.Clamp(size, 0.6f, 1.6f));
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
