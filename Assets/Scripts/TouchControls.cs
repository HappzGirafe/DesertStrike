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
/// resized in Settings > TOUCH CONTROLS (<see cref="DrawEditor"/>), and a second FIRE (for the left thumb), JUMP,
/// CROUCH, SCOPE or RELOAD can be added there (<see cref="AddExtra"/>); that layout is saved on the device.
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
        public int Extra = -1;   // on an added button: its index in `extras`
        public Vector2 Last;
    }

    struct Pad
    {
        public TouchButton Button;
        public Vector2 Center;
        public float Radius;
        public string Label;
        public bool Lit;     // drawn highlighted (crouched, a request to act)
        public int Extra;    // an added button: its index in `extras`; -1 for the usual ones
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
        LookSettings.Load();   // the scope sensitivity (mouse and fingers), read with the rest of the controls
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

    /// <summary>The buttons the player can add a second one of (ADD BUTTON in the editor), with their pictures.</summary>
    public static readonly (TouchButton Button, string Label)[] ExtraActions =
    {
        (TouchButton.Fire, "fire"), (TouchButton.Jump, "jump"), (TouchButton.Crouch, "crouch"), (TouchButton.Scope, "scope"),
        (TouchButton.Reload, "reload"),
    };

    public const int MaxExtras = 6;

    /// <summary>A button the player added: it does what its original does, and has its own place and size.</summary>
    class Extra
    {
        public TouchButton Button;
        public Vector3 Place;   // like `custom`: centre as a share of the screen's width and height, and a size factor
    }

    // Saved one per key, "Fire;x;y;size", in the order they were added.
    static readonly List<Extra> extras = new List<Extra>();
    const string ExtraKey = LayoutKey + "Extra";

    public static int ExtraCount => extras.Count;

    /// <summary>The picture of a button that can be added, or null for one that cannot.</summary>
    static string ExtraPicture(TouchButton button)
    {
        foreach (var (action, label) in ExtraActions)
            if (action == button) return label;
        return null;
    }

    // Where the stick is drawn while no thumb holds it.
    Vector2 StickHint => new Vector2(safeLeft + 260f, RefHeight - 330f);

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

    /// <summary>Where an added button is, and its size: its original's normal size times its own factor.</summary>
    (Vector2 Center, float Radius) ExtraPlace(int index)
    {
        var extra = extras[index];
        return (new Vector2(extra.Place.x * width, extra.Place.y * RefHeight), DefaultPlace(extra.Button).Radius * extra.Place.z);
    }

    void SetExtraPlace(int index, Vector2 center, float size)
    {
        extras[index].Place = new Vector3(center.x / width, center.y / RefHeight, size);
        SaveExtra(index);
    }

    static void SaveExtra(int index)
    {
        var extra = extras[index];
        PlayerPrefs.SetString(ExtraKey + index, string.Format(CultureInfo.InvariantCulture, "{0};{1:0.####};{2:0.####};{3:0.##}",
                                                              extra.Button, extra.Place.x, extra.Place.y, extra.Place.z));
    }

    // All of them again (one was removed, so the ones after it moved up a key).
    static void SaveExtras()
    {
        for (int i = 0; i < MaxExtras; i++)
        {
            if (i < extras.Count) SaveExtra(i);
            else PlayerPrefs.DeleteKey(ExtraKey + i);
        }
    }

    static void LoadLayout()
    {
        custom.Clear();
        foreach (TouchButton button in Enum.GetValues(typeof(TouchButton)))
        {
            string[] parts = PlayerPrefs.GetString(LayoutKey + button, "").Split(';');
            if (parts.Length == 3 && ReadPlace(parts, 0, out var place)) custom[button] = place;
        }

        extras.Clear();
        bool renumber = false;   // a key was empty or unreadable, so the ones after it now have lower numbers
        for (int i = 0; i < MaxExtras; i++)
        {
            string[] parts = PlayerPrefs.GetString(ExtraKey + i, "").Split(';');
            if (parts.Length == 4
                && Enum.TryParse(parts[0], out TouchButton button) && ExtraPicture(button) != null
                && ReadPlace(parts, 1, out var place))
            {
                if (extras.Count < i) renumber = true;
                extras.Add(new Extra { Button = button, Place = place });
            }
            else if (PlayerPrefs.HasKey(ExtraKey + i)) renumber = true;
        }
        // Each button is saved under its own number, so the keys must match the list again (and lose the bad ones).
        if (renumber) SaveExtras();
    }

    /// <summary>A saved "x;y;size" from <paramref name="first"/> on: kept on the screen and in size, and false for
    /// anything that is not a plain number (a "NaN" would put the button nowhere, and every touch would hit it).</summary>
    static bool ReadPlace(string[] parts, int first, out Vector3 place)
    {
        place = default;
        if (!float.TryParse(parts[first], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) || !float.IsFinite(x)
            || !float.TryParse(parts[first + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) || !float.IsFinite(y)
            || !float.TryParse(parts[first + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out float size) || !float.IsFinite(size))
            return false;
        place = new Vector3(Mathf.Clamp01(x), Mathf.Clamp01(y), Mathf.Clamp(size, 0.5f, 2f));
        return true;
    }

    /// <summary>Every button back to its default place and size, and the added ones gone.</summary>
    public static void ResetLayout()
    {
        foreach (TouchButton button in Enum.GetValues(typeof(TouchButton))) PlayerPrefs.DeleteKey(LayoutKey + button);
        for (int i = 0; i < MaxExtras; i++) PlayerPrefs.DeleteKey(ExtraKey + i);
        custom.Clear();
        extras.Clear();
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
                    // Dragging to look: anywhere on the right, and on FIRE or an added FIRE (aim while shooting).
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

        // An added button counts as its original: FIRE stays held while either FIRE is.
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
        int hit = -1;
        for (int i = 0; i < pads.Count; i++)
        {
            if (Vector2.Distance(position, pads[i].Center) > pads[i].Radius * 1.15f) continue;
            if (hit < 0 || pads[i].Extra >= 0) hit = i;   // the first usual button; an added one (drawn over them) before it
        }
        if (hit >= 0)
        {
            var pad = pads[hit];
            finger.Role = Role.Button;
            finger.Button = pad.Button;
            finger.Extra = pad.Extra;
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
            pads.Add(new Pad { Button = button, Center = center, Radius = radius, Label = label, Lit = lit, Extra = -1 });
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

        // The buttons the player added: each only while its original is shown (SCOPE with a scoped gun), with the
        // original's picture and light. Last, so they are drawn over the usual ones.
        int usual = pads.Count;
        for (int i = 0; i < extras.Count; i++)
            for (int j = 0; j < usual; j++)
            {
                var original = pads[j];
                if (original.Button != extras[i].Button) continue;
                var (center, radius) = ExtraPlace(i);
                pads.Add(new Pad { Button = original.Button, Center = center, Radius = radius, Label = original.Label, Lit = original.Lit, Extra = i });
                break;
            }
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
            Disc(StickHint, StickRadius, new Color(1f, 1f, 1f, 0.06f));
            Disc(StickHint, 52f, new Color(1f, 1f, 1f, 0.16f));
        }

        foreach (var pad in pads)
        {
            bool pressed = Pressed(pad);
            Color fill = pad.Button == TouchButton.Fire ? new Color(0.85f, 0.25f, 0.2f, pressed ? 0.75f : 0.45f)
                       : pad.Lit ? new Color(0.85f, 0.55f, 0.15f, pressed ? 0.85f : 0.6f)
                       : new Color(0f, 0f, 0f, pressed ? 0.6f : 0.35f);
            Disc(pad.Center, pad.Radius, fill);
            Icons.Draw(new Rect(pad.Center.x - pad.Radius, pad.Center.y - pad.Radius, pad.Radius * 2f, pad.Radius * 2f), "ring",
                       new Color(1f, 1f, 1f, pressed ? 0.6f : 0.3f));
            PadLabel(pad.Center, pad.Radius, pad.Label);
        }
    }

    // A finger is on this very button (not only on another one that does the same).
    bool Pressed(Pad pad)
    {
        foreach (var finger in fingers.Values)
            if (finger.Role == Role.Button && finger.Button == pad.Button && finger.Extra == pad.Extra) return true;
        return false;
    }

    // ----------------------------------------------------------------- the layout editor (Settings > TOUCH CONTROLS)

    // The editor's buttons by number: the usual ones (EditorButtons) first, then the added ones.
    int dragging = -1, selected = -1;
    Vector2 dragOffset;
    Rect editorPanel;   // GameUI's panel over the buttons: its touches are the panel's, and new buttons keep out of it

    int EditorCount => EditorButtons.Length + extras.Count;

    // RESET can take away the added button that was selected.
    int Chosen => selected < EditorCount ? selected : -1;

    (TouchButton Button, string Label) EditorItem(int i)
    {
        if (i < EditorButtons.Length) return EditorButtons[i];
        var button = extras[i - EditorButtons.Length].Button;
        return (button, ExtraPicture(button));
    }

    (Vector2 Center, float Radius) EditorPlace(int i) => i < EditorButtons.Length ? Place(EditorButtons[i].Button) : ExtraPlace(i - EditorButtons.Length);

    float EditorSize(int i) => i < EditorButtons.Length ? SizeOf(EditorButtons[i].Button) : extras[i - EditorButtons.Length].Place.z;

    void SetEditorPlace(int i, Vector2 center, float size)
    {
        if (i < EditorButtons.Length) SetPlace(EditorButtons[i].Button, center, size);
        else SetExtraPlace(i - EditorButtons.Length, center, size);
    }

    /// <summary>What the button tapped last in the editor does (its size can be changed), or null.</summary>
    public TouchButton? Selected => Chosen >= 0 ? EditorItem(Chosen).Button : (TouchButton?)null;

    /// <summary>The selected button is one the player added (it can be removed).</summary>
    public bool SelectedIsExtra => Chosen >= EditorButtons.Length;

    /// <summary>The selected button's picture (an <see cref="Icons"/> name).</summary>
    public string SelectedPicture => Chosen >= 0 ? EditorItem(Chosen).Label : null;

    /// <summary>The selected button's size: 1 is its normal size.</summary>
    public float SelectedSize => Chosen >= 0 ? EditorSize(Chosen) : 1f;

    /// <summary>
    /// Draws every button where it is and lets the player drag them (called by GameUI with its 1080p matrix, before
    /// its own panel so a touch on a button wins, except inside <paramref name="panel"/>). Moving or resizing saves at once.
    /// </summary>
    public void DrawEditor(Rect panel)
    {
        Measure();
        PrepareDrawing();
        editorPanel = panel;
        if (selected >= EditorCount) selected = -1;
        if (dragging >= EditorCount) dragging = -1;
        Disc(StickHint, StickRadius, new Color(1f, 1f, 1f, 0.08f));
        PadLabel(StickHint, StickRadius * 0.6f, "stick");

        for (int i = 0; i < EditorCount; i++)
        {
            var (button, label) = EditorItem(i);
            var (center, radius) = EditorPlace(i);
            bool chosen = selected == i;
            if (chosen) Disc(center, radius + 8f, new Color(1f, 0.78f, 0.35f, 0.9f));
            Disc(center, radius, button == TouchButton.Fire ? new Color(0.85f, 0.25f, 0.2f, 0.7f)
                                : chosen ? new Color(0.85f, 0.55f, 0.15f, 0.85f) : new Color(0f, 0f, 0f, 0.55f));
            PadLabel(center, radius, label);
            if (i >= EditorButtons.Length) ExtraBadge(center, radius);
        }

        var e = Event.current;
        if (e.type == EventType.MouseDown && !panel.Contains(e.mousePosition))
        {
            for (int i = EditorCount - 1; i >= 0; i--)   // the one drawn on top first
            {
                var (center, radius) = EditorPlace(i);
                if (Vector2.Distance(e.mousePosition, center) > radius * 1.1f) continue;
                selected = dragging = i;
                dragOffset = center - e.mousePosition;
                e.Use();
                break;
            }
        }
        else if (e.type == EventType.MouseDrag && dragging >= 0)
        {
            float radius = EditorPlace(dragging).Radius;
            var center = e.mousePosition + dragOffset;
            center.x = Mathf.Clamp(center.x, radius, width - radius);
            center.y = Mathf.Clamp(center.y, radius, RefHeight - radius);
            SetEditorPlace(dragging, center, EditorSize(dragging));
            e.Use();
        }
        else if (e.type == EventType.MouseUp && dragging >= 0)
        {
            dragging = -1;
            e.Use();
        }
    }

    // A small gold + on an added button in the editor, to tell it from its original.
    void ExtraBadge(Vector2 center, float radius)
    {
        var at = center + new Vector2(radius * 0.72f, -radius * 0.72f);
        Disc(at, 15f, new Color(1f, 0.78f, 0.35f, 0.95f));
        GUI.color = new Color(0.12f, 0.09f, 0.04f);
        GUI.DrawTexture(new Rect(at.x - 8f, at.y - 2f, 16f, 4f), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(at.x - 2f, at.y - 8f, 4f, 16f), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    /// <summary>Resizes the selected button (0.6 to 1.6 of its normal size).</summary>
    public void ResizeSelected(float size)
    {
        if (Chosen < 0) return;
        Measure();
        SetEditorPlace(Chosen, EditorPlace(Chosen).Center, Mathf.Clamp(size, 0.6f, 1.6f));
    }

    /// <summary>
    /// Adds a second <paramref name="button"/> (one of <see cref="ExtraActions"/>) where it covers nothing, and selects
    /// it so it can be moved and resized at once. False when there are <see cref="MaxExtras"/> already.
    /// </summary>
    public bool AddExtra(TouchButton button)
    {
        if (extras.Count >= MaxExtras || ExtraPicture(button) == null) return false;
        Measure();
        float size = button == TouchButton.Fire ? 0.8f : 1f;   // the main FIRE is big; a second one sits among other buttons
        var center = FreeSpot(DefaultPlace(button).Radius * size);
        extras.Add(new Extra { Button = button, Place = new Vector3(center.x / width, center.y / RefHeight, size) });
        SaveExtra(extras.Count - 1);
        selected = EditorCount - 1;
        dragging = -1;
        return true;
    }

    /// <summary>Removes the selected button if the player added it (the usual ones stay).</summary>
    public void RemoveSelected()
    {
        if (!SelectedIsExtra) return;
        extras.RemoveAt(Chosen - EditorButtons.Length);
        SaveExtras();
        selected = dragging = -1;
    }

    // The free place nearest the left thumb, above the stick (where a second FIRE is wanted most): clear of every
    // button, the stick and the editor's panel. On a full screen, that place anyway (the player moves it).
    Vector2 FreeSpot(float radius)
    {
        var taken = new List<(Vector2 Center, float Radius)> { (StickHint, StickRadius) };
        for (int i = 0; i < EditorCount; i++) taken.Add(EditorPlace(i));
        var anchor = new Vector2(safeLeft + 300f, 470f);
        var best = anchor;
        float bestDistance = float.MaxValue;
        for (float y = radius + 90f; y <= RefHeight - radius - 10f; y += 20f)   // below PAUSE and the scores
            for (float x = safeLeft + radius + 10f; x <= safeRight - radius - 10f; x += 20f)
            {
                var spot = new Vector2(x, y);
                float distance = (spot - anchor).sqrMagnitude;
                if (distance >= bestDistance || !Free(spot, radius + 14f, taken)) continue;
                best = spot;
                bestDistance = distance;
            }
        return best;
    }

    bool Free(Vector2 spot, float radius, List<(Vector2 Center, float Radius)> taken)
    {
        var nearest = new Vector2(Mathf.Clamp(spot.x, editorPanel.xMin, editorPanel.xMax), Mathf.Clamp(spot.y, editorPanel.yMin, editorPanel.yMax));
        if (editorPanel.width > 0f && Vector2.Distance(spot, nearest) < radius) return false;
        foreach (var (center, other) in taken)
            if (Vector2.Distance(spot, center) < radius + other) return false;
        return true;
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
