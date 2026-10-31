using UnityEngine;

/// <summary>
/// Settings > TOUCH CONTROLS (moving, resizing, adding and removing the phone's buttons) and the look and scope
/// sensitivity, set with big − and + buttons that suit fingers (and a slider between them for the mouse).
/// </summary>
public partial class GameUI
{
    bool touchPanelHidden;   // TOUCH CONTROLS: the panel folded away, to reach the buttons under it

    /// <summary>
    /// Every on-screen button where it is: drag one to move it, tap one and change its size (or remove it, if it was
    /// added), ADD BUTTON for a second FIRE, JUMP, CROUCH, SCOPE or RELOAD, the two sensitivities, and RESET to put
    /// the buttons back. Saved on the device at once. The panel stays at the top, above the default buttons, and
    /// HIDE folds it away for buttons moved under it.
    /// </summary>
    void DrawTouchLayout()
    {
        if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape)
        {
            touchLayoutOpen = touchPanelHidden = false;
            Event.current.Use();
            return;
        }
        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.35f));
        if (touch == null) return;

        float w = touchPanelHidden ? 620f : Mathf.Min(960f, width - 40f), x = (width - w) / 2f, y = 92f;
        var panel = new Rect(x, y, w, touchPanelHidden ? 76f : 340f);
        touch.DrawEditor(panel);   // first, so a touch on a button is the button's (outside the panel)
        Panel(panel);
        if (touchPanelHidden)
        {
            Label(new Rect(x + 24f, y + 12f, 250f, 52f), "TOUCH CONTROLS", 24, Gold);
            if (Button(new Rect(x + 280f, y + 12f, 150f, 52f), "SHOW")) touchPanelHidden = false;
            if (Button(new Rect(x + 446f, y + 12f, 150f, 52f), "DONE", true)) touchLayoutOpen = touchPanelHidden = false;   // the panel shows again next time
            return;
        }

        const float pad = 24f;
        float column = (w - 3f * pad) / 2f, left = x + pad, right = x + 2f * pad + column;
        Label(new Rect(left, y + 12f, 300f, 44f), "TOUCH CONTROLS", 28, Gold);
        WrapLabel(new Rect(left + 300f, y + 14f, w - 2f * pad - 446f, 44f), "Drag a button to move it. Tap one to change its size.", 15, Dim);
        if (Button(new Rect(x + w - pad - 130f, y + 12f, 130f, 44f), "HIDE", false, true, 20)) touchPanelHidden = true;

        // The selected button: its picture, its size with − and +, and REMOVE for one that was added.
        float cy = y + 66f;
        if (touch.Selected is TouchButton selected)
        {
            float size = touch.SelectedSize;
            Label(new Rect(left, cy, column, 26f), $"Size of {selected.ToString().ToUpperInvariant()}   {Mathf.RoundToInt(size * 100f)}%", 18, Color.white);
            var picture = new Rect(left, cy + 30f, 56f, 56f);
            Round(picture, new Color(0f, 0f, 0f, 0.5f), 28f);
            Icons.Draw(new Rect(picture.x + 8f, picture.y + 8f, 40f, 40f), touch.SelectedPicture, Color.white);
            float snapped = Mathf.Round(size * 20f) / 20f;   // 5% steps
            if (StepButton(new Rect(left + 70f, cy + 30f, 76f, 56f), false)) touch.ResizeSelected(snapped - 0.05f);
            if (StepButton(new Rect(left + 156f, cy + 30f, 76f, 56f), true)) touch.ResizeSelected(snapped + 0.05f);
            if (touch.SelectedIsExtra && Button(new Rect(left + 246f, cy + 30f, column - 246f, 56f), "REMOVE", false, true, 20))
                touch.RemoveSelected();
        }
        else WrapLabel(new Rect(left, cy + 44f, column, 30f), "Tap a button to select it.", 17, Dim);

        // ADD BUTTON: a second one of these (a FIRE for the left thumb...), placed where the screen is free.
        int count = TouchControls.ExtraCount;
        bool full = count >= TouchControls.MaxExtras;
        Label(new Rect(right, cy, column, 26f), "ADD BUTTON", 18, Gold);
        Text(new Rect(right, cy, column, 26f), $"{count} / {TouchControls.MaxExtras}", 16, Dim, TextAnchor.MiddleRight);
        var actions = TouchControls.ExtraActions;
        float bw = (column - (actions.Length - 1) * 10f) / actions.Length;
        for (int i = 0; i < actions.Length; i++)
        {
            var rect = new Rect(right + i * (bw + 10f), cy + 30f, bw, 56f);
            if (Button(rect, "", false, !full)) touch.AddExtra(actions[i].Button);
            Icons.Draw(new Rect(rect.center.x - 20f, rect.center.y - 20f, 40f, 40f), actions[i].Label, new Color(1f, 1f, 1f, full ? 0.35f : 0.95f));
        }

        cy = y + 166f;
        LookSensitivity(left, cy, column);
        ScopeSensitivity(right, cy, column);

        cy = y + 266f;
        if (Button(new Rect(left, cy, column, 54f), "RESET")) TouchControls.ResetLayout();
        if (Button(new Rect(right, cy, column, 54f), "DONE", true)) touchLayoutOpen = false;
    }

    // ----------------------------------------------------------------- sensitivity

    /// <summary>Settings: the look sensitivity, and beside it the scope's share of it (mouse and fingers alike).</summary>
    float SensitivitySlider(float x, float y, float w)
    {
        float half = (w - 30f) / 2f;
        LookSensitivity(x, y, half);
        ScopeSensitivity(x + half + 30f, y, half);
        return y + 95f;
    }

    void LookSensitivity(float x, float y, float w)
    {
        float look = PlayerController.MouseSensitivity;
        float changed = StepSlider(x, y, w, $"Look sensitivity   {look:0.0}", look, 0.3f, 8f, 0.1f);
        if (!Mathf.Approximately(changed, look)) GameSettings.SetSensitivity(changed);
    }

    void ScopeSensitivity(float x, float y, float w)
    {
        float share = LookSettings.ScopeShare;
        float changed = StepSlider(x, y, w, $"Scope sensitivity   {Mathf.RoundToInt(share * 100f)}%", share,
                                   LookSettings.MinScopeShare, LookSettings.MaxScopeShare, 0.05f);
        if (!Mathf.Approximately(changed, share)) LookSettings.SetScopeShare(changed);
    }

    /// <summary>
    /// A number setting for fingers and the mouse alike: its title (with the value) above, big − and + buttons at the
    /// ends that move it one <paramref name="step"/> at a time, and a slider between them. Returns the new value.
    /// </summary>
    float StepSlider(float x, float y, float w, string title, float value, float min, float max, float step)
    {
        Label(new Rect(x, y, w, 30f), title, 20, Color.white);
        const float side = 60f;
        float row = y + 34f;
        // From the nearest step, so a value the slider left in between comes back onto the steps.
        if (StepButton(new Rect(x, row, side, 48f), false)) value = Mathf.Round(value / step) * step - step;
        if (StepButton(new Rect(x + w - side, row, side, 48f), true)) value = Mathf.Round(value / step) * step + step;
        value = GUI.HorizontalSlider(new Rect(x + side + 14f, row + 14f, w - 2f * side - 28f, 24f), value, min, max);
        return Mathf.Clamp(value, min, max);
    }

    // Holding − or +: which one (by its place), the last frame it was seen held, and when it steps again.
    int stepKey, stepFrame;
    float stepNext;

    /// <summary>
    /// A − or + button, drawn with bars (no font needed for a true minus). A tap makes one step; held, it makes more
    /// after a moment, like a key on a keyboard, so a big change needs no long tapping.
    /// </summary>
    bool StepButton(Rect rect, bool plus)
    {
        var type = Event.current.type;   // read first: the button uses up the event, which changes its type
        bool held = GUI.RepeatButton(rect, GUIContent.none, GUIStyle.none);
        float hover = Hover(rect, held ? true : (bool?)null);
        float radius = Mathf.Min(10f, rect.height * 0.28f);
        Round(rect, Color.Lerp(new Color(0.12f, 0.13f, 0.15f, 0.9f), new Color(0.24f, 0.26f, 0.3f, 0.95f), hover), radius);
        Sheen(rect, 0.05f + 0.05f * hover);
        Outline(rect, new Color(1f, 1f, 1f, 0.07f + 0.2f * hover), radius);
        float bar = Mathf.Min(rect.width, rect.height) * 0.4f, thick = 5f;
        Fill(new Rect(rect.center.x - bar / 2f, rect.center.y - thick / 2f, bar, thick), Color.white);
        if (plus) Fill(new Rect(rect.center.x - thick / 2f, rect.center.y - bar / 2f, thick, bar), Color.white);

        int key = Mathf.RoundToInt(rect.x) * 7919 + Mathf.RoundToInt(rect.y);
        bool heldBefore = stepKey == key && stepFrame >= Time.frameCount - 1;
        if (type == EventType.Repaint)
        {
            if (!held) return false;
            stepKey = key;
            stepFrame = Time.frameCount;
            if (!heldBefore)
            {
                stepNext = Time.unscaledTime + 0.45f;
                return true;
            }
            if (Time.unscaledTime < stepNext) return false;
            stepNext = Time.unscaledTime + 0.07f;
            return true;
        }
        if (type != EventType.MouseUp || !held) return false;
        stepKey = 0;
        return !heldBefore;   // a tap too quick for a frame in between: its step when the finger lifts
    }
}
