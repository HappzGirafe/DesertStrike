"""Player styles: how each player plays each map, for the bots that copy real players (the game's PlayerTactics).

An online room's game host follows its players every round (the way they walk and when, the site they go for, where
and how they hold, what they start the round with, where they get kills from), adds that to each player's style and
sends the styles here after the match. A style is kept per account and map; only the game hosts write them.

    GET  /internal/tactics/{map}?users=a,b   the game hosts: those accounts' styles ("players") and up to 8 others ("others")
    POST /internal/tactics                   the game hosts: {map, players: [{user_id, data}]} after a match
    GET  /tactics/{map}                      public: up to 8 styles (no names), for the bots of games elsewhere

A style ("data"), as the game writes it (Unity's JsonUtility, so flat lists of numbers):
    {v, t: {...}, swat: {...}}, each side {rounds, siteA, siteB, paths: [{site, points: [x, y, z, seconds, ...],
    lookX, lookZ, hold, crouch}], buys: [weapon index, rounds, ...], armor, helmet, kit,
    kills: [x, y, z, dx, dz, ...], killCount, headshots}
"""
import hmac
import json
import math
import threading
import time
from typing import List

from fastapi import HTTPException, Request
from fastapi.responses import JSONResponse
from pydantic import BaseModel

MAX_PATHS, MAX_POINTS, MAX_KILLS, MAX_BUYS = 8, 32, 16, 20
SAMPLE = 8                # styles in an answer for the bots
SAMPLE_DAYS = 60          # only styles played within this long

SCHEMA = """
CREATE TABLE IF NOT EXISTS player_tactics (
    user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    map TEXT NOT NULL,
    data TEXT NOT NULL,
    rounds INTEGER NOT NULL,
    updated_at REAL NOT NULL,
    PRIMARY KEY (user_id, map)
);
CREATE INDEX IF NOT EXISTS player_tactics_map ON player_tactics (map, updated_at);
"""


class Entry(BaseModel):
    user_id: str = ""
    data: dict = {}


class Upload(BaseModel):
    map: str = ""
    players: List[Entry] = []


def _number(v, low, high) -> float:
    if isinstance(v, bool) or not isinstance(v, (int, float)) or not math.isfinite(v):
        raise ValueError
    return float(min(max(v, low), high))


def _count(v) -> int:
    return int(_number(v, 0, 1_000_000))


def _floats(raw, group: int, most: int, limits) -> list:
    if not isinstance(raw, list) or len(raw) % group != 0:
        raise ValueError
    raw = raw[:most * group]
    return [round(_number(v, *limits[i % group]), 2) for i, v in enumerate(raw)]


POINT_LIMITS = [(-2000, 2000), (-200, 200), (-2000, 2000), (0, 300)]
KILL_LIMITS = [(-2000, 2000), (-200, 200), (-2000, 2000), (-1, 1), (-1, 1)]


def _side(raw) -> dict:
    if not isinstance(raw, dict):
        raw = {}
    paths = []
    for p in (raw.get("paths") or [])[:MAX_PATHS]:
        if not isinstance(p, dict):
            raise ValueError
        points = _floats(p.get("points", []), 4, MAX_POINTS, POINT_LIMITS)
        if len(points) < 8:
            continue
        site = p.get("site", "")
        paths.append({"site": site if site in ("A", "B") else "", "points": points,
                      "lookX": round(_number(p.get("lookX", 0), -1, 1), 2), "lookZ": round(_number(p.get("lookZ", 0), -1, 1), 2),
                      "hold": round(_number(p.get("hold", 0), 0, 120), 2), "crouch": bool(p.get("crouch", False))})
    buys = raw.get("buys") or []
    if not isinstance(buys, list) or len(buys) % 2 != 0:
        raise ValueError
    buys = [int(_number(v, 0, 255 if i % 2 == 0 else 1_000_000)) for i, v in enumerate(buys[:MAX_BUYS * 2])]
    return {"rounds": _count(raw.get("rounds", 0)), "siteA": _count(raw.get("siteA", 0)), "siteB": _count(raw.get("siteB", 0)),
            "paths": paths, "buys": buys, "armor": _count(raw.get("armor", 0)), "helmet": _count(raw.get("helmet", 0)),
            "kit": _count(raw.get("kit", 0)), "kills": _floats(raw.get("kills") or [], 5, MAX_KILLS, KILL_LIMITS),
            "killCount": _count(raw.get("killCount", 0)), "headshots": _count(raw.get("headshots", 0))}


def clean(data: dict) -> dict:
    """A style with only what the game knows, every number in its range; ValueError if it is not a style."""
    if not isinstance(data, dict):
        raise ValueError
    return {"v": 1, "t": _side(data.get("t")), "swat": _side(data.get("swat"))}


def install(app, internal, s):
    """Adds the routes. `s` is the server module (database, accounts, limits)."""
    cache = {}                # map -> (monotonic time, styles)
    lock = threading.Lock()

    def internal_key(request: Request):
        if not hmac.compare_digest(request.headers.get("x-internal-key", ""), s.CONFIG.internal_key()):
            raise HTTPException(403, "wrong key")

    def check_map(map_id: str):
        if map_id not in s.MAPS:
            raise HTTPException(404, "Unknown map.")

    def sample(map_id: str, leave_out=()) -> list:
        """Styles of players who played the map lately, the ones with the most rounds first."""
        with lock:
            hit = cache.get(map_id)
        if not hit or time.monotonic() - hit[0] > 60:
            with s.DB_LOCK:
                rows = s.DB.execute("SELECT user_id, data FROM player_tactics WHERE map = ? AND updated_at > ? "
                                    "ORDER BY rounds DESC, updated_at DESC LIMIT ?",
                                    (map_id, s.now() - SAMPLE_DAYS * 86400, SAMPLE * 3)).fetchall()
            hit = (time.monotonic(), [(r["user_id"], json.loads(r["data"])) for r in rows])
            with lock:
                cache[map_id] = hit
        return [data for user_id, data in hit[1] if user_id not in leave_out][:SAMPLE]

    @internal.get("/internal/tactics/{map_id}")
    def get_internal(map_id: str, request: Request, users: str = ""):
        internal_key(request)
        check_map(map_id)
        wanted = [u for u in users.split(",") if u][:20]
        players = []
        if wanted:
            with s.DB_LOCK:
                rows = s.DB.execute(f"SELECT user_id, data FROM player_tactics WHERE map = ? AND user_id IN ({','.join('?' * len(wanted))})",
                                    (map_id, *wanted)).fetchall()
            players = [{"user_id": r["user_id"], "data": json.loads(r["data"])} for r in rows]
        return {"map": map_id, "players": players, "others": sample(map_id, set(wanted))}

    @internal.post("/internal/tactics")
    def post_internal(body: Upload, request: Request):
        internal_key(request)
        check_map(body.map)
        if len(body.players) > 40:
            raise HTTPException(422, "Too many players.")
        kept = 0
        t = s.now()
        with s.Transaction() as con:
            for entry in body.players:
                try:
                    data = clean(entry.data)
                except (ValueError, TypeError, AttributeError):
                    continue
                if con.execute("SELECT 1 FROM users WHERE id = ?", (entry.user_id,)).fetchone() is None:
                    continue
                rounds = data["t"]["rounds"] + data["swat"]["rounds"]
                con.execute("INSERT OR REPLACE INTO player_tactics VALUES (?, ?, ?, ?, ?)",
                            (entry.user_id, body.map, json.dumps(data, separators=(",", ":")), rounds, t))
                kept += 1
        with lock:
            cache.pop(body.map, None)
        if kept:
            print(f"[bots] kept the styles of {kept} player(s) on {body.map}")
        return {"ok": True, "kept": kept}

    @app.get("/tactics/{map_id}")
    def get_public(map_id: str, request: Request):
        s.limit(request, "tactics-get", 60, 60)
        check_map(map_id)
        return JSONResponse({"map": map_id, "players": [], "others": sample(map_id)}, headers={"Cache-Control": "public, max-age=60"})

    return {"clean": clean}
