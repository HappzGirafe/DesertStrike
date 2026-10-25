"""Password recovery by email: an account can have a confirmed email, and a forgotten password is reset with a code
sent there.

    POST /me/email           {email, password, lang}: sets (or changes) the account's email, which counts once
                             confirmed: a code goes to the new address. "" removes the email. Needs the password.
    POST /me/email/confirm   {code}: the code from that mail; the address is the account's email from then on
    POST /auth/forgot        {nick, lang}: if the account has a confirmed email, a reset code goes there; the answer is
                             the same either way (it does not tell which nicknames exist or have an email)
    POST /auth/reset         {nick, code, new_password}: the code from that mail and a new password; logs out every
                             device and logs this one in

Codes are 8 digits, kept only as a keyed hash (AUTH_SECRET), good for 30 minutes and 5 tries. Mails go out through the
mailbox in .env (SMTP_HOST, SMTP_PORT, SMTP_USER, SMTP_PASSWORD, MAIL_FROM); MAIL_DIR (tests) writes them as files
there instead. Without either, these answer that email is not set up on this server.
"""
import hmac
import re
import secrets
import smtplib
import ssl
import threading
import time
from email.message import EmailMessage
from email.utils import formataddr, parseaddr
from pathlib import Path
from typing import Optional

from fastapi import Depends, HTTPException, Request
from pydantic import BaseModel

SCHEMA = """
ALTER TABLE users ADD COLUMN email TEXT NOT NULL DEFAULT '';
CREATE TABLE IF NOT EXISTS email_codes (
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    purpose TEXT NOT NULL,
    email TEXT NOT NULL,
    code_hash TEXT NOT NULL,
    expires_at REAL NOT NULL,
    tries INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (user_id, purpose)
);
"""
# purpose: "verify" (confirming a new email; email is the new address) or "reset" (a forgotten password)

CODE_SECONDS = 30 * 60
MAX_TRIES = 5
EMAIL = re.compile(r"^[^@\s<>()\[\],;:\"]{1,64}@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}$")

# The mails, in the game's six languages: subject, then the text ({nick}, {code}, {minutes}).
MAILS = {
    "verify": {
        "en": ("Low Strike: confirm your email",
               "Hello {nick},\n\nyour code to confirm this email for your Low Strike account is:\n\n    {code}\n\n"
               "Type it in the game (ACCOUNT). It works for {minutes} minutes.\nIf you did not ask for it, ignore this mail."),
        "ua": ("Low Strike: підтвердіть email",
               "Привіт, {nick}!\n\nКод для підтвердження цього email для вашого акаунта Low Strike:\n\n    {code}\n\n"
               "Введіть його в грі (АКАУНТ). Він діє {minutes} хвилин.\nЯкщо ви його не просили, просто проігноруйте цей лист."),
        "de": ("Low Strike: E-Mail bestätigen",
               "Hallo {nick},\n\ndein Code, um diese E-Mail für dein Low-Strike-Konto zu bestätigen:\n\n    {code}\n\n"
               "Gib ihn im Spiel ein (KONTO). Er gilt {minutes} Minuten.\nWenn du ihn nicht angefordert hast, ignoriere diese Mail."),
        "fr": ("Low Strike : confirmez votre e-mail",
               "Bonjour {nick},\n\nvotre code pour confirmer cet e-mail pour votre compte Low Strike :\n\n    {code}\n\n"
               "Saisissez-le dans le jeu (COMPTE). Il est valable {minutes} minutes.\nSi vous ne l'avez pas demandé, ignorez ce message."),
        "it": ("Low Strike: conferma la tua email",
               "Ciao {nick},\n\nil tuo codice per confermare questa email per il tuo account Low Strike:\n\n    {code}\n\n"
               "Inseriscilo nel gioco (ACCOUNT). Vale {minutes} minuti.\nSe non l'hai chiesto, ignora questa email."),
        "es": ("Low Strike: confirma tu email",
               "Hola {nick}:\n\ntu código para confirmar este email en tu cuenta de Low Strike es:\n\n    {code}\n\n"
               "Escríbelo en el juego (CUENTA). Vale {minutes} minutos.\nSi no lo pediste, ignora este correo."),
    },
    "reset": {
        "en": ("Low Strike: your password reset code",
               "Hello {nick},\n\nyour code to set a new password for your Low Strike account is:\n\n    {code}\n\n"
               "Type it in the game (FORGOT PASSWORD) with your new password. It works for {minutes} minutes.\n"
               "If you did not ask for it, ignore this mail: your password stays as it is."),
        "ua": ("Low Strike: код для нового пароля",
               "Привіт, {nick}!\n\nКод, щоб задати новий пароль для вашого акаунта Low Strike:\n\n    {code}\n\n"
               "Введіть його в грі (ЗАБУЛИ ПАРОЛЬ?) разом із новим паролем. Він діє {minutes} хвилин.\n"
               "Якщо ви його не просили, проігноруйте цей лист: пароль залишиться тим самим."),
        "de": ("Low Strike: dein Code für ein neues Passwort",
               "Hallo {nick},\n\ndein Code, um ein neues Passwort für dein Low-Strike-Konto festzulegen:\n\n    {code}\n\n"
               "Gib ihn im Spiel ein (PASSWORT VERGESSEN?), zusammen mit dem neuen Passwort. Er gilt {minutes} Minuten.\n"
               "Wenn du ihn nicht angefordert hast, ignoriere diese Mail: dein Passwort bleibt, wie es ist."),
        "fr": ("Low Strike : votre code pour un nouveau mot de passe",
               "Bonjour {nick},\n\nvotre code pour choisir un nouveau mot de passe pour votre compte Low Strike :\n\n    {code}\n\n"
               "Saisissez-le dans le jeu (MOT DE PASSE OUBLIÉ ?) avec le nouveau mot de passe. Il est valable {minutes} minutes.\n"
               "Si vous ne l'avez pas demandé, ignorez ce message : votre mot de passe ne change pas."),
        "it": ("Low Strike: il tuo codice per una nuova password",
               "Ciao {nick},\n\nil tuo codice per impostare una nuova password per il tuo account Low Strike:\n\n    {code}\n\n"
               "Inseriscilo nel gioco (PASSWORD DIMENTICATA?) con la nuova password. Vale {minutes} minuti.\n"
               "Se non l'hai chiesto, ignora questa email: la password resta quella di prima."),
        "es": ("Low Strike: tu código para una contraseña nueva",
               "Hola {nick}:\n\ntu código para poner una contraseña nueva en tu cuenta de Low Strike es:\n\n    {code}\n\n"
               "Escríbelo en el juego (¿OLVIDASTE LA CONTRASEÑA?) con la contraseña nueva. Vale {minutes} minutos.\n"
               "Si no lo pediste, ignora este correo: tu contraseña sigue igual."),
    },
}


class EmailBody(BaseModel):
    email: str = ""
    password: str = ""
    lang: str = "en"


class CodeBody(BaseModel):
    code: str = ""


class ForgotBody(BaseModel):
    nick: str = ""
    lang: str = "en"


class ResetBody(BaseModel):
    nick: str = ""
    code: str = ""
    new_password: str = ""


def install(app, s):
    cfg = s.CONFIG
    sending = threading.Lock()

    def mail_ready() -> bool:
        return bool(cfg.mail_dir or (cfg.smtp_host and cfg.mail_from))

    def send(to: str, subject: str, text: str):
        """Sends the mail in the background (the answer does not wait for the mail server)."""
        message = EmailMessage()
        name, address = parseaddr(cfg.mail_from or "Low Strike <noreply@localhost>")
        message["From"] = formataddr((name or "Low Strike", address))
        message["To"] = to
        message["Subject"] = subject
        message.set_content(text)

        def deliver():
            try:
                with sending:
                    if cfg.mail_dir:
                        folder = Path(cfg.mail_dir)
                        folder.mkdir(parents=True, exist_ok=True)
                        (folder / f"{time.time():.6f}-{secrets.token_hex(4)}.eml").write_bytes(bytes(message))
                        return
                    context = ssl.create_default_context()
                    if cfg.smtp_port == 465:
                        server = smtplib.SMTP_SSL(cfg.smtp_host, cfg.smtp_port, context=context, timeout=20)
                    else:
                        server = smtplib.SMTP(cfg.smtp_host, cfg.smtp_port, timeout=20)
                        server.starttls(context=context)
                    with server:
                        if cfg.smtp_user:
                            server.login(cfg.smtp_user, cfg.smtp_password)
                        server.send_message(message)
                print(f"[mail] sent '{subject}'")
            except Exception as e:   # the player just gets no mail; the log says why
                print(f"[mail] could not send '{subject}': {type(e).__name__}: {e}")

        threading.Thread(target=deliver, daemon=True).start()

    def text_of(purpose: str, lang: str, nick: str, code: str):
        subject, body = MAILS[purpose].get(lang) or MAILS[purpose]["en"]
        return subject, body.format(nick=nick, code=code, minutes=CODE_SECONDS // 60)

    def code_hash(user_id: str, purpose: str, code: str) -> str:
        return s.token_hash(f"code:{user_id}:{purpose}:{code}")

    def new_code(con, user_id: str, purpose: str, email: str) -> str:
        code = f"{secrets.randbelow(10 ** 8):08d}"
        con.execute("INSERT OR REPLACE INTO email_codes (user_id, purpose, email, code_hash, expires_at, tries) VALUES (?, ?, ?, ?, ?, 0)",
                    (user_id, purpose, email, code_hash(user_id, purpose, code), s.now() + CODE_SECONDS))
        return code

    def take_code(con, user_id: str, purpose: str, code: str) -> Optional[str]:
        """The address the code was sent to when it is right (and it is used up), else None (a wrong try counts)."""
        row = con.execute("SELECT * FROM email_codes WHERE user_id = ? AND purpose = ?", (user_id, purpose)).fetchone()
        if row is None or row["expires_at"] < s.now() or row["tries"] >= MAX_TRIES:
            return None
        code = re.sub(r"\D", "", code or "")
        if not hmac.compare_digest(row["code_hash"], code_hash(user_id, purpose, code)):
            con.execute("UPDATE email_codes SET tries = tries + 1 WHERE user_id = ? AND purpose = ?", (user_id, purpose))
            return None
        con.execute("DELETE FROM email_codes WHERE user_id = ? AND purpose = ?", (user_id, purpose))
        return row["email"]

    def check_password(user, password: str):
        try:
            s.HASHER.verify(user["password_hash"], password or "")
        except (s.VerifyMismatchError, s.VerificationError, s.InvalidHashError):
            raise HTTPException(401, "Your password is not right.")

    def lang_of(lang: str) -> str:
        return lang if lang in MAILS["verify"] else "en"

    @app.post("/me/email")
    def set_email(body: EmailBody, request: Request, user=Depends(s.current_user)):
        s.limit(request, "password", 10, 600, user["id"])
        s.limit(request, "email-set", 5, 3600, user["id"])   # each sends a mail
        check_password(user, body.password)
        email = (body.email or "").strip()
        with s.Transaction() as con:
            if not email:
                con.execute("UPDATE users SET email = '' WHERE id = ?", (user["id"],))
                con.execute("DELETE FROM email_codes WHERE user_id = ?", (user["id"],))
                return {"ok": True, "email": "", "pending": ""}
            if len(email) > 254 or not EMAIL.match(email):
                raise HTTPException(422, "That is not an email address.")
            if not mail_ready():
                raise HTTPException(503, "Email is not set up on this server yet.")
            code = new_code(con, user["id"], "verify", email)
        send(email, *text_of("verify", lang_of(body.lang), user["nick"], code))
        print(f"[recovery] {user['nick']} asked to confirm a new email")
        return {"ok": True, "email": user["email"], "pending": email}

    @app.post("/me/email/confirm")
    def confirm_email(body: CodeBody, request: Request, user=Depends(s.current_user)):
        s.limit(request, "email-code", 20, 600, user["id"])
        with s.Transaction() as con:   # a wrong try is kept (raising in here would undo it)
            email = take_code(con, user["id"], "verify", body.code)
            if email is not None:
                con.execute("UPDATE users SET email = ? WHERE id = ?", (email, user["id"]))
        if email is None:
            raise HTTPException(422, "That code is not right (or it ran out: ask for a new one).")
        print(f"[recovery] {user['nick']} confirmed an email")
        return {"ok": True, "email": email, "pending": ""}

    @app.post("/auth/forgot")
    def forgot(body: ForgotBody, request: Request):
        s.limit(request, "forgot", 5, 600)
        if not mail_ready():
            raise HTTPException(503, "Password recovery by email is not set up on this server yet.")
        key = (body.nick or "").strip().lower()[:32]
        answer = {"ok": True, "message": "If this account has a confirmed email, a code is on its way. Look in your mail (and its spam folder)."}
        if not key or not s.LIMITS.allow(f"forgot-nick:{key}", 3, 3600):
            return answer   # the same answer: nothing tells which nicknames exist
        with s.Transaction() as con:
            user = con.execute("SELECT * FROM users WHERE nick_key = ?", (key,)).fetchone()
            if user is None or not user["email"]:
                return answer
            code = new_code(con, user["id"], "reset", user["email"])
        send(user["email"], *text_of("reset", lang_of(body.lang), user["nick"], code))
        print(f"[recovery] reset code sent for {user['nick']}")
        return answer

    @app.post("/auth/reset")
    def reset(body: ResetBody, request: Request):
        s.limit(request, "reset", 20, 600)
        problem = s.password_problem(body.new_password or "")
        if problem:
            raise HTTPException(422, problem)
        key = (body.nick or "").strip().lower()[:32]
        hashed = s.HASHER.hash(body.new_password)
        with s.Transaction() as con:   # a wrong try is kept (raising in here would undo it)
            user = con.execute("SELECT * FROM users WHERE nick_key = ?", (key,)).fetchone()
            right = user is not None and take_code(con, user["id"], "reset", body.code) is not None
            if right:
                con.execute("UPDATE users SET password_hash = ? WHERE id = ?", (hashed, user["id"]))
                con.execute("DELETE FROM sessions WHERE user_id = ?", (user["id"],))   # every device logs out
                token = s.new_session(con, user["id"])
                view = s.me_view(con, con.execute("SELECT * FROM users WHERE id = ?", (user["id"],)).fetchone())
        if not right:
            raise HTTPException(422, "That code is not right (or it ran out: ask for a new one).")
        s.LIMITS.reset(f"login-fail:{key}")
        print(f"[recovery] {user['nick']} set a new password with a code")
        return {"token": token, "user": view}

    return {"mail_ready": mail_ready}
