using UnityEngine;

/// <summary>
/// Password recovery for online accounts (online-server/recovery.py): ACCOUNT's email (saved after the password and
/// confirmed with a code the server mails there), and FORGOT PASSWORD? on the log-in screen: a code goes to that
/// email, and with it a new password logs the player in.
/// </summary>
public partial class GameUI
{
    // ACCOUNT's email
    string emailInput = "", emailPassword = "", emailCode = "";
    // FORGOT PASSWORD
    bool forgotOpen, forgotCodeSent;
    string forgotNick = "", forgotCode = "", forgotPassword = "", forgotRepeat = "", forgotMessage;
    bool forgotBad;

    static readonly Color Hint = new Color(1f, 1f, 1f, 0.3f);

    /// <summary>ACCOUNT's email section; returns how tall it was.</summary>
    float DrawEmailSection(float fx, float fw, float cy)
    {
        var online = Online;
        var user = online.User;
        float top = cy;
        Label(new Rect(fx, cy, fw, 30f), "Email for password recovery", 22, Color.white);
        cy += 32f;
        bool pending = !string.IsNullOrEmpty(user.email_pending);
        string status = pending ? $"Waiting for the code sent to {user.email_pending}"
                      : !string.IsNullOrEmpty(user.email) ? $"Confirmed: {user.email}"
                      : "No email yet: without one, a forgotten password cannot be reset.";
        Label(new Rect(fx, cy, fw, 26f), status, 16, pending || string.IsNullOrEmpty(user.email) ? Pumpkin : MoneyGreen);
        cy += 32f;
        if (pending)
        {
            emailCode = TextInput(new Rect(fx, cy, fw - 230f, 42f), "email-code", emailCode, 12, numbers: true);
            Label(new Rect(fx + 8f, cy, fw - 230f, 42f), emailCode.Length == 0 && !KeyboardOpenFor("email-code") ? "code from the email" : "", 16, Hint);
            if (Button(new Rect(fx + fw - 220f, cy, 220f, 42f), "CONFIRM", true, emailCode.Trim().Length >= 8 && !online.Busy, 20))
                online.ConfirmEmail(emailCode, problem =>
                {
                    SetAccountPageMessage(problem ?? "Email confirmed.", problem != null);
                    if (problem == null) emailCode = "";
                });
            cy += 50f;
            if (Button(new Rect(fx, cy, fw, 40f), "USE ANOTHER EMAIL", false, !online.Busy, 17)) user.email_pending = "";
            return cy + 48f - top;
        }
        float half = (fw - 10f) / 2f;
        emailInput = TextInput(new Rect(fx, cy, half, 42f), "email-address", emailInput, 254);
        Label(new Rect(fx + 8f, cy, half, 42f), emailInput.Length == 0 && !KeyboardOpenFor("email-address") ? "email address" : "", 16, Hint);
        emailPassword = TextInput(new Rect(fx + half + 10f, cy, half, 42f), "email-password", emailPassword, 128, true);
        Label(new Rect(fx + half + 18f, cy, half, 42f), emailPassword.Length == 0 && !KeyboardOpenFor("email-password") ? "your password" : "", 16, Hint);
        cy += 50f;
        bool removing = emailInput.Trim().Length == 0 && !string.IsNullOrEmpty(user.email);
        if (Button(new Rect(fx, cy, fw, 46f), removing ? "REMOVE EMAIL" : "SAVE EMAIL", false,
                   emailPassword.Length > 0 && (removing || emailInput.Trim().Length > 0) && !online.Busy, 20))
        {
            string address = emailInput.Trim();
            online.SetEmail(address, emailPassword, problem =>
            {
                emailPassword = "";
                SetAccountPageMessage(problem ?? (address.Length == 0 ? "Email removed." : $"Code sent to {address}: type it here."), problem != null);
                if (problem == null) emailInput = "";
            });
        }
        return cy + 56f - top;
    }

    void OpenForgotPassword(string nick)
    {
        forgotOpen = true;
        forgotCodeSent = false;
        forgotNick = nick ?? "";
        forgotCode = forgotPassword = forgotRepeat = "";
        forgotMessage = null;
    }

    void DrawForgotPassword()
    {
        var online = Online;
        if (online.LoggedIn || (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Escape))
        {
            forgotOpen = false;
            return;
        }
        Fill(new Rect(0f, 0f, width, RefHeight), new Color(0f, 0f, 0f, 0.45f));
        float w = 760f, h = forgotCodeSent ? 800f : 560f, x = (width - w) / 2f, y = (RefHeight - h) / 2f;
        Fill(new Rect(x, y, w, h), PanelColor);
        Label(new Rect(x, y + 18f, w, 60f), "FORGOT PASSWORD", 44, Gold, TextAnchor.MiddleCenter);
        float fx = x + 60f, fw = w - 120f, cy = y + 90f;
        WrapLabel(new Rect(fx, cy, fw, 50f), "We email a code to the account's confirmed email (you add it in ACCOUNT once you are logged in). No email? Ask an admin for a code.", 17, Dim);
        cy += 62f;

        Label(new Rect(fx, cy, fw, 30f), "Nickname", 20, Color.white);
        forgotNick = TextInput(new Rect(fx, cy + 32f, fw, 42f), "forgot-nick", forgotNick, 16);
        cy += 90f;
        float sendWidth = forgotCodeSent ? fw : fw * 0.6f - 5f;
        if (Button(new Rect(fx, cy, sendWidth, 52f), forgotCodeSent ? "SEND A NEW CODE" : "SEND CODE", !forgotCodeSent,
                   forgotNick.Trim().Length >= 3 && !online.Busy, 22))
            online.ForgotPassword(forgotNick, (message, problem) =>
            {
                forgotMessage = problem ?? message;
                forgotBad = problem != null;
                if (problem == null) forgotCodeSent = true;
            });
        // A code from an admin (admin panel > RECOVERY CODE): no mail needed.
        if (!forgotCodeSent && Button(new Rect(fx + fw * 0.6f + 5f, cy, fw * 0.4f - 5f, 52f), "I HAVE A CODE", false, true, 20))
        {
            forgotCodeSent = true;
            SetForgotMessage("Type the code from your email, or the one an admin gave you.", false);
        }
        cy += 66f;

        if (forgotCodeSent)
        {
            forgotCode = TextInput(new Rect(fx, cy, fw, 42f), "forgot-code", forgotCode, 12, numbers: true);
            Label(new Rect(fx + 8f, cy, fw, 42f), forgotCode.Length == 0 && !KeyboardOpenFor("forgot-code") ? "the code (8 digits)" : "", 16, Hint);
            cy += 50f;
            forgotPassword = TextInput(new Rect(fx, cy, fw, 42f), "forgot-new", forgotPassword, 128, true);
            Label(new Rect(fx + 8f, cy, fw, 42f), forgotPassword.Length == 0 && !KeyboardOpenFor("forgot-new") ? "new password (at least 8 characters)" : "", 16, Hint);
            cy += 50f;
            forgotRepeat = TextInput(new Rect(fx, cy, fw, 42f), "forgot-repeat", forgotRepeat, 128, true);
            Label(new Rect(fx + 8f, cy, fw, 42f), forgotRepeat.Length == 0 && !KeyboardOpenFor("forgot-repeat") ? "new password again" : "", 16, Hint);
            cy += 54f;
            if (Button(new Rect(fx, cy, fw, 56f), "SET NEW PASSWORD", true, forgotCode.Trim().Length >= 8 && forgotPassword.Length > 0 && !online.Busy, 22))
            {
                if (forgotPassword != forgotRepeat) SetForgotMessage("The two new passwords are not the same.", true);
                else if (forgotPassword.Length < 8) SetForgotMessage("The new password needs at least 8 characters.", true);
                else
                    online.ResetPassword(forgotNick, forgotCode, forgotPassword, problem =>
                    {
                        SetForgotMessage(problem, true);
                        if (problem != null) return;
                        forgotOpen = accountOpen = false;
                        forgotCode = forgotPassword = forgotRepeat = accountPassword = accountRepeat = "";
                        gm.MenuNotice = "Password changed: you are logged in. Other computers and phones were logged out.";
                    });
            }
            cy += 70f;
        }

        if (!string.IsNullOrEmpty(forgotMessage)) WrapLabel(new Rect(fx, cy, fw, 50f), forgotMessage, 17, forgotBad ? Danger : Gold);
        if (Button(new Rect(x + w / 2f - 150f, y + h - 76f, 300f, 56f), "BACK"))
        {
            forgotOpen = false;
            accountName = forgotNick.Trim().Length > 0 ? forgotNick.Trim() : accountName;
        }
    }

    void SetForgotMessage(string text, bool bad)
    {
        forgotMessage = text;
        forgotBad = bad;
    }
}
