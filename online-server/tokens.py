"""Tokens: the game's money, kept on the online account, for buying the skins in SHOP below.

    GET  /tokens          {tokens, owned: ["ak47/Halloween", ...], shop: [{weapon, skin, price}], today, day_limit}
    POST /tokens/match    a match the player's own game ran (against bots, or a LAN game): {kills, won, rounds, seconds}
                          pays PER_KILL a kill and PER_WIN for a win, at most DAY_LIMIT a day from such matches
                          (the game reports them itself, so they are capped)
    POST /tokens/buy      {weapon, skin}: buys a skin of the shop, once
An online room's match is reported by its game host (the heartbeat at the end, with each player's kills): the same pay,
without the daily cap. The prices are kept here; the game only shows them.
"""
import time
from typing import Optional

from fastapi import Depends, HTTPException, Request
from pydantic import BaseModel

SCHEMA = """
ALTER TABLE users ADD COLUMN halloween_tokens INTEGER NOT NULL DEFAULT 0;
CREATE TABLE IF NOT EXISTS owned_skins (
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    weapon TEXT NOT NULL,
    skin TEXT NOT NULL,
    price INTEGER NOT NULL,
    bought_at REAL NOT NULL,
    PRIMARY KEY (user_id, weapon, skin)
);
CREATE TABLE IF NOT EXISTS token_days (
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    day TEXT NOT NULL,
    earned INTEGER NOT NULL DEFAULT 0,
    PRIMARY KEY (user_id, day)
);
"""

# (weapon id, skin folder) -> price in Tokens. Skins not listed are free.
SHOP = {
    ("ak47", "Halloween"): 100,
    ("m4a1", "Halloween"): 150,
    ("knife", "Classic Halloween"): 500,
    ("knife", "karambit"): 400,
    ("knife", "nogektestskin"): 1000,
    ("deagle", "digle"): 750,
    ("rpg", "Web"): 400,
    ("awp", "AWPOLD"): 750,
}
# The neon wraps (1.20.1): four colours for every weapon, 500 Tokens each.
NEON_WEAPONS = ("knife", "glock", "usp", "tec9", "m1911", "deagle", "uzi", "mp5", "shotgun", "ak47", "m4a1", "awp", "rpg", "svd")
SHOP.update({(weapon, f"Neon {colour}"): 500 for weapon in NEON_WEAPONS for colour in ("Cyan", "Pink", "Green", "Yellow")})
PER_KILL = 1
PER_WIN = 5
DAY_LIMIT = 40          # tokens a day from matches the player's own game reports
MAX_KILLS = 60          # in one match


class MatchReport(BaseModel):
    kills: int = 0
    won: bool = False
    rounds: int = 0
    seconds: float = 0


class BuyBody(BaseModel):
    weapon: str = ""
    skin: str = ""


def pay(kills: int, won: bool) -> int:
    return max(0, min(kills, MAX_KILLS)) * PER_KILL + (PER_WIN if won else 0)


def install(app, s):
    def today() -> str:
        return time.strftime("%Y-%m-%d", time.gmtime())

    def view(con, user_id: str) -> dict:
        tokens = con.execute("SELECT halloween_tokens FROM users WHERE id = ?", (user_id,)).fetchone()["halloween_tokens"]
        owned = [f"{r['weapon']}/{r['skin']}" for r in
                 con.execute("SELECT weapon, skin FROM owned_skins WHERE user_id = ? ORDER BY bought_at", (user_id,))]
        day = con.execute("SELECT earned FROM token_days WHERE user_id = ? AND day = ?", (user_id, today())).fetchone()
        return {"tokens": tokens, "owned": owned, "today": day["earned"] if day else 0, "day_limit": DAY_LIMIT,
                "shop": [{"weapon": w, "skin": k, "price": p} for (w, k), p in SHOP.items()]}

    def give(con, user_id: str, amount: int, capped: bool) -> int:
        """Adds tokens (capped: no more than what is left of today's limit); returns how many."""
        if capped:
            day = con.execute("SELECT earned FROM token_days WHERE user_id = ? AND day = ?", (user_id, today())).fetchone()
            amount = min(amount, DAY_LIMIT - (day["earned"] if day else 0))
        if amount <= 0:
            return 0
        con.execute("UPDATE users SET halloween_tokens = halloween_tokens + ? WHERE id = ?", (amount, user_id))
        if capped:
            con.execute("INSERT INTO token_days (user_id, day, earned) VALUES (?, ?, ?) "
                        "ON CONFLICT(user_id, day) DO UPDATE SET earned = earned + excluded.earned", (user_id, today(), amount))
        return amount

    @app.get("/tokens")
    def get_tokens(user=Depends(s.current_user)):
        with s.DB_LOCK:
            return view(s.DB, user["id"])

    @app.post("/tokens/match")
    def own_match(body: MatchReport, request: Request, user=Depends(s.current_user)):
        if not 1 <= body.rounds <= 40 or not 0 <= body.kills <= MAX_KILLS or body.seconds < body.rounds * 15:
            raise HTTPException(422, "That match cannot have happened.")
        s.limit(request, "tokens-match", 12, 3600, key=user["id"])
        with s.Transaction() as con:
            earned = give(con, user["id"], pay(body.kills, body.won), capped=True)
            return dict(view(con, user["id"]), earned=earned)

    @app.post("/tokens/buy")
    def buy(body: BuyBody, request: Request, user=Depends(s.current_user)):
        price = SHOP.get((body.weapon, body.skin))
        if price is None:
            raise HTTPException(404, "That skin is not for sale.")
        s.limit(request, "tokens-buy", 30, 3600, key=user["id"])
        with s.Transaction() as con:
            if con.execute("SELECT 1 FROM owned_skins WHERE user_id = ? AND weapon = ? AND skin = ?",
                           (user["id"], body.weapon, body.skin)).fetchone():
                return view(con, user["id"])
            tokens = con.execute("SELECT halloween_tokens FROM users WHERE id = ?", (user["id"],)).fetchone()["halloween_tokens"]
            if tokens < price:
                raise HTTPException(409, "Not enough Tokens.")
            con.execute("UPDATE users SET halloween_tokens = halloween_tokens - ? WHERE id = ?", (price, user["id"]))
            con.execute("INSERT INTO owned_skins (user_id, weapon, skin, price, bought_at) VALUES (?, ?, ?, ?, ?)",
                        (user["id"], body.weapon, body.skin, price, s.now()))
            print(f"[tokens] {user['nick']} bought {body.weapon}/{body.skin} for {price}")
            return view(con, user["id"])

    def room_match_over(con, players, terrorist_wins: int, swat_wins: int):
        """An online room's match ended (reported once by its game host): every player is paid for it."""
        winner = "t" if terrorist_wins > swat_wins else "swat" if swat_wins > terrorist_wins else ""
        for p in players:
            if p.team in ("t", "swat") and con.execute("SELECT 1 FROM users WHERE id = ?", (p.user_id,)).fetchone():
                give(con, p.user_id, pay(p.kills, p.team == winner), capped=False)

    return {"room_match_over": room_match_over, "view": view}
