"""The admin panel (in the game: ADMIN in the main menu, shown to admin accounts only).

Admins are the accounts named in ADMIN_NICKS (.env, comma-separated; by default 001, pups183 and pups183mobile).
Nobody can sign up with a nickname on that list that has no account yet, so a stranger cannot take an admin's name;
to create such an account, take its name off the list first, sign up, and put it back.

    GET  /admin/player?nick=   one player's account: {nick, created_at, played, tokens, today, day_limit, owned, shop}
    POST /admin/tokens         {nick, amount}: gives tokens (a negative amount takes them; never below 0)
    POST /admin/skins          {nick, weapon, skin, owned}: gives a skin of the shop, or takes it back
    GET  /admin/log            the last 40 things admins did
    POST /admin/lan-pass       an admin's pass for LAN games (an hour): their game gives it to the LAN host it joins
    POST /admin/lan-pass/check {pass}: {admin, nick}; the LAN host asks before it carries out a guest's cheats
Everything an admin does is kept in admin_log (who, to whom, what, when) and printed in the server log.
"""
import secrets
import time

from fastapi import Depends, HTTPException, Request
from pydantic import BaseModel

SCHEMA = """
CREATE TABLE IF NOT EXISTS admin_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    at REAL NOT NULL,
    admin TEXT NOT NULL,
    target TEXT NOT NULL,
    action TEXT NOT NULL
);
"""

DEFAULT_ADMINS = "001,pups183,pups183mobile"
MAX_AMOUNT = 100000     # tokens given or taken at a time
LAN_PASS_SECONDS = 3600


class TokensBody(BaseModel):
    nick: str = ""
    amount: int = 0


class SkinBody(BaseModel):
    nick: str = ""
    weapon: str = ""
    skin: str = ""
    owned: bool = True


def admin_nicks(text: str) -> set:
    return {n.strip().lower() for n in (text or "").split(",") if n.strip()}


def install(app, s, tokens_module, token_view):
    def is_admin(user) -> bool:
        return user is not None and user["nick_key"] in s.CONFIG.admin_nicks

    def admin(request: Request, user=Depends(s.current_user)):
        if not is_admin(user):
            raise HTTPException(403, "Only admins can do that.")
        s.limit(request, "admin", 600, 3600, key=user["id"])
        return user

    def target(con, nick: str):
        row = con.execute("SELECT * FROM users WHERE nick_key = ?", ((nick or "").strip().lower()[:32],)).fetchone()
        if row is None:
            raise HTTPException(404, "No player has that nickname.")
        return row

    def player(con, row) -> dict:
        return dict(token_view(con, row["id"]), nick=row["nick"], created_at=row["created_at"],
                    played=int(row["played_seconds"]))

    def log(con, user, row, action: str):
        con.execute("INSERT INTO admin_log (at, admin, target, action) VALUES (?, ?, ?, ?)",
                    (s.now(), user["nick"], row["nick"], action))
        print(f"[admin] {user['nick']} -> {row['nick']}: {action}")

    @app.get("/admin/player")
    def get_player(nick: str = "", user=Depends(admin)):
        with s.DB_LOCK:
            return player(s.DB, target(s.DB, nick))

    @app.post("/admin/tokens")
    def give_tokens(body: TokensBody, user=Depends(admin)):
        if body.amount == 0 or abs(body.amount) > MAX_AMOUNT:
            raise HTTPException(422, f"Give or take 1 to {MAX_AMOUNT} tokens at a time.")
        with s.Transaction() as con:
            row = target(con, body.nick)
            before = con.execute("SELECT halloween_tokens FROM users WHERE id = ?", (row["id"],)).fetchone()["halloween_tokens"]
            after = max(0, before + body.amount)
            con.execute("UPDATE users SET halloween_tokens = ? WHERE id = ?", (after, row["id"]))
            log(con, user, row, f"{'+' if body.amount > 0 else ''}{body.amount} tokens ({before} -> {after})")
            return player(con, row)

    @app.post("/admin/skins")
    def give_skin(body: SkinBody, user=Depends(admin)):
        if (body.weapon, body.skin) not in tokens_module.SHOP:
            raise HTTPException(404, "That skin is not for sale.")
        with s.Transaction() as con:
            row = target(con, body.nick)
            if body.owned:
                added = con.execute("INSERT OR IGNORE INTO owned_skins (user_id, weapon, skin, price, bought_at) VALUES (?, ?, ?, 0, ?)",
                                    (row["id"], body.weapon, body.skin, s.now())).rowcount
                if added:
                    log(con, user, row, f"gave {body.weapon}/{body.skin}")
            elif con.execute("DELETE FROM owned_skins WHERE user_id = ? AND weapon = ? AND skin = ?",
                             (row["id"], body.weapon, body.skin)).rowcount:
                log(con, user, row, f"took {body.weapon}/{body.skin}")
            return player(con, row)

    # LAN passes live in memory only: a restarted server forgets them, and the games simply ask for new ones.
    passes = {}   # pass -> (nick, expires)

    @app.post("/admin/lan-pass")
    def lan_pass(user=Depends(admin)):
        now = time.time()
        for old in [p for p, (_, expires) in passes.items() if expires < now]:
            del passes[old]
        code = secrets.token_urlsafe(18)
        passes[code] = (user["nick"], now + LAN_PASS_SECONDS)
        return {"pass": code, "seconds": LAN_PASS_SECONDS}

    @app.post("/admin/lan-pass/check")
    async def lan_pass_check(request: Request):
        # No login needed (the LAN host may play as a guest), but limited: a pass cannot be guessed by trying.
        s.limit(request, "lan-pass-check", 120, 3600)
        try:
            code = str((await request.json()).get("pass", ""))[:64]
        except Exception:
            code = ""
        found = passes.get(code)
        if found is None or found[1] < time.time():
            return {"admin": False, "nick": ""}
        return {"admin": True, "nick": found[0]}

    @app.get("/admin/log")
    def get_log(user=Depends(admin)):
        with s.DB_LOCK:
            rows = s.DB.execute("SELECT at, admin, target, action FROM admin_log ORDER BY id DESC LIMIT 40").fetchall()
        return {"entries": [dict(r) for r in rows]}

    return {"is_admin": is_admin}
