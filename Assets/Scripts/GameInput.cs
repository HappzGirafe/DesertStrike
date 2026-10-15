using UnityEngine;

/// <summary>
/// What the player asks for this frame: from the keyboard and mouse, or on a phone or tablet from the on-screen
/// controls (<see cref="TouchControls"/>). Everything that reads the player's input goes through here.
/// </summary>
public static class GameInput
{
    public static bool Touch => TouchControls.Active;

    /// <summary>-1..1 sideways (x) and forward (y). A phone's stick pushed a little walks slowly and silently.</summary>
    public static Vector2 Move => Touch ? TouchControls.Move : new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));

    /// <summary>How far to turn this frame (x right, y up), scaled by the sensitivity setting.</summary>
    public static Vector2 Look(float sensitivity) =>
        Touch ? TouchControls.LookPixels * (0.055f * sensitivity)
              : new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * sensitivity;

    public static bool FireHeld => Touch ? TouchControls.Held(TouchButton.Fire) : Input.GetMouseButton(0);
    public static bool FireDown => Touch ? TouchControls.Down(TouchButton.Fire) : Input.GetMouseButtonDown(0);

    /// <summary>Right click: scope (AWP) or semi/full-auto (TEC-DC9, M1911).</summary>
    public static bool AltFireDown => Touch ? TouchControls.Down(TouchButton.Scope) : Input.GetMouseButtonDown(1);

    public static bool JumpDown => Touch ? TouchControls.Down(TouchButton.Jump) : Input.GetKeyDown(KeyCode.Space);
    public static bool CrouchHeld => Touch ? TouchControls.Crouched : Input.GetKey(KeyCode.LeftControl);
    public static bool WalkHeld => !Touch && Input.GetKey(KeyCode.LeftShift);
    public static bool ReloadDown => Touch ? TouchControls.Down(TouchButton.Reload) : Input.GetKeyDown(KeyCode.R);

    /// <summary>A weapon slot picked directly (keys 1 / 2 / 3), or null.</summary>
    public static WeaponSlot? SlotDown
    {
        get
        {
            if (Touch) return null;
            if (Input.GetKeyDown(KeyCode.Alpha1)) return WeaponSlot.Primary;
            if (Input.GetKeyDown(KeyCode.Alpha2)) return WeaponSlot.Secondary;
            if (Input.GetKeyDown(KeyCode.Alpha3)) return WeaponSlot.Knife;
            return null;
        }
    }

    public static bool LastWeaponDown => !Touch && Input.GetKeyDown(KeyCode.Q);

    /// <summary>+1 next weapon, -1 previous (mouse wheel, or the phone's weapon button), 0 none.</summary>
    public static int WeaponCycle
    {
        get
        {
            if (Touch) return TouchControls.Down(TouchButton.Switch) ? 1 : 0;
            float wheel = Input.GetAxis("Mouse ScrollWheel");
            return wheel > 0f ? -1 : wheel < 0f ? 1 : 0;
        }
    }

    /// <summary>E: plant or defuse the bomb (held).</summary>
    public static bool UseHeld => Touch ? TouchControls.Held(TouchButton.Use) : Input.GetKey(KeyCode.E);
    public static bool DropBombDown => Touch ? TouchControls.Down(TouchButton.Drop) : Input.GetKeyDown(KeyCode.G);
    public static bool BuyDown => Touch ? TouchControls.Down(TouchButton.Buy) : Input.GetKeyDown(KeyCode.B);
    public static bool ScoresHeld => Touch ? TouchControls.Held(TouchButton.Scores) : Input.GetKey(KeyCode.Tab);

    /// <summary>Esc, a phone's pause button, or Android's back button (which Unity reports as Escape).</summary>
    public static bool PauseDown => Input.GetKeyDown(KeyCode.Escape) || (Touch && TouchControls.Down(TouchButton.Pause));

    public static bool TalkHeld => Touch ? TouchControls.Held(TouchButton.Talk) : Input.GetKey(VoiceChat.TalkKey);
    public static bool TalkDown => Touch ? TouchControls.Down(TouchButton.Talk) : Input.GetKeyDown(VoiceChat.TalkKey);

    /// <summary>Spectating: the next or the previous player (left / right click, or the arrows on a phone).</summary>
    public static bool SpectateNextDown => Touch ? TouchControls.Down(TouchButton.Next) : Input.GetMouseButtonDown(0);
    public static bool SpectatePreviousDown => Touch ? TouchControls.Down(TouchButton.Previous) : Input.GetMouseButtonDown(1);

    // Names of the controls for hints on screen.
    public static string UseName => Touch ? "USE" : "E";
    public static string BuyName => Touch ? "BUY" : "B";
    public static string DropName => Touch ? "DROP" : "G";
    public static string TalkName => Touch ? "MIC" : "V";
}
