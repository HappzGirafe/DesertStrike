"""Bot knowledge: what real players do on each map, gathered from every game and given to the bots of every game.

A game that runs a match (an online room's game host, a LAN host, single player with an online account) counts its
people, per team, in 3 m cells of the map: where they stood ("stand") and where they got a kill from ("kill", with the
summed direction they shot in). After the match it sends those counts here; one table keeps them for all games. Each
cell's weight halves every HALF_LIFE_DAYS days, so what people do now counts most.

    GET  /bot-knowledge/{map}               public: the strongest cells, weights 0..1 (games cache it)
    POST /bot-knowledge                     a logged-in player's game (counts half: anyone could send anything)
    POST /internal/bot-knowledge            the online game hosts
    GET  /internal/bot-knowledge/{map}      the same answer as the public one, for the game hosts

The cells go as flat lists of numbers (Unity's JsonUtility reads no nested lists):
    upload  cells: team, kind, cx, cy, cz, weight, dx, dz, team, kind, ...   (team 0 Terrorists / 1 SWAT, kind 0 stand / 1 kill)
    answer  stand: team, cx, cy, cz, weight, ...     kill: team, cx, cy, cz, weight, dx, dz, ...
"""
import hmac
import math
import threading
import time
from typing import List

from fastapi import Depends, HTTPException, Request
from fastapi.responses import JSONResponse
from pydantic import BaseModel

CELL = 3.0                # metres across a cell
LAYER = 2.5               # metres high
HALF_LIFE_DAYS = 14.0
MAX_CELLS = 300           # cells in one upload
STAND_TOP, KILL_TOP = 400, 200   # cells per team in an answer

SCHEMA = """
CREATE TABLE IF NOT EXISTS bot_knowledge (
    map TEXT NOT NULL,
    team INTEGER NOT NULL,
    kind INTEGER NOT NULL,
    cx INTEGER NOT NULL,
    cy INTEGER NOT NULL,
    cz INTEGER NOT NULL,
    weight REAL NOT NULL,
    dx REAL NOT NULL,
    dz REAL NOT NULL,
    updated_at REAL NOT NULL,
    PRIMARY KEY (map, team, kind, cx, cy, cz)
);
CREATE TABLE IF NOT EXISTS bot_knowledge_matches (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    map TEXT NOT NULL,
    source TEXT NOT NULL,
    mode TEXT NOT NULL,
    cells INTEGER NOT NULL,
    at REAL NOT NULL
);
"""


class Upload(BaseModel):
    map: str = ""
    mode: str = ""            # "online", "lan" or "solo"
    cells: List[float] = []


def decay(age_seconds: float) -> float:
    return 0.5 ** (max(0.0, age_seconds) / (HALF_LIFE_DAYS * 86400.0))


def check_cells(raw: List[float]) -> list:
    if len(raw) % 8 != 0 or len(raw) // 8 > MAX_CELLS:
        raise HTTPException(422, "Bad bot knowledge.")
    cells = []
    for i in range(0, len(raw), 8):
        team, kind, cx, cy, cz, weight, dx, dz = raw[i:i + 8]
        if not all(math.isfinite(v) for v in (team, kind, cx, cy, cz, weight, dx, dz)):
            raise HTTPException(422, "Bad bot knowledge.")
        team, kind, cx, cy, cz = int(team), int(kind), int(cx), int(cy), int(cz)
        cap = 30.0 if kind == 1 else 150.0   # one match: at most this many kills / samples from one cell
        if team not in (0, 1) or kind not in (0, 1) or abs(cx) > 1000 or abs(cz) > 1000 or abs(cy) > 100 or not 0 < weight <= cap:
            raise HTTPException(422, "Bad bot knowledge.")
        length = math.hypot(dx, dz)
        if length > weight * 1.01 + 1e-6:     # a sum of unit directions is never longer than their count
            dx, dz = dx / length * weight, dz / length * weight
        cells.append((team, kind, cx, cy, cz, weight, dx if kind == 1 else 0.0, dz if kind == 1 else 0.0))
    return cells


def install(app, internal, s):
    """Adds the routes. `s` is the server module (database, accounts, limits)."""
    cache = {}                # map -> (monotonic time, answer)
    lock = threading.Lock()

    def add(map_id: str, cells: list, scale: float, source: str, mode: str):
        t = s.now()
        with s.Transaction() as con:
            for team, kind, cx, cy, cz, weight, dx, dz in cells:
                row = con.execute("SELECT weight, dx, dz, updated_at FROM bot_knowledge WHERE map = ? AND team = ? AND kind = ? "
                                  "AND cx = ? AND cy = ? AND cz = ?", (map_id, team, kind, cx, cy, cz)).fetchone()
                keep = decay(t - row["updated_at"]) if row else 0.0
                old_w, old_dx, old_dz = (row["weight"], row["dx"], row["dz"]) if row else (0.0, 0.0, 0.0)
                con.execute("INSERT OR REPLACE INTO bot_knowledge VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                            (map_id, team, kind, cx, cy, cz, old_w * keep + weight * scale, old_dx * keep + dx * scale,
                             old_dz * keep + dz * scale, t))
            con.execute("INSERT INTO bot_knowledge_matches (map, source, mode, cells, at) VALUES (?, ?, ?, ?, ?)",
                        (map_id, source, mode[:16], len(cells), t))
        with lock:
            cache.pop(map_id, None)
        print(f"[bots] learned from a {mode or '?'} match on {map_id} ({source}): {len(cells)} cells")

    def answer(map_id: str) -> dict:
        if map_id not in s.MAPS:
            raise HTTPException(404, "Unknown map.")
        with lock:
            hit = cache.get(map_id)
            if hit and time.monotonic() - hit[0] < 60:
                return hit[1]
        t = s.now()
        with s.DB_LOCK:
            rows = s.DB.execute("SELECT * FROM bot_knowledge WHERE map = ?", (map_id,)).fetchall()
            matches = s.DB.execute("SELECT COUNT(*) AS n FROM bot_knowledge_matches WHERE map = ?", (map_id,)).fetchone()["n"]
        groups = {}
        for r in rows:
            w = r["weight"] * decay(t - r["updated_at"])
            if w < 0.05:
                continue
            groups.setdefault((r["team"], r["kind"]), []).append((w, r))
        stand, kill = [], []
        for (team, kind), items in groups.items():
            items.sort(key=lambda item: -item[0])
            items = items[:KILL_TOP if kind == 1 else STAND_TOP]
            top = items[0][0]
            for w, r in items:
                if kind == 0:
                    stand += [team, r["cx"], r["cy"], r["cz"], round(w / top, 3)]
                else:
                    length = math.hypot(r["dx"], r["dz"]) or 1.0
                    kill += [team, r["cx"], r["cy"], r["cz"], round(w / top, 3), round(r["dx"] / length, 3), round(r["dz"] / length, 3)]
        body = {"map": map_id, "cell": CELL, "layer": LAYER, "matches": matches, "stand": stand, "kill": kill}
        with lock:
            cache[map_id] = (time.monotonic(), body)
        return body

    @app.get("/bot-knowledge/{map_id}")
    def get_knowledge(map_id: str, request: Request):
        s.limit(request, "knowledge-get", 60, 60)
        return JSONResponse(answer(map_id), headers={"Cache-Control": "public, max-age=60"})

    @app.post("/bot-knowledge")
    def post_knowledge(body: Upload, request: Request, user=Depends(s.current_user)):
        if body.map not in s.MAPS:
            raise HTTPException(422, "Unknown map.")
        cells = check_cells(body.cells)
        s.limit(request, "knowledge-post", 1, 50, user["id"])   # one match's counts a minute at most
        s.limit(request, "knowledge-day", 40, 86400, user["id"])
        if cells:
            add(body.map, cells, 0.5, "player", body.mode if body.mode in ("lan", "solo") else "solo")
        return {"ok": True, "cells": len(cells)}

    def internal_key(request: Request):
        if not hmac.compare_digest(request.headers.get("x-internal-key", ""), s.CONFIG.internal_key()):
            raise HTTPException(403, "wrong key")

    @internal.post("/internal/bot-knowledge")
    def post_internal(body: Upload, request: Request):
        internal_key(request)
        if body.map not in s.MAPS:
            raise HTTPException(422, "Unknown map.")
        cells = check_cells(body.cells)
        if cells:
            add(body.map, cells, 1.0, "server", "online")
        return {"ok": True, "cells": len(cells)}

    @internal.get("/internal/bot-knowledge/{map_id}")
    def get_internal(map_id: str, request: Request):
        internal_key(request)
        return answer(map_id)

    def forget_faded(con):
        """Upkeep: cells that have faded to almost nothing go."""
        t = s.now()
        con.execute("DELETE FROM bot_knowledge WHERE weight * ? < 0.02 AND updated_at < ?", (decay(60 * 86400), t - 60 * 86400))
        con.execute("DELETE FROM bot_knowledge_matches WHERE at < ?", (t - 365 * 86400,))

    return {"forget_faded": forget_faded}
