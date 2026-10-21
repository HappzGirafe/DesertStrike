using UnityEngine;

/// <summary>
/// Tokens on screen (the balance, buying a skin with them, "+5 Tokens" after a match) and the
/// tutorial: the offer after signing up and the steps during the practice round.
/// </summary>
public partial class GameUI
{
    static readonly Color Pumpkin = new Color(1f, 0.58f, 0.15f);

    WeaponSkin buying;     // inventory: the skin whose purchase is being asked about
    string buyProblem;
    bool tutorialOffer;    // a new account: offer the practice round

    /// <summary>"+15 Tokens" for a few seconds after a match paid some.</summary>
    void DrawTokenToast()
    {
        if (Online == null || Time.unscaledTime - Online.LastEarnedAt > 8f) return;
        var rect = new Rect(width / 2f - 300f, 150f, 600f, 60f);
        Fill(rect, new Color(0.08f, 0.04f, 0.0f, 0.85f));
        Label(rect, $"+{Online.LastEarned} Tokens", 32, Pumpkin, TextAnchor.MiddleCenter);
    }

    /// <summary>Inventory: buy the skin for Tokens? (The server checks the price and the balance.)</summary>
    void DrawBuySkin()
    {
        var online = Online;
        var weapon = WeaponData.Find(buying.WeaponId);
        int price = online.PriceOf(buying.WeaponId, buying.Id, buying.TokenPrice);
        bool enough = online.Tokens >= price;
        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.6f));
        float w = 860f, h = 640f, x = (width - w) / 2f, y = (RefHeight - h) / 2f;
        Fill(new Rect(x, y, w, h), new Color(0.05f, 0.05f, 0.06f, 0.96f));
        Label(new Rect(x, y + 20f, w, 50f), $"{weapon?.Name} | {buying.Name}", 34, Gold, TextAnchor.MiddleCenter);
        var previewRect = new Rect(x + (w - 480f) / 2f, y + 80f, 480f, 300f);
        if (weapon != null)
        {
            preview.Show(weapon, buying);
            GUI.DrawTexture(previewRect, preview.Texture);
        }
        float ty = previewRect.yMax + 16f;
        Label(new Rect(x, ty, w, 36f), $"{price} Tokens", 30, Pumpkin, TextAnchor.MiddleCenter);
        Label(new Rect(x, ty + 40f, w, 30f), $"You have {Mathf.Max(0, online.Tokens)}", 21, Color.white, TextAnchor.MiddleCenter);
        string note = buyProblem ?? (enough ? null : $"Earn more in matches: 1 for every kill and 5 for every match won (up to {online.TokenDayLimit} a day against bots).");
        if (note != null) WrapLabel(new Rect(x + 50f, ty + 74f, w - 100f, 54f), note, 18, buyProblem != null ? Danger : Dim);
        float by = y + h - 86f, bw = (w - 120f) / 2f;
        if (Button(new Rect(x + 40f, by, bw, 62f), online.Busy ? "BUYING..." : "BUY", true, enough && !online.Busy, 26))
        {
            var skin = buying;
            online.BuySkin(skin.WeaponId, skin.Id, problem =>
            {
                buyProblem = problem;
                if (problem != null) return;
                buying = null;
                if (weapon != null) WeaponSkins.Equip(weapon, skin);   // bought: on the gun at once
                SoundFX.Play(SoundFX.Buy, Vector3.zero, 0.8f, 1f, false);
            });
        }
        if (Button(new Rect(x + 80f + bw, by, bw, 62f), "CANCEL", false, true, 26))
        {
            buying = null;
            preview.Hide();
        }
    }

    /// <summary>After signing up: learn to play in a practice round?</summary>
    void DrawTutorialOffer()
    {
        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.6f));
        float w = 820f, h = 380f, x = (width - w) / 2f, y = (RefHeight - h) / 2f;
        Fill(new Rect(x, y, w, h), new Color(0.05f, 0.05f, 0.06f, 0.96f));
        Label(new Rect(x, y + 24f, w, 50f), "WELCOME TO LOW STRIKE", 36, Gold, TextAnchor.MiddleCenter);
        WrapLabel(new Rect(x + 50f, y + 96f, w - 100f, 120f),
                  "Learn to play in a short practice round: walking, the shop, shooting and the bomb. It takes about two " +
                  "minutes. (Settings > TUTORIAL plays it again.)", 21, Color.white);
        float bw = (w - 120f) / 2f;
        if (Button(new Rect(x + 40f, y + h - 104f, bw, 64f), "START TUTORIAL", true, true, 24))
        {
            tutorialOffer = false;
            gm.StartTutorial();
        }
        if (Button(new Rect(x + 80f + bw, y + h - 104f, bw, 64f), "NOT NOW", false, true, 24)) tutorialOffer = false;
    }

    /// <summary>The practice round's steps, on the left of the screen; FINISH when they are done.</summary>
    void DrawTutorial()
    {
        var tutorial = gm.Tutorial;
        bool done = tutorial.Current == Tutorial.Step.Done;
        float w = 600f, x = 20f, y = 330f, h = done ? 250f : 210f;
        Fill(new Rect(x, y, w, h), new Color(0.05f, 0.05f, 0.06f, 0.85f));
        Fill(new Rect(x, y, 6f, h), Gold);
        string title = done ? "TUTORIAL COMPLETE" : $"TUTORIAL  ·  {(int)tutorial.Current + 1} / {Tutorial.Steps}";
        Label(new Rect(x + 24f, y + 12f, w - 40f, 34f), title, 24, Gold);
        // A bar for each step: the ones done are lit.
        float step = (w - 48f) / Tutorial.Steps;
        for (int i = 0; i < Tutorial.Steps; i++)
            Fill(new Rect(x + 24f + i * step, y + 52f, step - 6f, 6f), i < (int)tutorial.Current ? Gold : new Color(1f, 1f, 1f, 0.2f));
        WrapLabel(new Rect(x + 24f, y + 70f, w - 48f, 90f), tutorial.Instruction(GameInput.Touch), 22, Color.white);
        if (tutorial.Current == Tutorial.Step.Shoot)
            Label(new Rect(x + 24f, y + 160f, w - 48f, 30f), $"Hits: {tutorial.HitCount} / {Tutorial.Hits}", 20, Pumpkin);
        if (done)
        {
            if (Button(new Rect(x + 24f, y + h - 74f, 260f, 56f), "FINISH", true, true, 24)) gm.EndTutorial();
        }
        else Label(new Rect(x + 24f, y + h - 34f, w - 48f, 26f), GameInput.Touch ? "Pause: SKIP TUTORIAL" : "Esc: SKIP TUTORIAL", 16, Dim);
    }
}
