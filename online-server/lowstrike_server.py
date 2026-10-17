"""Low Strike online server: accounts, friends, presence, rooms and join tickets.

One process, two HTTP apps:

* public   API_HOST:API_PORT (127.0.0.1:8000). The internet reaches it only through Cloudflare Tunnel
           (https://api.<DOMAIN>).
* internal 127.0.0.1:INTERNAL_PORT (8001). Only for the Unity game hosts on this PC: they report their state every
           second and get their room's settings back. Never route the tunnel to this port.

When UNITY_SERVER_EXECUTABLE is set, it also starts one Unity game host per room slot (ROOM_SLOTS_JSON) and restarts
any that stops or hangs.

Run:  python lowstrike_server.py                     (reads .env next to this file; logs to the console)
      pythonw lowstrike_server.py --log logs/api.log  (no window; what scripts/start.ps1 does)
"""

import asyncio
import base64
import hashlib
import hmac
import json
import os
import re
import secrets
import shlex
import socket
import sqlite3
import subprocess
import sys
import threading
import time
import uuid
from collections import defaultdict, deque
from contextlib import asynccontextmanager
from datetime import datetime, timezone
from pathlib import Path
from typing import Optional

import uvicorn
from argon2 import PasswordHasher
from argon2.exceptions import InvalidHashError, VerificationError, VerifyMismatchError
from fastapi import Depends, FastAPI, HTTPException, Request
from fastapi.exceptions import RequestValidationError
from fastapi.responses import JSONResponse
from pydantic import BaseModel

HERE = Path(__file__).resolve().parent
PROTOCOL_VERSION = 5          # NetSession.ProtocolVersion of the game build these hosts run
MAX_BODY = 16 * 1024          # bytes in one request
MAX_PROFILE = 8 * 1024        # bytes of saved skins and settings per account
MAX_FRIENDS = 100
ONLINE_SECONDS = 15           # the game asks every 4 s while it is open; silent this long = offline
SLOT_ALIVE_SECONDS = 10       # a game host reports every second
SLOT_LOST_SECONDS = 45        # a room whose host has been silent this long is closed
INVITE_SECONDS = 30 * 60
TICKET_SECONDS = 120
MAPS = ("dune", "village", "halloween", "industrial")
DIFFICULTIES = ("Easy", "Normal", "Hard", "Extreme")
ROUNDS = (3, 5, 8, 16)
TEAMS = ("t", "swat", "spectator")
MAX_SPECTATORS = 4
NICK = re.compile(r"^[A-Za-z0-9_-]{3,16}$")
CODE_LETTERS = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"   # no 0/O or 1/I


# ----------------------------------------------------------------- configuration

def read_env_file(path: Path) -> dict:
    values = {}
    if path.exists():
        for line in path.read_text(encoding="utf-8").splitlines():
            line = line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            key, value = line.split("=", 1)
            value = value.strip()
            if len(value) >= 2 and value[0] == value[-1] and value[0] in "'\"":
                value = value[1:-1]
            values[key.strip()] = value
    return values


class Config:
    def __init__(self):
        env = read_env_file(Path(os.environ.get("LOWSTRIKE_ENV", HERE / ".env")))
        env.update({k: v for k, v in os.environ.items() if k in env or k.startswith(("API_", "LOWSTRIKE_", "GAME_", "ROOM_", "UNITY_"))
                    or k in ("PUBLIC_API_URL", "DATABASE_PATH", "AUTH_SECRET", "INTERNAL_PORT", "SESSION_IDLE_DAYS", "SESSION_MAX_DAYS")})
        self.domain = env.get("LOWSTRIKE_DOMAIN", "")
        self.public_api_url = env.get("PUBLIC_API_URL", "")
        self.api_host = env.get("API_HOST", "127.0.0.1")
        self.api_port = int(env.get("API_PORT", "8000"))
        self.internal_port = int(env.get("INTERNAL_PORT", "8001"))
        db = env.get("DATABASE_PATH", "./data/lowstrike.sqlite3")
        self.database_path = Path(db) if os.path.isabs(db) else (HERE / db).resolve()
        self.auth_secret = env.get("AUTH_SECRET", "")
        self.join_secret = env.get("GAME_JOIN_SECRET", "")
        self.game_version = env.get("GAME_BUILD_VERSION", "")
        if self.game_version.upper().startswith("REPLACE"):
            self.game_version = ""
        self.session_idle = float(env.get("SESSION_IDLE_DAYS", "7")) * 86400
        self.session_max = float(env.get("SESSION_MAX_DAYS", "30")) * 86400
        self.unity_exe = env.get("UNITY_SERVER_EXECUTABLE", "")
        if self.unity_exe.upper().startswith("PATH_TO") or self.unity_exe.upper().startswith("REPLACE"):
            self.unity_exe = ""
        self.unity_args = env.get("UNITY_SERVER_ARGS", "-batchmode -nographics")
        self.slots = self.parse_slots(env.get("ROOM_SLOTS_JSON", "[]"))

    @staticmethod
    def parse_slots(text: str) -> list:
        try:
            slots = json.loads(text)
        except json.JSONDecodeError as e:
            sys.exit(f"ROOM_SLOTS_JSON is not valid JSON: {e}")
        if not isinstance(slots, list):
            sys.exit("ROOM_SLOTS_JSON must be a list of slots")
        result, seen_ids, seen_ports = [], set(), set()
        for s in slots:
            try:
                slot = {"id": str(s["id"]), "local_udp_port": int(s["local_udp_port"]),
                        "public_udp_host": str(s["public_udp_host"]), "public_udp_port": int(s["public_udp_port"])}
            except (KeyError, TypeError, ValueError):
                sys.exit("Every slot in ROOM_SLOTS_JSON needs id, local_udp_port, public_udp_host and public_udp_port")
            if not re.match(r"^[A-Za-z0-9_-]{1,32}$", slot["id"]) or slot["id"] in seen_ids:
                sys.exit(f"Slot id {slot['id']!r}: letters, digits, _ or -, and different for every slot")
            if slot["local_udp_port"] in seen_ports:
                sys.exit(f"Two slots use local UDP port {slot['local_udp_port']}")
            seen_ids.add(slot["id"])
            seen_ports.add(slot["local_udp_port"])
            result.append(slot)
        return result

    def check(self):
        problems = []
        for name, value in (("AUTH_SECRET", self.auth_secret), ("GAME_JOIN_SECRET", self.join_secret)):
            if len(value) < 32 or "REPLACE" in value.upper():
                problems.append(f"{name} must be a long random secret (at least 32 characters); make one with: "
                                f"python -c \"import secrets; print(secrets.token_urlsafe(48))\"")
        if self.auth_secret and self.auth_secret == self.join_secret:
            problems.append("AUTH_SECRET and GAME_JOIN_SECRET must be different")
        if self.api_host not in ("127.0.0.1", "localhost", "::1"):
            problems.append("API_HOST must be 127.0.0.1: the internet reaches the API through Cloudflare Tunnel only")
        if self.internal_port == self.api_port:
            problems.append("INTERNAL_PORT must differ from API_PORT")
        if not self.slots:
            print("[config] ROOM_SLOTS_JSON has no slots: accounts and friends work, but no online rooms can be made")
        for slot in self.slots:
            if "YOUR_" in slot["public_udp_host"].upper() or "REPLACE" in slot["public_udp_host"].upper():
                print(f"[config] Slot {slot['id']}: public_udp_host is still the example; players cannot reach it")
        if problems:
            sys.exit("\n".join(["Fix online-server/.env:"] + ["  - " + p for p in problems]))

    def internal_key(self) -> str:
        # What the game hosts send to the internal API (they compute it from GAME_JOIN_SECRET as well).
        return hmac.new(self.join_secret.encode(), b"lowstrike-internal", hashlib.sha256).hexdigest()


CONFIG = Config()


# ----------------------------------------------------------------- database

MIGRATIONS = [
    """
    CREATE TABLE users (
        id TEXT PRIMARY KEY,
        nick TEXT NOT NULL,
        nick_key TEXT NOT NULL UNIQUE,
        password_hash TEXT NOT NULL,
        hide_status INTEGER NOT NULL DEFAULT 0,
        created_at TEXT NOT NULL,
        last_seen REAL NOT NULL DEFAULT 0
    );
    CREATE TABLE sessions (
        token_hash TEXT PRIMARY KEY,
        user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
        created_at REAL NOT NULL,
        last_used REAL NOT NULL,
        expires_at REAL NOT NULL
    );
    CREATE INDEX sessions_user ON sessions(user_id);
    CREATE TABLE profiles (
        user_id TEXT PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
        data TEXT NOT NULL DEFAULT '{}',
        linked_local INTEGER NOT NULL DEFAULT 0,
        updated_at REAL NOT NULL
    );
    CREATE TABLE friend_requests (
        id TEXT PRIMARY KEY,
        from_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
        to_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
        created_at REAL NOT NULL,
        UNIQUE(from_id, to_id)
    );
    CREATE TABLE friendships (
        user_a TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
        user_b TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
        created_at REAL NOT NULL,
        PRIMARY KEY(user_a, user_b),
        CHECK(user_a < user_b)
    );
    CREATE INDEX friendships_b ON friendships(user_b);
    CREATE TABLE rooms (
        id TEXT PRIMARY KEY,
        slot_id TEXT NOT NULL,
        name TEXT NOT NULL,
        owner_id TEXT REFERENCES users(id) ON DELETE SET NULL,
        map TEXT NOT NULL,
        team_size INTEGER NOT NULL,
        difficulty TEXT NOT NULL,
        rounds INTEGER NOT NULL,
        friendly_fire INTEGER NOT NULL,
        private INTEGER NOT NULL,
        code TEXT NOT NULL,
        status TEXT NOT NULL,
        start_requested INTEGER NOT NULL DEFAULT 0,
        created_at REAL NOT NULL,
        updated_at REAL NOT NULL,
        closed_at REAL
    );
    CREATE INDEX rooms_status ON rooms(status);
    CREATE UNIQUE INDEX rooms_open_code ON rooms(code) WHERE status != 'closed';
    CREATE UNIQUE INDEX rooms_open_slot ON rooms(slot_id) WHERE status != 'closed';
    CREATE TABLE room_members (
        room_id TEXT NOT NULL REFERENCES rooms(id) ON DELETE CASCADE,
        user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
        team TEXT NOT NULL,
        joined_at REAL NOT NULL,
        connected INTEGER NOT NULL DEFAULT 0,
        PRIMARY KEY(room_id, user_id)
    );
    CREATE INDEX room_members_user ON room_members(user_id);
    CREATE TABLE invites (
        id TEXT PRIMARY KEY,
        room_id TEXT NOT NULL REFERENCES rooms(id) ON DELETE CASCADE,
        from_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
        to_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
        created_at REAL NOT NULL,
        expires_at REAL NOT NULL,
        status TEXT NOT NULL
    );
    CREATE INDEX invites_to ON invites(to_id, status);
    CREATE TABLE match_results (
        id TEXT PRIMARY KEY,
        room_id TEXT NOT NULL,
        match_number INTEGER NOT NULL,
        map TEXT NOT NULL,
        terrorist_wins INTEGER NOT NULL,
        swat_wins INTEGER NOT NULL,
        players TEXT NOT NULL,
        finished_at REAL NOT NULL,
        UNIQUE(room_id, match_number)
    );
    """,
]


def open_database() -> sqlite3.Connection:
    CONFIG.database_path.parent.mkdir(parents=True, exist_ok=True)
    con = sqlite3.connect(CONFIG.database_path, timeout=15, isolation_level=None, check_same_thread=False)
    con.row_factory = sqlite3.Row
    con.execute("PRAGMA journal_mode = WAL")
    con.execute("PRAGMA foreign_keys = ON")
    con.execute("PRAGMA busy_timeout = 15000")
    con.execute("CREATE TABLE IF NOT EXISTS schema_version (version INTEGER NOT NULL)")
    row = con.execute("SELECT version FROM schema_version").fetchone()
    version = row["version"] if row else 0
    if row is None:
        con.execute("INSERT INTO schema_version VALUES (0)")
    # New migrations are only ever added at the end of the list, so a database keeps its data on every update.
    for number in range(version, len(MIGRATIONS)):
        con.execute("BEGIN")
        try:
            for statement in [s.strip() for s in MIGRATIONS[number].split(";") if s.strip()]:
                con.execute(statement)
            con.execute("UPDATE schema_version SET version = ?", (number + 1,))
            con.execute("COMMIT")
        except Exception:
            con.execute("ROLLBACK")
            raise
        print(f"[db] schema updated to version {number + 1}")
    return con


DB: Optional[sqlite3.Connection] = None
DB_LOCK = threading.RLock()   # one small server: every database step runs alone, so nothing can interleave


class Transaction:
    def __enter__(self):
        DB_LOCK.acquire()
        DB.execute("BEGIN IMMEDIATE")
        return DB

    def __exit__(self, kind, value, trace):
        try:
            DB.execute("COMMIT" if kind is None else "ROLLBACK")
        finally:
            DB_LOCK.release()


def now() -> float:
    return time.time()


def iso(ts: float) -> str:
    return datetime.fromtimestamp(ts, timezone.utc).isoformat(timespec="seconds")


# ----------------------------------------------------------------- rate limits

class RateLimiter:
    def __init__(self):
        self.hits = defaultdict(deque)
        self.lock = threading.Lock()

    def allow(self, key: str, limit: int, seconds: float) -> bool:
        t = time.monotonic()
        with self.lock:
            q = self.hits[key]
            while q and t - q[0] > seconds:
                q.popleft()
            if len(q) >= limit:
                return False
            q.append(t)
            if len(self.hits) > 100000:
                for k in [k for k, v in self.hits.items() if not v or t - v[-1] > 3600]:
                    del self.hits[k]
            return True

    def count(self, key: str, seconds: float) -> int:
        t = time.monotonic()
        with self.lock:
            q = self.hits.get(key)
            if not q:
                return 0
            while q and t - q[0] > seconds:
                q.popleft()
            return len(q)


LIMITS = RateLimiter()


def client_ip(request: Request) -> str:
    # The API listens on 127.0.0.1 only, so requests come from cloudflared, which names the real client.
    return request.headers.get("cf-connecting-ip") or (request.client.host if request.client else "?")


def limit(request: Request, name: str, count: int, seconds: float, key: str = None):
    if not LIMITS.allow(f"{name}:{key or client_ip(request)}", count, seconds):
        raise HTTPException(429, "Too many requests. Wait a minute and try again.")


# ----------------------------------------------------------------- accounts

HASHER = PasswordHasher()   # Argon2id with argon2-cffi's defaults (RFC 9106 low-memory profile)


def token_hash(token: str) -> str:
    return hmac.new(CONFIG.auth_secret.encode(), token.encode(), hashlib.sha256).hexdigest()


def new_session(con, user_id: str) -> str:
    token = secrets.token_urlsafe(32)
    t = now()
    con.execute("INSERT INTO sessions VALUES (?, ?, ?, ?, ?)", (token_hash(token), user_id, t, t, t + CONFIG.session_max))
    return token


def current_user(request: Request) -> sqlite3.Row:
    auth = request.headers.get("authorization", "")
    if not auth.startswith("Bearer ") or len(auth) > 200:
        raise HTTPException(401, "Log in first.")
    hashed = token_hash(auth[7:].strip())
    t = now()
    with DB_LOCK:
        row = DB.execute("SELECT u.*, s.last_used, s.expires_at FROM sessions s JOIN users u ON u.id = s.user_id "
                         "WHERE s.token_hash = ?", (hashed,)).fetchone()
        if row is None or row["expires_at"] < t or t - row["last_used"] > CONFIG.session_idle:
            if row is not None:
                DB.execute("DELETE FROM sessions WHERE token_hash = ?", (hashed,))
            raise HTTPException(401, "You were logged out. Log in again.")
        if t - row["last_used"] > 60:
            DB.execute("UPDATE sessions SET last_used = ? WHERE token_hash = ?", (t, hashed))
        DB.execute("UPDATE users SET last_seen = ? WHERE id = ?", (t, row["id"]))
    limit(request, "user", 240, 60, row["id"])
    return row


def check_version(request: Request):
    if not CONFIG.game_version:
        return
    theirs = request.headers.get("x-game-version", "")
    if theirs != CONFIG.game_version:
        raise HTTPException(426, f"Update Low Strike to version {CONFIG.game_version} to play online "
                                 f"(this game is {theirs or 'older'}). Download it from the website.")


class Credentials(BaseModel):
    nick: str = ""
    password: str = ""


class PasswordChange(BaseModel):
    old_password: str = ""
    new_password: str = ""


class PasswordOnly(BaseModel):
    password: str = ""


class ProfilePatch(BaseModel):
    hide_status: Optional[bool] = None
    profile: Optional[dict] = None
    linked_local: Optional[bool] = None


def password_problem(password: str) -> Optional[str]:
    if len(password) < 8:
        return "Password: at least 8 characters."
    if len(password) > 128:
        return "Password: at most 128 characters."
    return None


def me_view(con, user) -> dict:
    profile = con.execute("SELECT data, linked_local FROM profiles WHERE user_id = ?", (user["id"],)).fetchone()
    return {"id": user["id"], "nick": user["nick"], "hide_status": bool(user["hide_status"]),
            "created_at": user["created_at"], "profile": json.loads(profile["data"]) if profile else {},
            "linked_local": bool(profile["linked_local"]) if profile else False}


# ----------------------------------------------------------------- friends and presence

def pair(a: str, b: str):
    return (a, b) if a < b else (b, a)


def are_friends(con, a: str, b: str) -> bool:
    x, y = pair(a, b)
    return con.execute("SELECT 1 FROM friendships WHERE user_a = ? AND user_b = ?", (x, y)).fetchone() is not None


def friend_ids(con, user_id: str) -> list:
    rows = con.execute("SELECT user_b AS id FROM friendships WHERE user_a = ? UNION SELECT user_a FROM friendships WHERE user_b = ?",
                       (user_id, user_id)).fetchall()
    return [r["id"] for r in rows]


def status_of(con, user, viewer_hidden: bool) -> str:
    if viewer_hidden:
        return "hidden"   # hiding your own status hides everyone's from you as well
    if user["hide_status"] or now() - user["last_seen"] > ONLINE_SECONDS:
        return "offline"
    playing = con.execute("SELECT 1 FROM room_members m JOIN rooms r ON r.id = m.room_id WHERE m.user_id = ? AND m.connected = 1 "
                          "AND r.status = 'playing'", (user["id"],)).fetchone()
    return "in_match" if playing else "online"


def friends_view(con, me) -> list:
    ids = friend_ids(con, me["id"])
    if not ids:
        return []
    rows = con.execute(f"SELECT * FROM users WHERE id IN ({','.join('?' * len(ids))})", ids).fetchall()
    order = {"in_match": 0, "online": 1, "offline": 2, "hidden": 3}
    result = [{"id": r["id"], "nick": r["nick"], "status": status_of(con, r, bool(me["hide_status"]))} for r in rows]
    result.sort(key=lambda f: (order[f["status"]], f["nick"].lower()))
    return result


# ----------------------------------------------------------------- rooms and game hosts

SLOTS = {}   # slot id -> what its game host last reported (in memory: a restarted API waits for the next reports)
STARTED_AT = time.monotonic()


def slot_config(slot_id: str) -> Optional[dict]:
    return next((s for s in CONFIG.slots if s["id"] == slot_id), None)


def slot_alive(slot_id: str) -> bool:
    state = SLOTS.get(slot_id)
    return state is not None and time.monotonic() - state["at"] < SLOT_ALIVE_SECONDS


def open_room(con, room_id: str):
    return con.execute("SELECT * FROM rooms WHERE id = ? AND status != 'closed'", (room_id,)).fetchone()


def members_of(con, room_id: str) -> list:
    return con.execute("SELECT m.*, u.nick FROM room_members m JOIN users u ON u.id = m.user_id WHERE m.room_id = ? "
                       "ORDER BY m.joined_at", (room_id,)).fetchall()


def room_view(con, room, me_id: str, details: bool = False) -> dict:
    members = members_of(con, room["id"])
    is_member = any(m["user_id"] == me_id for m in members)
    owner = next((m["nick"] for m in members if m["user_id"] == room["owner_id"]), "")
    view = {
        "id": room["id"], "name": room["name"], "map": room["map"], "team_size": room["team_size"],
        "max_players": room["team_size"] * 2, "players": sum(1 for m in members if m["team"] != "spectator"),
        "spectators": sum(1 for m in members if m["team"] == "spectator"), "status": room["status"],
        "private": bool(room["private"]), "owner": owner, "owner_id": room["owner_id"] or "",
        "difficulty": room["difficulty"], "rounds": room["rounds"], "friendly_fire": bool(room["friendly_fire"]),
        "is_member": is_member, "is_owner": room["owner_id"] == me_id, "server_online": slot_alive(room["slot_id"]),
        "code": room["code"] if is_member else "", "members": [],
    }
    if details and is_member:
        view["members"] = [{"user_id": m["user_id"], "nick": m["nick"], "team": m["team"], "connected": bool(m["connected"]),
                            "owner": m["user_id"] == room["owner_id"]} for m in members]
    return view


def make_ticket(room, user, team: str) -> str:
    expires = int(now()) + TICKET_SECONDS
    payload = f"1|{room['id']}|{room['slot_id']}|{user['id']}|{user['nick']}|{team}|{expires}|{secrets.token_urlsafe(12)}"
    body = base64.urlsafe_b64encode(payload.encode()).decode().rstrip("=")
    signature = base64.urlsafe_b64encode(hmac.new(CONFIG.join_secret.encode(), body.encode(), hashlib.sha256).digest()).decode().rstrip("=")
    return f"{body}.{signature}"


def join_info(con, room, user, team: str) -> dict:
    slot = slot_config(room["slot_id"])
    return {"room": room_view(con, room, user["id"], details=True), "udp_host": slot["public_udp_host"],
            "udp_port": slot["public_udp_port"], "join_ticket": make_ticket(room, user, team), "ticket_seconds": TICKET_SECONDS,
            "protocol": PROTOCOL_VERSION}


def team_has_room(con, room, team: str, user_id: str) -> bool:
    count = con.execute("SELECT COUNT(*) AS n FROM room_members WHERE room_id = ? AND team = ? AND user_id != ?",
                        (room["id"], team, user_id)).fetchone()["n"]
    return count < (MAX_SPECTATORS if team == "spectator" else room["team_size"])


def pick_team(con, room, wanted: str, user_id: str) -> str:
    if wanted in TEAMS and team_has_room(con, room, wanted, user_id):
        return wanted
    for team in ("t", "swat", "spectator"):
        if team_has_room(con, room, team, user_id):
            return team
    raise HTTPException(409, "This room is full.")


def leave_room(con, room_id: str, user_id: str):
    con.execute("DELETE FROM room_members WHERE room_id = ? AND user_id = ?", (room_id, user_id))
    room = open_room(con, room_id)
    if room is None:
        return
    rest = members_of(con, room_id)
    if not rest:
        close_room(con, room_id)
    elif room["owner_id"] == user_id or room["owner_id"] is None:
        con.execute("UPDATE rooms SET owner_id = ?, updated_at = ? WHERE id = ?", (rest[0]["user_id"], now(), room_id))


def close_room(con, room_id: str):
    con.execute("UPDATE rooms SET status = 'closed', closed_at = ?, updated_at = ? WHERE id = ? AND status != 'closed'",
                (now(), now(), room_id))
    con.execute("DELETE FROM room_members WHERE room_id = ?", (room_id,))
    con.execute("DELETE FROM invites WHERE room_id = ?", (room_id,))


def new_code(con) -> str:
    while True:
        code = "".join(secrets.choice(CODE_LETTERS) for _ in range(6))
        if con.execute("SELECT 1 FROM rooms WHERE code = ? AND status != 'closed'", (code,)).fetchone() is None:
            return code


def clean_room_name(name: str) -> str:
    name = re.sub(r"[\x00-\x1f\x7f|]", "", name or "").strip()
    name = re.sub(r"\s+", " ", name)
    return name[:24]


class RoomSettings(BaseModel):
    name: Optional[str] = None
    map: Optional[str] = None
    team_size: Optional[int] = None
    difficulty: Optional[str] = None
    rounds: Optional[int] = None
    friendly_fire: Optional[bool] = None
    private: Optional[bool] = None
    team: Optional[str] = None


class JoinRequest(BaseModel):
    code: str = ""
    team: str = "t"


class InviteRequest(BaseModel):
    user_id: str = ""


class FriendRequest(BaseModel):
    nick: str = ""
    user_id: str = ""


def check_settings(s: RoomSettings, partial: bool) -> dict:
    values = {}
    if s.name is not None or not partial:
        name = clean_room_name(s.name or "")
        if len(name) < 3:
            raise HTTPException(422, "Room name: 3 to 24 characters.")
        values["name"] = name
    if s.map is not None or not partial:
        if (s.map or "dune") not in MAPS:
            raise HTTPException(422, "Unknown map.")
        values["map"] = s.map or "dune"
    if s.team_size is not None or not partial:
        size = s.team_size if s.team_size is not None else 5
        if not 1 <= size <= 5:
            raise HTTPException(422, "Players per team: 1 to 5.")
        values["team_size"] = size
    if s.difficulty is not None or not partial:
        if (s.difficulty or "Normal") not in DIFFICULTIES:
            raise HTTPException(422, "Unknown bot difficulty.")
        values["difficulty"] = s.difficulty or "Normal"
    if s.rounds is not None or not partial:
        if (s.rounds or 8) not in ROUNDS:
            raise HTTPException(422, "Rounds to win: 3, 5, 8 or 16.")
        values["rounds"] = s.rounds or 8
    if s.friendly_fire is not None or not partial:
        values["friendly_fire"] = 1 if s.friendly_fire else 0
    if s.private is not None or not partial:
        values["private"] = 1 if s.private else 0
    return values


# ----------------------------------------------------------------- the public API

@asynccontextmanager
async def lifespan(app):
    task = asyncio.create_task(maintenance_loop())
    yield
    task.cancel()


app = FastAPI(title="Low Strike online", docs_url=None, redoc_url=None, openapi_url=None, lifespan=lifespan)


@app.exception_handler(RequestValidationError)
async def bad_request(request: Request, exc: RequestValidationError):
    return JSONResponse({"detail": "The request was not understood."}, status_code=422)


@app.exception_handler(Exception)
async def server_error(request: Request, exc: Exception):
    print(f"[error] {request.method} {request.url.path}: {type(exc).__name__}: {exc}")
    return JSONResponse({"detail": "Something went wrong on the server."}, status_code=500)


@app.get("/")
def root():
    return {"name": "Low Strike online", "health": "/health"}


@app.get("/health")
def health():
    with DB_LOCK:
        online = DB.execute("SELECT COUNT(*) AS n FROM users WHERE last_seen > ?", (now() - ONLINE_SECONDS,)).fetchone()["n"]
        used = {r["slot_id"] for r in DB.execute("SELECT slot_id FROM rooms WHERE status != 'closed'")}
    alive = [s["id"] for s in CONFIG.slots if slot_alive(s["id"])]
    body = {"status": "ok", "game_version": CONFIG.game_version, "protocol": PROTOCOL_VERSION, "servers": len(alive),
            "rooms_free": sum(1 for s in alive if s not in used), "players_online": online}
    # The website shows this; it holds nothing private.
    return JSONResponse(body, headers={"Access-Control-Allow-Origin": "*", "Cache-Control": "no-store"})


@app.post("/auth/register")
def register(body: Credentials, request: Request):
    limit(request, "register", 20, 3600)   # many players can share one address on mobile networks
    nick = (body.nick or "").strip()
    if not NICK.match(nick):
        raise HTTPException(422, "Nickname: 3 to 16 letters, digits, _ or -.")
    problem = password_problem(body.password or "")
    if problem:
        raise HTTPException(422, problem)
    hashed = HASHER.hash(body.password)
    user_id = str(uuid.uuid4())
    with Transaction() as con:
        if con.execute("SELECT 1 FROM users WHERE nick_key = ?", (nick.lower(),)).fetchone():
            raise HTTPException(409, "This nickname is already taken.")
        con.execute("INSERT INTO users (id, nick, nick_key, password_hash, created_at, last_seen) VALUES (?, ?, ?, ?, ?, ?)",
                    (user_id, nick, nick.lower(), hashed, iso(now()), now()))
        con.execute("INSERT INTO profiles (user_id, data, updated_at) VALUES (?, '{}', ?)", (user_id, now()))
        token = new_session(con, user_id)
        user = con.execute("SELECT * FROM users WHERE id = ?", (user_id,)).fetchone()
        print(f"[auth] new account {nick}")
        return {"token": token, "user": me_view(con, user)}


@app.post("/auth/login")
def login(body: Credentials, request: Request):
    limit(request, "login", 60, 600)
    nick = (body.nick or "").strip()
    key = nick.lower()[:32]
    if LIMITS.count(f"login-fail:{key}", 900) >= 8:
        raise HTTPException(429, "Too many wrong passwords for this nickname. Wait 15 minutes.")
    with DB_LOCK:
        user = DB.execute("SELECT * FROM users WHERE nick_key = ?", (key,)).fetchone()
    ok = False
    if user is not None and body.password:
        try:
            ok = HASHER.verify(user["password_hash"], body.password)
        except (VerifyMismatchError, VerificationError, InvalidHashError):
            ok = False
    else:
        HASHER.hash("timing-equaliser")   # as slow as a real check, so the answer does not tell which nicknames exist
    if not ok:
        LIMITS.allow(f"login-fail:{key}", 1000, 900)
        raise HTTPException(401, "Wrong nickname or password.")
    with Transaction() as con:
        if HASHER.check_needs_rehash(user["password_hash"]):
            con.execute("UPDATE users SET password_hash = ? WHERE id = ?", (HASHER.hash(body.password), user["id"]))
        con.execute("UPDATE users SET last_seen = ? WHERE id = ?", (now(), user["id"]))
        token = new_session(con, user["id"])
        return {"token": token, "user": me_view(con, user)}


@app.post("/auth/logout")
def logout(request: Request, user=Depends(current_user)):
    with DB_LOCK:
        DB.execute("DELETE FROM sessions WHERE token_hash = ?", (token_hash(request.headers["authorization"][7:].strip()),))
        DB.execute("UPDATE users SET last_seen = 0 WHERE id = ?", (user["id"],))
    return {"ok": True}


@app.get("/me")
def get_me(user=Depends(current_user)):
    with DB_LOCK:
        return me_view(DB, user)


@app.patch("/me")
def patch_me(body: ProfilePatch, user=Depends(current_user)):
    with Transaction() as con:
        if body.hide_status is not None:
            con.execute("UPDATE users SET hide_status = ? WHERE id = ?", (1 if body.hide_status else 0, user["id"]))
        if body.profile is not None:
            data = json.dumps(body.profile, separators=(",", ":"))
            if len(data) > MAX_PROFILE:
                raise HTTPException(413, "Too many settings to save.")
            con.execute("UPDATE profiles SET data = ?, updated_at = ? WHERE user_id = ?", (data, now(), user["id"]))
        if body.linked_local is not None:
            con.execute("UPDATE profiles SET linked_local = ? WHERE user_id = ?", (1 if body.linked_local else 0, user["id"]))
        return me_view(con, con.execute("SELECT * FROM users WHERE id = ?", (user["id"],)).fetchone())


@app.post("/me/password")
def change_password(body: PasswordChange, request: Request, user=Depends(current_user)):
    limit(request, "password", 10, 600, user["id"])
    try:
        HASHER.verify(user["password_hash"], body.old_password or "")
    except (VerifyMismatchError, VerificationError, InvalidHashError):
        raise HTTPException(401, "Your current password is not right.")
    problem = password_problem(body.new_password or "")
    if problem:
        raise HTTPException(422, problem)
    hashed = HASHER.hash(body.new_password)
    keep = token_hash(request.headers["authorization"][7:].strip())
    with Transaction() as con:
        con.execute("UPDATE users SET password_hash = ? WHERE id = ?", (hashed, user["id"]))
        con.execute("DELETE FROM sessions WHERE user_id = ? AND token_hash != ?", (user["id"], keep))   # other devices log out
    return {"ok": True}


@app.delete("/me")
def delete_me(body: PasswordOnly, request: Request, user=Depends(current_user)):
    limit(request, "password", 10, 600, user["id"])
    try:
        HASHER.verify(user["password_hash"], body.password or "")
    except (VerifyMismatchError, VerificationError, InvalidHashError):
        raise HTTPException(401, "Your password is not right.")
    with Transaction() as con:
        for row in con.execute("SELECT room_id FROM room_members WHERE user_id = ?", (user["id"],)).fetchall():
            leave_room(con, row["room_id"], user["id"])
        con.execute("DELETE FROM users WHERE id = ?", (user["id"],))   # sessions, profile, friends, requests and invites go with it
    print("[auth] an account was deleted")
    return {"ok": True}


@app.get("/players")
def search_players(request: Request, search: str = "", user=Depends(current_user)):
    limit(request, "search", 30, 60, user["id"])
    text = re.sub(r"[^A-Za-z0-9_-]", "", search or "").lower()[:16]
    if len(text) < 2:
        return {"players": []}
    with DB_LOCK:
        rows = DB.execute("SELECT id, nick FROM users WHERE nick_key LIKE ? ESCAPE '\\' AND id != ? ORDER BY "
                          "nick_key != ?, length(nick_key), nick_key LIMIT 20",
                          (text.replace("_", "\\_") + "%", user["id"], text)).fetchall()
        friends = set(friend_ids(DB, user["id"]))
        sent = {r["to_id"] for r in DB.execute("SELECT to_id FROM friend_requests WHERE from_id = ?", (user["id"],))}
        received = {r["from_id"] for r in DB.execute("SELECT from_id FROM friend_requests WHERE to_id = ?", (user["id"],))}
    return {"players": [{"id": r["id"], "nick": r["nick"], "friend": r["id"] in friends, "request_sent": r["id"] in sent,
                         "request_received": r["id"] in received} for r in rows]}


@app.get("/friends")
def get_friends(user=Depends(current_user)):
    with DB_LOCK:
        incoming = DB.execute("SELECT r.id, r.from_id, u.nick, r.created_at FROM friend_requests r JOIN users u ON u.id = r.from_id "
                              "WHERE r.to_id = ? ORDER BY r.created_at", (user["id"],)).fetchall()
        outgoing = DB.execute("SELECT r.id, r.to_id, u.nick, r.created_at FROM friend_requests r JOIN users u ON u.id = r.to_id "
                              "WHERE r.from_id = ? ORDER BY r.created_at", (user["id"],)).fetchall()
        return {"friends": friends_view(DB, user), "hidden": bool(user["hide_status"]),
                "incoming": [{"id": r["id"], "user_id": r["from_id"], "nick": r["nick"]} for r in incoming],
                "outgoing": [{"id": r["id"], "user_id": r["to_id"], "nick": r["nick"]} for r in outgoing]}


@app.post("/friends/requests")
def send_friend_request(body: FriendRequest, request: Request, user=Depends(current_user)):
    limit(request, "friend-request", 30, 600, user["id"])
    with Transaction() as con:
        if body.user_id:
            other = con.execute("SELECT * FROM users WHERE id = ?", (body.user_id,)).fetchone()
        else:
            other = con.execute("SELECT * FROM users WHERE nick_key = ?", ((body.nick or "").strip().lower(),)).fetchone()
        if other is None:
            raise HTTPException(404, "No player has that nickname.")
        if other["id"] == user["id"]:
            raise HTTPException(422, "That is you.")
        if are_friends(con, user["id"], other["id"]):
            raise HTTPException(409, f"{other['nick']} is already your friend.")
        if len(friend_ids(con, user["id"])) >= MAX_FRIENDS:
            raise HTTPException(409, f"You have {MAX_FRIENDS} friends already.")
        reverse = con.execute("SELECT id FROM friend_requests WHERE from_id = ? AND to_id = ?", (other["id"], user["id"])).fetchone()
        if reverse:   # they asked first: now you are friends
            con.execute("DELETE FROM friend_requests WHERE id = ?", (reverse["id"],))
            con.execute("INSERT OR IGNORE INTO friendships VALUES (?, ?, ?)", (*pair(user["id"], other["id"]), now()))
            return {"ok": True, "friends": True, "nick": other["nick"]}
        con.execute("INSERT OR IGNORE INTO friend_requests VALUES (?, ?, ?, ?)", (str(uuid.uuid4()), user["id"], other["id"], now()))
        return {"ok": True, "friends": False, "nick": other["nick"]}


@app.post("/friends/requests/{request_id}/accept")
def accept_friend(request_id: str, user=Depends(current_user)):
    with Transaction() as con:
        row = con.execute("SELECT * FROM friend_requests WHERE id = ? AND to_id = ?", (request_id, user["id"])).fetchone()
        if row is None:
            raise HTTPException(404, "That request is gone.")
        if len(friend_ids(con, user["id"])) >= MAX_FRIENDS:
            raise HTTPException(409, f"You have {MAX_FRIENDS} friends already.")
        con.execute("DELETE FROM friend_requests WHERE id = ?", (request_id,))
        con.execute("INSERT OR IGNORE INTO friendships VALUES (?, ?, ?)", (*pair(user["id"], row["from_id"]), now()))
    return {"ok": True}


@app.post("/friends/requests/{request_id}/decline")
def decline_friend(request_id: str, user=Depends(current_user)):
    with Transaction() as con:   # declines one sent to you, or takes back one you sent
        con.execute("DELETE FROM friend_requests WHERE id = ? AND (to_id = ? OR from_id = ?)", (request_id, user["id"], user["id"]))
    return {"ok": True}


@app.delete("/friends/{friend_id}")
def remove_friend(friend_id: str, user=Depends(current_user)):
    with Transaction() as con:
        con.execute("DELETE FROM friendships WHERE user_a = ? AND user_b = ?", pair(user["id"], friend_id))
    return {"ok": True}


@app.get("/presence")
def presence(user=Depends(current_user)):
    with DB_LOCK:
        friends = friends_view(DB, user)
        pending = DB.execute("SELECT COUNT(*) AS n FROM friend_requests WHERE to_id = ?", (user["id"],)).fetchone()["n"]
        invites = DB.execute("SELECT COUNT(*) AS n FROM invites WHERE to_id = ? AND status = 'pending' AND expires_at > ?",
                             (user["id"], now())).fetchone()["n"]
        room = DB.execute("SELECT r.id FROM room_members m JOIN rooms r ON r.id = m.room_id WHERE m.user_id = ? AND r.status != 'closed'",
                          (user["id"],)).fetchone()
    return {"friends": friends, "hidden": bool(user["hide_status"]), "requests": pending, "invites": invites,
            "room_id": room["id"] if room else ""}


@app.get("/rooms")
def list_rooms(user=Depends(current_user)):
    with DB_LOCK:
        rows = DB.execute("SELECT * FROM rooms WHERE status != 'closed' ORDER BY created_at").fetchall()
        mine = {r["room_id"] for r in DB.execute("SELECT room_id FROM room_members WHERE user_id = ?", (user["id"],))}
        invited = {r["room_id"] for r in DB.execute("SELECT room_id FROM invites WHERE to_id = ? AND status = 'pending' AND expires_at > ?",
                                                    (user["id"], now()))}
        rooms = [room_view(DB, r, user["id"]) for r in rows if not r["private"] or r["id"] in mine or r["id"] in invited]
        used = {r["slot_id"] for r in rows}
    alive = [s["id"] for s in CONFIG.slots if slot_alive(s["id"])]
    return {"rooms": rooms, "servers": len(alive), "rooms_free": sum(1 for s in alive if s not in used)}


@app.post("/rooms")
def create_room(body: RoomSettings, request: Request, user=Depends(current_user)):
    check_version(request)
    limit(request, "create-room", 10, 600, user["id"])
    values = check_settings(body, partial=False)
    with Transaction() as con:
        for row in con.execute("SELECT room_id FROM room_members WHERE user_id = ?", (user["id"],)).fetchall():
            leave_room(con, row["room_id"], user["id"])   # one room at a time
        used = {r["slot_id"] for r in con.execute("SELECT slot_id FROM rooms WHERE status != 'closed'")}
        free = [s for s in CONFIG.slots if s["id"] not in used and slot_alive(s["id"])]
        if not free:
            if not any(slot_alive(s["id"]) for s in CONFIG.slots):
                raise HTTPException(503, "The game servers are offline right now. Try again later.")
            raise HTTPException(503, "All rooms are busy. Join a room or try again later.")
        room_id = str(uuid.uuid4())
        t = now()
        con.execute("INSERT INTO rooms (id, slot_id, name, owner_id, map, team_size, difficulty, rounds, friendly_fire, private, code, "
                    "status, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 'waiting', ?, ?)",
                    (room_id, free[0]["id"], values["name"], user["id"], values["map"], values["team_size"], values["difficulty"],
                     values["rounds"], values["friendly_fire"], values["private"], new_code(con), t, t))
        room = open_room(con, room_id)
        team = pick_team(con, room, body.team or "t", user["id"])
        con.execute("INSERT INTO room_members (room_id, user_id, team, joined_at) VALUES (?, ?, ?, ?)", (room_id, user["id"], team, t))
        print(f"[rooms] {user['nick']} made room {values['name']!r} on {free[0]['id']}")
        return join_info(con, room, user, team)


@app.get("/rooms/{room_id}")
def get_room(room_id: str, user=Depends(current_user)):
    with DB_LOCK:
        room = DB.execute("SELECT * FROM rooms WHERE id = ?", (room_id,)).fetchone()
        if room is None or room["status"] == "closed":
            raise HTTPException(404, "This room was closed.")
        view = room_view(DB, room, user["id"], details=True)
        if room["private"] and not view["is_member"]:
            raise HTTPException(404, "This room was closed.")
        return view


@app.patch("/rooms/{room_id}")
def change_room(room_id: str, body: RoomSettings, user=Depends(current_user)):
    values = check_settings(body, partial=True)
    with Transaction() as con:
        room = open_room(con, room_id)
        if room is None:
            raise HTTPException(404, "This room was closed.")
        if room["owner_id"] != user["id"]:
            raise HTTPException(403, "Only the room's owner can change it.")
        if room["status"] != "waiting":
            raise HTTPException(409, "Wait until the match is over.")
        if "team_size" in values:
            biggest = con.execute("SELECT MAX(n) AS n FROM (SELECT COUNT(*) AS n FROM room_members WHERE room_id = ? AND team != 'spectator' "
                                  "GROUP BY team)", (room_id,)).fetchone()["n"] or 0
            if values["team_size"] < biggest:
                raise HTTPException(409, "More players are in a team than that.")
        for key, value in values.items():
            con.execute(f"UPDATE rooms SET {key} = ? WHERE id = ?", (value, room_id))
        con.execute("UPDATE rooms SET updated_at = ? WHERE id = ?", (now(), room_id))
        return room_view(con, open_room(con, room_id), user["id"], details=True)


@app.delete("/rooms/{room_id}")
def delete_room(room_id: str, user=Depends(current_user)):
    with Transaction() as con:
        room = open_room(con, room_id)
        if room is None:
            return {"ok": True}
        if room["owner_id"] != user["id"]:
            raise HTTPException(403, "Only the room's owner can close it.")
        close_room(con, room_id)
    return {"ok": True}


def join_room(con, room, user, wanted_team: str, code: str = "") -> dict:
    member = con.execute("SELECT * FROM room_members WHERE room_id = ? AND user_id = ?", (room["id"], user["id"])).fetchone()
    if member is None:
        invited = con.execute("SELECT 1 FROM invites WHERE room_id = ? AND to_id = ? AND status = 'pending' AND expires_at > ?",
                              (room["id"], user["id"], now())).fetchone()
        code = re.sub(r"[^A-Za-z0-9]", "", code or "").upper()
        if room["private"] and not invited and not hmac.compare_digest(code, room["code"]):
            raise HTTPException(403, "This room is private: you need its code or an invitation.")
        if not slot_alive(room["slot_id"]):
            raise HTTPException(503, "This room's game server is not answering right now.")
        for row in con.execute("SELECT room_id FROM room_members WHERE user_id = ? AND room_id != ?", (user["id"], room["id"])).fetchall():
            leave_room(con, row["room_id"], user["id"])
        team = pick_team(con, room, wanted_team, user["id"])
        con.execute("INSERT INTO room_members (room_id, user_id, team, joined_at) VALUES (?, ?, ?, ?)", (room["id"], user["id"], team, now()))
        con.execute("UPDATE invites SET status = 'accepted' WHERE room_id = ? AND to_id = ? AND status = 'pending'", (room["id"], user["id"]))
    else:
        team = member["team"]
        if wanted_team in TEAMS and wanted_team != team and room["status"] == "waiting" and team_has_room(con, room, wanted_team, user["id"]):
            team = wanted_team
            con.execute("UPDATE room_members SET team = ? WHERE room_id = ? AND user_id = ?", (team, room["id"], user["id"]))
    return join_info(con, room, user, team)


@app.post("/rooms/{room_id}/join")
def join_room_by_id(room_id: str, body: JoinRequest, request: Request, user=Depends(current_user)):
    check_version(request)
    limit(request, "join", 30, 60, user["id"])
    with Transaction() as con:
        room = open_room(con, room_id)
        if room is None:
            raise HTTPException(404, "This room was closed.")
        return join_room(con, room, user, body.team, body.code or "")


@app.post("/rooms/code/{code}/join")
def join_room_by_code(code: str, body: JoinRequest, request: Request, user=Depends(current_user)):
    check_version(request)
    limit(request, "join-code", 20, 600, user["id"])   # codes cannot be guessed by trying many
    with Transaction() as con:
        code = re.sub(r"[^A-Za-z0-9]", "", code or "").upper()[:12]
        room = con.execute("SELECT * FROM rooms WHERE code = ? AND status != 'closed'", (code,)).fetchone()
        if room is None:
            raise HTTPException(404, "No open room has that code.")
        return join_room(con, room, user, body.team, room["code"])


@app.post("/rooms/{room_id}/leave")
def leave(room_id: str, user=Depends(current_user)):
    with Transaction() as con:
        leave_room(con, room_id, user["id"])
    return {"ok": True}


@app.post("/rooms/{room_id}/invite")
def invite(room_id: str, body: InviteRequest, request: Request, user=Depends(current_user)):
    limit(request, "invite", 30, 600, user["id"])
    with Transaction() as con:
        room = open_room(con, room_id)
        if room is None:
            raise HTTPException(404, "This room was closed.")
        if con.execute("SELECT 1 FROM room_members WHERE room_id = ? AND user_id = ?", (room_id, user["id"])).fetchone() is None:
            raise HTTPException(403, "Join the room first.")
        if not are_friends(con, user["id"], body.user_id):
            raise HTTPException(403, "You can invite your friends only.")
        con.execute("DELETE FROM invites WHERE room_id = ? AND to_id = ?", (room_id, body.user_id))
        con.execute("INSERT INTO invites VALUES (?, ?, ?, ?, ?, ?, 'pending')",
                    (str(uuid.uuid4()), room_id, user["id"], body.user_id, now(), now() + INVITE_SECONDS))
    return {"ok": True}


@app.post("/rooms/{room_id}/start")
def start(room_id: str, user=Depends(current_user)):
    with Transaction() as con:
        room = open_room(con, room_id)
        if room is None:
            raise HTTPException(404, "This room was closed.")
        if room["owner_id"] != user["id"]:
            raise HTTPException(403, "Only the room's owner can start the match.")
        if room["status"] == "playing":
            raise HTTPException(409, "The match is running already.")
        if not slot_alive(room["slot_id"]):
            raise HTTPException(503, "This room's game server is not answering right now.")
        if con.execute("SELECT 1 FROM room_members WHERE room_id = ? AND connected = 1", (room_id,)).fetchone() is None:
            raise HTTPException(409, "Nobody is connected to the game server yet. Wait a moment.")
        con.execute("UPDATE rooms SET start_requested = 1, updated_at = ? WHERE id = ?", (now(), room_id))
    return {"ok": True}


@app.get("/invites")
def get_invites(user=Depends(current_user)):
    with DB_LOCK:
        rows = DB.execute("SELECT i.id, i.room_id, r.name, r.map, r.status, u.nick FROM invites i JOIN rooms r ON r.id = i.room_id "
                          "JOIN users u ON u.id = i.from_id WHERE i.to_id = ? AND i.status = 'pending' AND i.expires_at > ? "
                          "AND r.status != 'closed' ORDER BY i.created_at DESC", (user["id"], now())).fetchall()
    return {"invites": [{"id": r["id"], "room_id": r["room_id"], "room_name": r["name"], "map": r["map"], "from": r["nick"],
                         "status": r["status"]} for r in rows]}


@app.post("/invites/{invite_id}/accept")
def accept_invite(invite_id: str, request: Request, body: Optional[JoinRequest] = None, user=Depends(current_user)):
    check_version(request)
    with Transaction() as con:
        row = con.execute("SELECT * FROM invites WHERE id = ? AND to_id = ? AND status = 'pending' AND expires_at > ?",
                          (invite_id, user["id"], now())).fetchone()
        if row is None:
            raise HTTPException(404, "This invitation has run out.")
        room = open_room(con, row["room_id"])
        if room is None:
            raise HTTPException(404, "This room was closed.")
        return join_room(con, room, user, body.team if body else "t")


@app.post("/invites/{invite_id}/decline")
def decline_invite(invite_id: str, user=Depends(current_user)):
    with Transaction() as con:
        con.execute("UPDATE invites SET status = 'declined' WHERE id = ? AND to_id = ?", (invite_id, user["id"]))
    return {"ok": True}


# ----------------------------------------------------------------- the internal API (game hosts on this PC)

internal = FastAPI(docs_url=None, redoc_url=None, openapi_url=None)


class HostPlayer(BaseModel):
    user_id: str = ""
    team: str = ""
    connected: bool = False


class Heartbeat(BaseModel):
    room_id: str = ""
    state: str = "lobby"   # lobby, playing, over
    protocol: int = 0
    version: str = ""
    match_number: int = 0
    terrorist_wins: int = 0
    swat_wins: int = 0
    map: str = ""
    players: list[HostPlayer] = []


@internal.post("/internal/slots/{slot_id}/heartbeat")
def heartbeat(slot_id: str, body: Heartbeat, request: Request):
    if not hmac.compare_digest(request.headers.get("x-internal-key", ""), CONFIG.internal_key()):
        raise HTTPException(403, "wrong key")
    if slot_config(slot_id) is None:
        raise HTTPException(404, "no such slot in ROOM_SLOTS_JSON")
    first = slot_id not in SLOTS or time.monotonic() - SLOTS[slot_id]["at"] > SLOT_ALIVE_SECONDS
    SLOTS[slot_id] = {"at": time.monotonic(), "room_id": body.room_id, "state": body.state, "protocol": body.protocol,
                      "version": body.version, "pid": SLOTS.get(slot_id, {}).get("pid")}
    if first:
        print(f"[hosts] game host {slot_id} is up (protocol {body.protocol}, version {body.version or '?'})")
    if body.protocol and body.protocol != PROTOCOL_VERSION:
        print(f"[hosts] {slot_id} runs protocol {body.protocol}, the API expects {PROTOCOL_VERSION}: update one of them")
    with Transaction() as con:
        room = con.execute("SELECT * FROM rooms WHERE slot_id = ? AND status != 'closed'", (slot_id,)).fetchone()
        if room is None:
            return {"has_room": False}
        if body.room_id == room["id"]:
            reported = {p.user_id: p for p in body.players}
            for m in members_of(con, room["id"]):
                p = reported.get(m["user_id"])
                connected = 1 if p is not None and p.connected else 0
                team = p.team if p is not None and p.team in TEAMS else m["team"]
                if connected != m["connected"] or team != m["team"]:
                    con.execute("UPDATE room_members SET connected = ?, team = ? WHERE room_id = ? AND user_id = ?",
                                (connected, team, room["id"], m["user_id"]))
            if body.state in ("playing", "over") and room["status"] != "playing":
                con.execute("UPDATE rooms SET status = 'playing', start_requested = 0, updated_at = ? WHERE id = ?", (now(), room["id"]))
            elif body.state == "lobby" and room["status"] == "playing":
                con.execute("UPDATE rooms SET status = 'waiting', updated_at = ? WHERE id = ?", (now(), room["id"]))
            if body.state == "over" and body.match_number > 0:
                players = json.dumps([p.user_id for p in body.players])
                con.execute("INSERT OR IGNORE INTO match_results VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                            (str(uuid.uuid4()), room["id"], body.match_number, body.map or room["map"], body.terrorist_wins,
                             body.swat_wins, players, now()))
            room = open_room(con, room["id"])
        members = members_of(con, room["id"])
        return {"has_room": True, "room": {
            "id": room["id"], "name": room["name"], "map": room["map"], "team_size": room["team_size"], "difficulty": room["difficulty"],
            "rounds": room["rounds"], "friendly_fire": bool(room["friendly_fire"]), "start": bool(room["start_requested"]),
            "owner_id": room["owner_id"] or "",
            "members": [{"user_id": m["user_id"], "nick": m["nick"], "team": m["team"]} for m in members]}}


# ----------------------------------------------------------------- upkeep

def upkeep():
    t = now()
    up = time.monotonic()
    with Transaction() as con:
        for room in con.execute("SELECT * FROM rooms WHERE status != 'closed'").fetchall():
            state = SLOTS.get(room["slot_id"])
            last = state["at"] if state else STARTED_AT
            if up - last > SLOT_LOST_SECONDS or slot_config(room["slot_id"]) is None:
                print(f"[rooms] closing {room['name']!r}: its game host stopped answering")
                close_room(con, room["id"])
                continue
            # Members whose game has closed: not on the game server and not asking the API for a minute.
            for m in con.execute("SELECT m.*, u.last_seen FROM room_members m JOIN users u ON u.id = m.user_id WHERE m.room_id = ?",
                                 (room["id"],)).fetchall():
                if not m["connected"] and t - m["last_seen"] > 60 and t - m["joined_at"] > 60:
                    leave_room(con, room["id"], m["user_id"])
            if open_room(con, room["id"]) is not None and not members_of(con, room["id"]) and t - room["created_at"] > 30:
                close_room(con, room["id"])
        con.execute("DELETE FROM invites WHERE expires_at < ?", (t,))
        con.execute("DELETE FROM sessions WHERE expires_at < ? OR last_used < ?", (t, t - CONFIG.session_idle))
        con.execute("DELETE FROM rooms WHERE status = 'closed' AND closed_at < ?", (t - 7 * 86400,))


async def maintenance_loop():
    while True:
        try:
            await asyncio.to_thread(upkeep)
        except Exception as e:
            print(f"[upkeep] {type(e).__name__}: {e}")
        await asyncio.sleep(5)


class HostSupervisor:
    """Starts a Unity game host for every slot and starts it again when it stops or stops reporting."""

    def __init__(self):
        self.processes = {}
        self.started = {}
        self.backoff = defaultdict(lambda: 5.0)
        self.stopping = False

    def command(self, slot) -> list:
        logs = HERE / "logs"
        logs.mkdir(exist_ok=True)
        return [CONFIG.unity_exe, *shlex.split(CONFIG.unity_args, posix=os.name != "nt"), "-ds-server",
                "-ds-port", str(slot["local_udp_port"]), "-ds-room-id", slot["id"],
                "-ds-control", f"http://127.0.0.1:{CONFIG.internal_port}", "-logFile", str(logs / f"unity-{slot['id']}.log")]

    def launch(self, slot):
        env = dict(os.environ)
        env["LOWSTRIKE_GAME_JOIN_SECRET"] = CONFIG.join_secret   # in its environment, never on its command line
        flags = subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0
        process = subprocess.Popen(self.command(slot), env=env, creationflags=flags, stdin=subprocess.DEVNULL,
                                   stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        self.processes[slot["id"]] = process
        self.started[slot["id"]] = time.monotonic()
        print(f"[hosts] started game host {slot['id']} (UDP {slot['local_udp_port']}, process {process.pid})")

    def run(self):
        if not os.path.isfile(CONFIG.unity_exe):
            print(f"[hosts] UNITY_SERVER_EXECUTABLE {CONFIG.unity_exe!r} does not exist: game hosts are not started")
            return
        while not self.stopping:
            for slot in CONFIG.slots:
                process = self.processes.get(slot["id"])
                up = time.monotonic()
                if process is None:
                    self.launch(slot)
                    continue
                if process.poll() is not None:
                    wait = self.backoff[slot["id"]]
                    if up - self.started[slot["id"]] > wait:
                        print(f"[hosts] game host {slot['id']} stopped (exit {process.returncode}); starting it again")
                        self.backoff[slot["id"]] = min(wait * 2, 120.0) if up - self.started[slot["id"]] < 60 else 5.0
                        self.launch(slot)
                    continue
                state = SLOTS.get(slot["id"])
                last = state["at"] if state else self.started[slot["id"]]
                if up - max(last, self.started[slot["id"]]) > 120:
                    print(f"[hosts] game host {slot['id']} has not reported for 2 minutes; restarting it")
                    process.kill()
                    self.started[slot["id"]] = 0.0
            time.sleep(2)

    def stop(self):
        self.stopping = True
        for process in self.processes.values():
            if process.poll() is None:
                process.terminate()


# ----------------------------------------------------------------- body size limit

class BodyLimit:
    """Turns away requests over MAX_BODY bytes before they are read in full."""

    def __init__(self, inner, size: int):
        self.inner = inner
        self.size = size

    async def __call__(self, scope, receive, send):
        if scope["type"] != "http":
            return await self.inner(scope, receive, send)
        length = dict(scope.get("headers") or []).get(b"content-length")
        if length is not None and (not length.isdigit() or int(length) > self.size):
            return await JSONResponse({"detail": "The request is too large."}, status_code=413)(scope, receive, send)
        total = 0

        async def limited():
            nonlocal total
            message = await receive()
            if message["type"] == "http.request":
                total += len(message.get("body", b""))
                if total > self.size:
                    raise ValueError("body too large")
            return message

        try:
            await self.inner(scope, limited, send)
        except ValueError as e:
            if str(e) != "body too large":
                raise
            await JSONResponse({"detail": "The request is too large."}, status_code=413)(scope, receive, send)


# ----------------------------------------------------------------- start

async def serve():
    public = uvicorn.Server(uvicorn.Config(BodyLimit(app, MAX_BODY), host=CONFIG.api_host, port=CONFIG.api_port,
                                           log_level="info", proxy_headers=False, server_header=False))
    private = uvicorn.Server(uvicorn.Config(BodyLimit(internal, MAX_BODY), host="127.0.0.1", port=CONFIG.internal_port,
                                            log_level="warning", access_log=False, server_header=False))
    await asyncio.gather(public.serve(), private.serve())


def main():
    global DB
    if "--log" in sys.argv[1:-1]:
        # Without a console (pythonw): everything printed goes to this file, the one before kept as .old.
        path = Path(sys.argv[sys.argv.index("--log") + 1])
        path.parent.mkdir(parents=True, exist_ok=True)
        if path.exists() and path.stat().st_size > 5_000_000:
            path.replace(path.with_suffix(".old.log"))
        sys.stdout = sys.stderr = open(path, "a", encoding="utf-8", buffering=1)
        print()
        print(f"[api] started {iso(now())}")
    else:
        sys.stdout.reconfigure(line_buffering=True)   # log lines appear at once (also when written to a file)
    CONFIG.check()
    # One copy only (on Windows a second one could share the port, and its game hosts would fight over theirs).
    for port in (CONFIG.api_port, CONFIG.internal_port):
        with socket.socket() as probe:
            try:
                probe.bind(("127.0.0.1", port))
            except OSError:
                print(f"[api] port {port} is in use: the server runs already (or another program uses that port)")
                return
    DB = open_database()
    print(f"[api] database {CONFIG.database_path}")
    print(f"[api] public API on http://{CONFIG.api_host}:{CONFIG.api_port} (the tunnel's target), "
          f"game hosts report to http://127.0.0.1:{CONFIG.internal_port}")
    print(f"[api] {len(CONFIG.slots)} room slot(s): {', '.join(s['id'] for s in CONFIG.slots) or 'none'}")
    supervisor = None
    if CONFIG.unity_exe:
        supervisor = HostSupervisor()
        threading.Thread(target=supervisor.run, daemon=True).start()
    else:
        print("[api] UNITY_SERVER_EXECUTABLE is not set: start the game hosts yourself (see README)")
    try:
        asyncio.run(serve())
    finally:
        if supervisor:
            supervisor.stop()


if __name__ == "__main__":
    main()
