using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The console (` in a match, or CONSOLE in the pause menu): a line to type commands in, what they answered, and the
/// cheats' switches once "/cheats on" worked (see <see cref="Cheats"/>); and what the cheats draw: the wall hack's
/// boxes round every enemy, seen through walls, and a line saying which cheats are on.
/// </summary>
public partial class GameUI
{
    static readonly Color CheatColor = new Color(1f, 0.3f, 0.3f);

    string consoleText = "";
    // -ds-console-demo (with -ds-touch, logged in as an admin): opens the console in the match and types "/cheats on" on
    // the game's keyboard, key by key, then DONE, as a phone would (tests).
    bool consoleDemo = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-ds-console-demo") >= 0;
    readonly List<string> consoleLines = new List<string> { "Type /help for the commands." };
    int consoleOpenedFrame = -1;

    void StartConsoleDemo()
    {
        if (!consoleDemo || gm.State != MatchState.Live || gm.Warmup || !Cheats.Allowed(gm)) return;
        consoleDemo = false;
        gm.ConsoleOpen = true;
        keyboardDemo.Enqueue("@console");
        foreach (char c in "/cheats on") keyboardDemo.Enqueue(c == ' ' ? "SPACE" : c.ToString());
        keyboardDemo.Enqueue("DONE");
        Debug.Log("[Console] Demo: typing /cheats on on the game's keyboard");
    }

    void DrawConsole()
    {
        var e = Event.current;
        bool enter = e.type == EventType.KeyDown && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
        float w = Mathf.Min(1100f, width - 40f), x = (width - w) / 2f, y = 90f;
        float h = Cheats.On ? 420f : 320f;   // the switches below the line
        Fill(new Rect(x, y, w, h), new Color(0.02f, 0.02f, 0.03f, 0.92f));
        Fill(new Rect(x, y, w, 4f), Gold);
        Label(new Rect(x + 20f, y + 10f, w - 40f, 30f), "CONSOLE", 22, Gold);
        if (!GameInput.Touch) Label(new Rect(x + 20f, y + 10f, w - 40f, 30f), "` or Esc: close", 15, Dim, TextAnchor.MiddleRight);
        else if (Button(new Rect(x + w - 170f, y + 8f, 150f, 40f), "CLOSE", false, true, 18))   // a phone's buttons wait meanwhile
        {
            gm.ConsoleOpen = false;
            keyboardField = null;
            return;
        }

        // What was typed and answered, newest last.
        float ly = y + 48f;
        for (int i = Mathf.Max(0, consoleLines.Count - 6); i < consoleLines.Count; i++, ly += 28f)
            Label(new Rect(x + 20f, ly, w - 40f, 28f), consoleLines[i], 18, consoleLines[i].StartsWith("> ") ? Dim : Color.white);

        var field = new Rect(x + 20f, y + 222f, w - 200f, 48f);
        if (consoleOpenedFrame < 0) consoleOpenedFrame = Time.frameCount;
        consoleText = TextInput(field, "console", consoleText, 64).Replace("`", "");   // the key that opened it
        if (!OnPhone && Time.frameCount - consoleOpenedFrame < 3) GUI.FocusControl("console");
        if (Button(new Rect(field.xMax + 10f, field.y, 160f, 48f), "ENTER", true, true, 20)
            || (enter && GUI.GetNameOfFocusedControl() == "console") || KeyboardDone("console"))
        {
            if (consoleText.Trim().Length > 0) consoleLines.Add("> " + consoleText.Trim());
            string answer = Cheats.Command(gm, consoleText);
            Debug.Log($"[Console] {consoleText.Trim()} -> {answer}");
            if (answer != null) consoleLines.Add(answer);
            consoleText = "";
            if (enter) e.Use();
        }

        if (!Cheats.On) return;
        // The switches.
        float sy = y + 290f, sw = (w - 40f - 5f * 10f) / 6f;
        string[] names = { "AIMBOT", "FLY", "INFINITE AMMO", "WALL HACK", "INFINITE MONEY", "INFINITE HEALTH" };
        bool[] on = { Cheats.Aimbot, Cheats.Fly, Cheats.InfiniteAmmo, Cheats.WallHack, Cheats.InfiniteMoney, Cheats.InfiniteHealth };
        for (int i = 0; i < names.Length; i++)
        {
            if (!Button(new Rect(x + 20f + i * (sw + 10f), sy, sw, 56f), names[i], on[i], true, 16)) continue;
            switch (i)
            {
                case 0: Cheats.Aimbot = !Cheats.Aimbot; break;
                case 1: Cheats.Fly = !Cheats.Fly; break;
                case 2: Cheats.InfiniteAmmo = !Cheats.InfiniteAmmo; break;
                case 3: Cheats.WallHack = !Cheats.WallHack; break;
                case 4: Cheats.InfiniteMoney = !Cheats.InfiniteMoney; break;
                case 5: Cheats.InfiniteHealth = !Cheats.InfiniteHealth; break;
            }
        }
        Label(new Rect(x + 20f, sy + 64f, w - 40f, 26f),
              GameInput.Touch ? "Fly: JUMP goes up, CROUCH goes down. Infinite ammo: 999 in reserve." : "Fly: Space goes up, Ctrl goes down. Infinite ammo: 999 in reserve.",
              16, Dim);
    }

    /// <summary>The wall hack's boxes (enemies through walls, with name, health and distance), and which cheats are on.</summary>
    void DrawCheatsHud()
    {
        if (!Cheats.On) return;
        var parts = new List<string>();
        if (Cheats.Aimbot) parts.Add("AIMBOT");
        if (Cheats.Fly) parts.Add("FLY");
        if (Cheats.InfiniteAmmo) parts.Add("INFINITE AMMO");
        if (Cheats.WallHack) parts.Add("WALL HACK");
        if (Cheats.InfiniteMoney) parts.Add("INFINITE MONEY");
        if (Cheats.InfiniteHealth) parts.Add("INFINITE HEALTH");
        if (!gm.ConsoleOpen)
            Label(new Rect(0f, 78f, width, 26f), parts.Count > 0 ? "CHEATS: " + string.Join("  ·  ", parts) : "CHEATS ON", 17, CheatColor,
                  TextAnchor.MiddleCenter);

        var camera = Camera.main;
        var me = gm.Player != null ? gm.Player.Self : null;
        if (!Cheats.WallHackOn || camera == null || me == null || Event.current.type != EventType.Repaint) return;
        foreach (var c in gm.Combatants)
        {
            if (c == me || !c.IsAlive || c.Team == me.Team) continue;
            Vector3 feet = camera.WorldToScreenPoint(c.transform.position);
            Vector3 top = camera.WorldToScreenPoint(c.transform.position + Vector3.up * (c.Height + 0.15f));
            if (feet.z <= 0f || top.z <= 0f) continue;   // behind the camera
            // Screen pixels (y up) to this UI's units (y down).
            float bottom = (Screen.height - feet.y) / scale, high = (Screen.height - top.y) / scale;
            float boxHeight = Mathf.Max(8f, bottom - high), boxWidth = boxHeight * 0.45f, cx = feet.x / scale;
            var box = new Rect(cx - boxWidth / 2f, high, boxWidth, boxHeight);
            Fill(new Rect(box.x, box.y, box.width, 2f), CheatColor);
            Fill(new Rect(box.x, box.yMax - 2f, box.width, 2f), CheatColor);
            Fill(new Rect(box.x, box.y, 2f, box.height), CheatColor);
            Fill(new Rect(box.xMax - 2f, box.y, 2f, box.height), CheatColor);
            int metres = Mathf.RoundToInt(Vector3.Distance(me.transform.position, c.transform.position));
            Label(new Rect(cx - 150f, box.y - 24f, 300f, 22f), c.DisplayName, 15, CheatColor, TextAnchor.MiddleCenter);
            Label(new Rect(cx - 150f, box.yMax + 2f, 300f, 22f), $"{c.Health} HP  ·  {metres} m", 14, CheatColor, TextAnchor.MiddleCenter);
        }
    }
}
