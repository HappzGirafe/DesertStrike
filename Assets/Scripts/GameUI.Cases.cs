using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CASES (inventory, and the main menu's bar): skin cases bought with Tokens. Each card shows a case, its price and how
/// many of its skins the account still lacks; OPEN asks the server (online-server/tokens.py picks the skin), then a strip
/// of the case's skins rolls past a gold line and slows down on the one won, which can be put on the gun at once.
/// </summary>
public partial class GameUI
{
    bool casesOpen;
    CaseInfo openingCase;      // the case being opened, or null
    string caseWon;            // what it gave ("weapon/skin"), once the server has said
    string caseProblem;
    float reelStart = -1f;     // when the strip started rolling (-1: waiting for the server)
    float reelStop;            // where it stops (screen units along the strip)
    int reelTick;
    readonly List<string> reel = new List<string>();
    string testCase;           // -ds-cases <id>: opens that case once the cases are known (tests)

    const float ReelSeconds = 5.5f, TileWidth = 220f, TileGap = 12f;
    const int ReelLength = 40, ReelWinner = 32;

    static readonly Dictionary<string, (Color Main, Color Second)> CaseColors = new Dictionary<string, (Color, Color)>
    {
        { "neon", (new Color(0.2f, 0.92f, 1f), new Color(1f, 0.3f, 0.82f)) },
        { "old", (new Color(0.93f, 0.72f, 0.36f), new Color(0.45f, 0.3f, 0.17f)) },
        { "halloween", (new Color(1f, 0.56f, 0.12f), new Color(0.42f, 0.18f, 0.58f)) },
    };

    void OpenCases()
    {
        inventoryOpen = true;
        casesOpen = true;
        openingCase = null;
        caseProblem = null;
        Online.RefreshTokens();
    }

    void DrawCases()
    {
        bool rolling = openingCase != null && (reelStart < 0f || Time.unscaledTime - reelStart < ReelSeconds);
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape && !rolling)
        {
            Event.current.Use();
            if (openingCase != null) openingCase = null;
            else casesOpen = false;
            preview.Hide();
            return;
        }
        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.5f));
        float w = Mathf.Min(1320f, width - 40f), h = 860f, x = (width - w) / 2f, y = (RefHeight - h) / 2f;
        Panel(new Rect(x, y, w, h), 22f);
        Label(new Rect(x, y + 18f, w, 60f), "CASES", 48, Gold, TextAnchor.MiddleCenter);
        if (Accounts.IsOnline && Online.Tokens >= 0)
            Label(new Rect(x + w - 330f, y + 30f, 300f, 34f), $"Tokens: {Online.Tokens}", 22, Pumpkin, TextAnchor.MiddleRight);

        if (!Accounts.IsOnline)
        {
            WrapLabel(new Rect(x + 120f, y + 300f, w - 240f, 80f), "Log in online to open cases with Tokens.", 26, Color.white);
            if (Button(new Rect(x + w / 2f - 160f, y + 420f, 320f, 62f), "LOG IN / SIGN UP", true, true, 22))
                OpenAccount("Log in online to open cases with Tokens", online: true);
        }
        else if (openingCase != null)
        {
            DrawCaseOpening(x, y, w, h);
            return;
        }
        else DrawCaseCards(x, y, w);
        if (Button(new Rect(x + w / 2f - 150f, y + h - 84f, 300f, 58f), "BACK")) casesOpen = false;
    }

    void DrawCaseCards(float x, float y, float w)
    {
        var cases = Online.Cases;
        if (testCase != null && !Online.Busy)
        {
            var test = cases.Find(k => k.id == testCase);
            if (test != null)
            {
                testCase = null;
                StartOpening(test);
                return;
            }
        }
        Label(new Rect(x, y + 80f, w, 30f), "Every case gives a skin you have not got yet; the dearer a skin, the rarer it is.", 19, Dim, TextAnchor.MiddleCenter);
        if (cases.Count == 0)
        {
            Label(new Rect(x, y + 360f, w, 40f), Online.Tokens < 0 ? "Asking the server..." : "No cases right now.", 24, Dim, TextAnchor.MiddleCenter);
            return;
        }
        const float gap = 30f;
        float cw = (w - 80f - gap * (cases.Count - 1)) / cases.Count, top = y + 130f;
        for (int i = 0; i < cases.Count; i++)
        {
            var info = cases[i];
            var (main, second) = CaseColors.TryGetValue(info.id, out var colors) ? colors : (Gold, new Color(0.3f, 0.3f, 0.35f));
            var card = new Rect(x + 40f + i * (cw + gap), top, cw, 560f);
            float hover = Hover(card);
            Glow(card, new Color(main.r, main.g, main.b, 0.12f + 0.18f * hover), 22f);
            Round(card, new Color(0.06f, 0.065f, 0.08f, 0.95f), 18f);
            Fade(new Rect(card.x + 2f, card.y + 2f, card.width - 4f, 260f), new Color(main.r, main.g, main.b, 0.14f));
            Outline(card, new Color(main.r, main.g, main.b, 0.35f + 0.4f * hover), 18f);

            DrawCaseBox(new Rect(card.center.x - 120f, card.y + 40f - 6f * hover, 240f, 170f), main, second);
            Label(new Rect(card.x, card.y + 228f, card.width, 40f), info.name, 30, Color.white, TextAnchor.MiddleCenter);
            int left = info.items.FindAll(item => !OwnsItem(item)).Count;
            Label(new Rect(card.x, card.y + 268f, card.width, 28f), $"{left} of {info.items.Count} skins still to get", 18, Dim, TextAnchor.MiddleCenter);

            // A few of its skins, as swatches.
            int shown = Mathf.Min(6, info.items.Count);
            float sw = 46f, sx = card.center.x - (shown * sw + (shown - 1) * 8f) / 2f;
            for (int k = 0; k < shown; k++)
            {
                string item = info.items[k * info.items.Count / shown];
                SkinChip(new Rect(sx + k * (sw + 8f), card.y + 312f, sw, sw), SkinOfItem(item, out _), RarityColor(PriceOfItem(item)));
            }
            var pill = new Rect(card.center.x - 90f, card.y + 384f, 180f, 44f);
            Round(pill, new Color(Pumpkin.r, Pumpkin.g, Pumpkin.b, 0.2f), 22f);
            Label(pill, $"{info.price} Tokens", 24, Pumpkin, TextAnchor.MiddleCenter);

            bool enough = Online.Tokens >= info.price;
            string text = left == 0 ? "YOU HAVE THEM ALL" : !enough ? "NOT ENOUGH TOKENS" : "OPEN";
            if (Button(new Rect(card.x + 30f, card.y + 460f, card.width - 60f, 66f), text, left > 0 && enough, left > 0 && enough && !Online.Busy, 26))
                StartOpening(info);
        }
        if (caseProblem != null) Label(new Rect(x, top + 580f, w, 30f), caseProblem, 19, Danger, TextAnchor.MiddleCenter);
    }

    // A case, drawn: a box with a lid, metal corners and a lock, in the case's colours.
    static void DrawCaseBox(Rect r, Color main, Color second)
    {
        Glow(r, new Color(main.r, main.g, main.b, 0.3f), 18f);
        Round(r, Color.Lerp(second, Color.black, 0.25f), 16f);
        Round(new Rect(r.x + 8f, r.y + 8f, r.width - 16f, r.height - 16f), second, 12f);
        Sheen(r, 0.12f);
        Round(new Rect(r.x, r.y + r.height * 0.32f, r.width, 14f), main, 5f);        // the lid's edge
        Round(new Rect(r.x + 18f, r.y + 6f, 10f, r.height - 12f), new Color(main.r, main.g, main.b, 0.55f), 4f);
        Round(new Rect(r.xMax - 28f, r.y + 6f, 10f, r.height - 12f), new Color(main.r, main.g, main.b, 0.55f), 4f);
        var lockRect = new Rect(r.center.x - 22f, r.y + r.height * 0.32f - 12f, 44f, 40f);
        Round(lockRect, Color.Lerp(main, Color.white, 0.25f), 8f);
        Round(new Rect(lockRect.center.x - 4f, lockRect.y + 12f, 8f, 16f), Color.Lerp(second, Color.black, 0.4f), 4f);
        Outline(r, new Color(1f, 1f, 1f, 0.15f), 16f);
    }

    void StartOpening(CaseInfo info)
    {
        openingCase = info;
        caseWon = null;
        caseProblem = null;
        reelStart = -1f;
        reel.Clear();
        for (int i = 0; i < ReelLength; i++) reel.Add(info.items[Random.Range(0, info.items.Count)]);
        Online.OpenCase(info.id, (won, problem) =>
        {
            if (won == null)
            {
                openingCase = null;
                caseProblem = problem ?? "The case could not be opened.";
                return;
            }
            caseWon = won;
            reel[ReelWinner] = won;
            reelStart = Time.unscaledTime;
            reelTick = -1;
            // It stops somewhere on the won tile, not always in its middle.
            reelStop = ReelWinner * (TileWidth + TileGap) + Random.Range(-TileWidth * 0.38f, TileWidth * 0.38f);
        });
    }

    void DrawCaseOpening(float x, float y, float w, float h)
    {
        Label(new Rect(x, y + 80f, w, 34f), openingCase.name, 26, Color.white, TextAnchor.MiddleCenter);
        var strip = new Rect(x + 40f, y + 150f, w - 80f, 200f);
        Round(strip, new Color(0f, 0f, 0f, 0.45f), 14f);
        if (reelStart < 0f)
        {
            Label(strip, "OPENING...", 30, Gold, TextAnchor.MiddleCenter);
            return;
        }
        float t = Mathf.Clamp01((Time.unscaledTime - reelStart) / ReelSeconds);
        float eased = 1f - Mathf.Pow(1f - t, 3f);
        float position = reelStop * eased;
        int under = Mathf.RoundToInt(position / (TileWidth + TileGap));
        if (under != reelTick && t < 1f)
        {
            reelTick = under;
            SoundFX.Play(SoundFX.HitMarker, Vector3.zero, 0.25f, 1.4f, false);
        }

        GUI.BeginGroup(strip);
        for (int i = 0; i < reel.Count; i++)
        {
            float tx = i * (TileWidth + TileGap) - position + strip.width / 2f - TileWidth / 2f;
            if (tx > strip.width || tx + TileWidth < 0f) continue;
            DrawCaseTile(new Rect(tx, 16f, TileWidth, strip.height - 32f), reel[i], t >= 1f && i == ReelWinner);
        }
        GUI.EndGroup();
        Fill(new Rect(strip.center.x - 2f, strip.y - 10f, 4f, strip.height + 20f), Gold);   // the line it stops on
        Fade(new Rect(strip.x, strip.y, 140f, strip.height), new Color(0.035f, 0.04f, 0.05f, 0.9f), toRight: true);

        if (t < 1f) return;
        var skin = SkinOfItem(caseWon, out var weapon);
        if (skin == null || weapon == null) return;
        float ry = strip.yMax + 30f;
        Label(new Rect(x, ry, w, 40f), "YOU GOT", 28, Gold, TextAnchor.MiddleCenter);
        Label(new Rect(x, ry + 40f, w, 40f), $"{weapon.Name} | {skin.Name}", 32, RarityColor(PriceOfItem(caseWon)), TextAnchor.MiddleCenter);
        var previewRect = new Rect(x + w / 2f - 240f, ry + 86f, 480f, 230f);
        preview.Show(weapon, skin);
        GUI.DrawTexture(previewRect, preview.Texture, ScaleMode.ScaleToFit);
        float bw = 280f, bx = x + w / 2f - (bw * 3f + 40f) / 2f, by = y + h - 84f;
        if (Button(new Rect(bx, by, bw, 58f), "EQUIP", true, true, 24))
        {
            WeaponSkins.Equip(weapon, skin);
            SoundFX.Play(SoundFX.Buy, Vector3.zero, 0.8f, 1f, false);
            openingCase = null;
            preview.Hide();
        }
        if (Button(new Rect(bx + bw + 20f, by, bw, 58f), "OPEN ANOTHER", false, true, 22))
        {
            openingCase = null;
            preview.Hide();
        }
        if (Button(new Rect(bx + 2f * (bw + 20f), by, bw, 58f), "BACK", false, true, 22))
        {
            openingCase = null;
            casesOpen = false;
            preview.Hide();
        }
    }

    void DrawCaseTile(Rect r, string item, bool won)
    {
        var skin = SkinOfItem(item, out var weapon);
        var color = RarityColor(PriceOfItem(item));
        if (won) Glow(r, new Color(color.r, color.g, color.b, 0.6f), 16f);
        Round(r, new Color(0.08f, 0.085f, 0.1f, 0.97f), 12f);
        Fade(new Rect(r.x + 2f, r.yMax - 60f, r.width - 4f, 58f), new Color(color.r, color.g, color.b, 0.35f), up: true);
        Round(new Rect(r.x + 10f, r.yMax - 8f, r.width - 20f, 4f), color, 2f);
        if (skin != null)
        {
            if (WeaponSkins.MaterialFor(skin, SkinPart.Main) != null)
            {
                DrawSwatch(new Rect(r.center.x - 52f, r.y + 18f, 48f, 48f), WeaponSkins.MaterialFor(skin, SkinPart.Main));
                DrawSwatch(new Rect(r.center.x + 4f, r.y + 18f, 48f, 48f), WeaponSkins.MaterialFor(skin, SkinPart.Grip));
            }
            else SkinChip(new Rect(r.center.x - 30f, r.y + 14f, 60f, 56f), skin, color);
            FitLabel(new Rect(r.x + 8f, r.y + 76f, r.width - 16f, 30f), weapon.Name, 19, Dim, TextAnchor.MiddleCenter);
            FitLabel(new Rect(r.x + 8f, r.y + 102f, r.width - 16f, 32f), skin.Name, 22, Color.white, TextAnchor.MiddleCenter);
        }
        Outline(r, new Color(color.r, color.g, color.b, won ? 1f : 0.45f), 12f);
    }

    // A skin's colours as a square; a skin that is a model of its own (no colours to show): a gun in its rarity's colour.
    static void SkinChip(Rect r, WeaponSkin skin, Color rarity)
    {
        var material = skin != null ? WeaponSkins.MaterialFor(skin, SkinPart.Main) : null;
        if (material != null)
        {
            DrawSwatch(r, material);
            return;
        }
        Round(r, new Color(rarity.r, rarity.g, rarity.b, 0.18f), 8f);
        Icons.Draw(new Rect(r.x + r.width * 0.12f, r.y + r.height * 0.12f, r.width * 0.76f, r.height * 0.76f), "gun", rarity);
    }

    // Blue for the cheaper skins, purple, gold for the dearest.
    static Color RarityColor(int price) =>
        price >= 700 ? new Color(1f, 0.76f, 0.22f) : price >= 300 ? new Color(0.72f, 0.4f, 1f) : new Color(0.3f, 0.62f, 1f);

    int PriceOfItem(string item)
    {
        var parts = item.Split('/');
        return parts.Length == 2 ? Online.PriceOf(parts[0], parts[1], 0) : 0;
    }

    bool OwnsItem(string item)
    {
        var parts = item.Split('/');
        return parts.Length == 2 && Online.Owns(parts[0], parts[1]);
    }

    /// <summary>The skin a case names ("weapon/skin"), and its weapon; null when this game has no such skin.</summary>
    static WeaponSkin SkinOfItem(string item, out WeaponData weapon)
    {
        weapon = null;
        if (string.IsNullOrEmpty(item)) return null;
        var parts = item.Split('/');
        if (parts.Length != 2) return null;
        weapon = WeaponData.Find(parts[0]);
        return weapon != null ? WeaponSkins.For(weapon).Find(s => s.Id == parts[1]) : null;
    }
}
