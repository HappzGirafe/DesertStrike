"""LIVING WORLD on the online server: the one persistent world's memory.

The Unity world host (the game run with -ds-server -ds-world) simulates the world and decides what a player's hands
did. It reports to the internal API on this PC; this module keeps the canonical record in the same SQLite database
as the accounts:

* world_mutations    the only canonical changes of the world. Each one is HUMAN: it names the authenticated account
                     whose live session did it, the action it came from, what it changed (before / after) and why
                     it was allowed. Written with the object's new state in ONE transaction, numbered by
                     worlds.revision. Nothing else (bots, Echoes, timers, restarts, rule evaluation) writes here.
* player_actions     what people did that did not change the world: crossings of passages, fights, interactions
                     started, skills used. Private; it teaches the bots and decides some legends.
* world_objects      the current state of every changeable thing (derived from the mutations, kept for speed).
* legends / player_titles / player_skills   derived from the mutations and actions by the rules in world_rules.json.
* avatars, echo_diary, behavior_profiles, movement_traces   the characters, what their AI Echoes did (kept apart
                     from human history), and the learnt behaviour.
* world_lifecycle    technical history: started, awake, asleep, shut down, crash recovery. Never a world change.

Public reads (history, legends, who changed what) are open; writes come only from the world host over the internal
address, with an action from a player the host reports as connected right now.
"""

import base64
import hashlib
import hmac
import json
import os
import re
import secrets
import shutil
import sqlite3
import threading
import time
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Optional

from fastapi import Depends, HTTPException, Request
from fastapi.responses import JSONResponse
from pydantic import BaseModel

WORLD_SCHEMA_VERSION = 1
HERE = Path(__file__).resolve().parent

SCHEMA = """
CREATE TABLE worlds (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    created_at REAL NOT NULL,
    schema_version INTEGER NOT NULL,
    revision INTEGER NOT NULL DEFAULT 0,
    last_human_change_at REAL,
    total_awake_seconds REAL NOT NULL DEFAULT 0,
    lifecycle_state TEXT NOT NULL DEFAULT 'offline',
    last_awake_at REAL,
    last_heartbeat_at REAL,
    definitions TEXT NOT NULL DEFAULT '{}'
);
CREATE TABLE world_lifecycle (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    world_id TEXT NOT NULL,
    kind TEXT NOT NULL,
    recorded_at REAL NOT NULL,
    previous_state TEXT,
    note TEXT
);
CREATE TABLE world_objects (
    world_id TEXT NOT NULL,
    object_id TEXT NOT NULL,
    state TEXT NOT NULL,
    owner_id TEXT,
    owner_label TEXT,
    revision INTEGER NOT NULL DEFAULT 0,
    last_mutation_id TEXT,
    held_awake_seconds REAL NOT NULL DEFAULT 0,
    PRIMARY KEY(world_id, object_id)
);
CREATE TABLE world_mutations (
    id TEXT PRIMARY KEY,
    world_id TEXT NOT NULL,
    revision INTEGER NOT NULL,
    source_type TEXT NOT NULL CHECK(source_type = 'HUMAN'),
    player_id TEXT REFERENCES users(id) ON DELETE SET NULL,
    player_nick TEXT NOT NULL,
    action_id TEXT NOT NULL UNIQUE,
    session_id TEXT,
    type TEXT NOT NULL,
    verb TEXT NOT NULL,
    object_id TEXT NOT NULL,
    sector_id TEXT NOT NULL,
    x REAL, y REAL, z REAL,
    before_state TEXT NOT NULL,
    after_state TEXT NOT NULL,
    before_owner TEXT,
    effect_kinds TEXT NOT NULL,
    contributors TEXT NOT NULL,
    caused_by TEXT NOT NULL,
    occurred_at REAL NOT NULL,
    schema_version INTEGER NOT NULL,
    UNIQUE(world_id, revision)
);
CREATE INDEX world_mutations_object ON world_mutations(world_id, object_id, revision);
CREATE INDEX world_mutations_player ON world_mutations(player_id, occurred_at);
CREATE TABLE player_actions (
    id TEXT PRIMARY KEY,
    world_id TEXT NOT NULL,
    player_id TEXT REFERENCES users(id) ON DELETE CASCADE,
    type TEXT NOT NULL,
    sector_id TEXT,
    object_id TEXT,
    x REAL, y REAL, z REAL,
    data TEXT NOT NULL DEFAULT '{}',
    occurred_at REAL NOT NULL
);
CREATE INDEX player_actions_type ON player_actions(world_id, type, occurred_at);
CREATE INDEX player_actions_player ON player_actions(player_id, occurred_at);
CREATE TABLE movement_traces (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    world_id TEXT NOT NULL,
    player_id TEXT REFERENCES users(id) ON DELETE CASCADE,
    sector_id TEXT,
    started_at REAL NOT NULL,
    ended_at REAL NOT NULL,
    points TEXT NOT NULL
);
CREATE TABLE behavior_profiles (
    world_id TEXT NOT NULL,
    subject TEXT NOT NULL,
    data TEXT NOT NULL,
    updated_at REAL NOT NULL,
    PRIMARY KEY(world_id, subject)
);
CREATE TABLE avatars (
    world_id TEXT NOT NULL,
    player_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    x REAL NOT NULL, y REAL NOT NULL, z REAL NOT NULL, yaw REAL NOT NULL,
    sector_id TEXT,
    health INTEGER NOT NULL,
    weapon TEXT,
    echo_state TEXT NOT NULL,
    intent TEXT,
    last_human_at REAL,
    updated_at REAL NOT NULL,
    PRIMARY KEY(world_id, player_id)
);
CREATE TABLE echo_diary (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    world_id TEXT NOT NULL,
    player_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    kind TEXT NOT NULL,
    text TEXT NOT NULL,
    sector_id TEXT,
    occurred_at REAL NOT NULL
);
CREATE INDEX echo_diary_player ON echo_diary(player_id, occurred_at);
CREATE TABLE player_titles (
    world_id TEXT NOT NULL,
    player_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    rule_id TEXT NOT NULL,
    title TEXT NOT NULL,
    reason TEXT NOT NULL,
    mutation_ids TEXT NOT NULL,
    legend_id TEXT,
    unlocked_at REAL NOT NULL,
    PRIMARY KEY(world_id, player_id, rule_id)
);
CREATE TABLE player_skills (
    world_id TEXT NOT NULL,
    player_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    skill_id TEXT NOT NULL,
    rule_id TEXT NOT NULL,
    reason TEXT NOT NULL,
    mutation_ids TEXT NOT NULL,
    legend_id TEXT,
    unlocked_at REAL NOT NULL,
    PRIMARY KEY(world_id, player_id, skill_id)
);
CREATE TABLE legends (
    id TEXT PRIMARY KEY,
    world_id TEXT NOT NULL,
    rule_id TEXT NOT NULL,
    anchor_mutation_id TEXT NOT NULL,
    name TEXT NOT NULL,
    story TEXT NOT NULL,
    epic_tier INTEGER NOT NULL,
    impact_score REAL NOT NULL,
    mutation_ids TEXT NOT NULL,
    object_ids TEXT NOT NULL,
    sector_ids TEXT NOT NULL,
    signals TEXT NOT NULL,
    first_occurred_at REAL NOT NULL,
    recognized_at REAL NOT NULL,
    schema_version INTEGER NOT NULL,
    UNIQUE(world_id, rule_id, anchor_mutation_id)
);
CREATE TABLE legend_roles (
    legend_id TEXT NOT NULL REFERENCES legends(id) ON DELETE CASCADE,
    player_id TEXT REFERENCES users(id) ON DELETE SET NULL,
    player_nick TEXT NOT NULL,
    role TEXT NOT NULL,
    mutation_ids TEXT NOT NULL
);
CREATE INDEX legend_roles_player ON legend_roles(player_id);
CREATE TABLE world_squads (
    id TEXT PRIMARY KEY,
    world_id TEXT NOT NULL,
    name TEXT NOT NULL,
    created_at REAL NOT NULL
);
CREATE TABLE world_squad_members (
    squad_id TEXT NOT NULL REFERENCES world_squads(id) ON DELETE CASCADE,
    player_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    joined_at REAL NOT NULL,
    PRIMARY KEY(squad_id, player_id)
);
CREATE UNIQUE INDEX world_squad_one_per_player ON world_squad_members(player_id);
CREATE TABLE world_squad_invites (
    id TEXT PRIMARY KEY,
    squad_id TEXT NOT NULL REFERENCES world_squads(id) ON DELETE CASCADE,
    from_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    to_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    created_at REAL NOT NULL
);
CREATE TABLE world_snapshots (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    world_id TEXT NOT NULL,
    up_to_revision INTEGER NOT NULL,
    schema_version INTEGER NOT NULL,
    created_at REAL NOT NULL,
    checksum TEXT NOT NULL,
    data TEXT NOT NULL
)
"""

# What each kind of object can become, by which deed, and what that does to the world. The world host offers the
# same verbs; this table decides (a change the table does not allow is refused whatever the host says).
TRANSITIONS = {
    "bridge": {("intact", "DESTROY"): ("destroyed", "BRIDGE_DESTROYED", ["route_closed", "structure_removed"]),
               ("destroyed", "REBUILD"): ("intact", "BRIDGE_REBUILT", ["route_opened", "structure_built"])},
    "grate": {("welded", "CUT"): ("open", "GRATE_CUT_OPEN", ["route_opened"]),
              ("open", "WELD"): ("welded", "GRATE_WELDED", ["route_closed"])},
    "gate": {("open", "CLOSE"): ("closed", "GATE_CLOSED", ["route_closed"]),
             ("closed", "OPEN"): ("open", "GATE_OPENED", ["route_opened"])},
    "generator": {("on", "SHUT_DOWN"): ("off", "POWER_OFF", ["power_off"]),
                  ("off", "RESTART"): ("on", "POWER_ON", ["power_on"])},
    "barricade": {("empty", "BUILD"): ("built", "BARRICADE_BUILT", ["structure_built", "route_narrowed"]),
                  ("built", "TEAR_DOWN"): ("empty", "BARRICADE_REMOVED", ["structure_removed", "route_widened"])},
    "wall": {("standing", "TEAR_DOWN"): ("breached", "WALL_BREACHED", ["route_opened", "structure_removed"]),
             ("breached", "REBUILD"): ("standing", "WALL_REBUILT", ["route_closed", "structure_built"])},
    "checkpoint": {("neutral", "CLAIM"): ("held", "CHECKPOINT_CLAIMED", ["control_changed"]),
                   ("held", "CLAIM"): ("held", "CHECKPOINT_CLAIMED", ["control_changed"])},
    "memorial": {("empty", "RAISE"): ("raised", "MEMORIAL_RAISED", ["structure_built"])},
}
ROUTE_TYPES = ("bridge", "grate", "gate", "barricade", "wall")
DEED_TEXT = {
    "BRIDGE_DESTROYED": "brought down {object}", "BRIDGE_REBUILT": "rebuilt {object}",
    "GRATE_CUT_OPEN": "cut open {object}", "GRATE_WELDED": "welded {object} shut",
    "GATE_CLOSED": "closed {object}", "GATE_OPENED": "opened {object}",
    "POWER_OFF": "shut down {object}", "POWER_ON": "restarted {object}",
    "BARRICADE_BUILT": "built {object}", "BARRICADE_REMOVED": "tore down {object}",
    "WALL_BREACHED": "broke through {object}", "WALL_REBUILT": "rebuilt {object}",
    "CHECKPOINT_CLAIMED": "took {object}", "MEMORIAL_RAISED": "raised {object}",
}
OBJECT_COOLDOWN = 30       # seconds before the same object can change again (no switch-flicking)
PLAYER_COOLDOWN = 8        # seconds between two changes by the same player
PRESENCE_SECONDS = 8       # the host reports every second; a player missing this long is not connected


def world_day(created_at: float, at: float) -> int:
    """World Day N: whole UTC days since the world was founded (asleep or offline days count too), from 1."""
    start = datetime.fromtimestamp(created_at, timezone.utc).date()
    return (datetime.fromtimestamp(at, timezone.utc).date() - start).days + 1


def clean_label(text: str, size: int = 40) -> str:
    return re.sub(r"[\x00-\x1f\x7f<>]", "", text or "")[:size]


def install(app, internal, s, record_start=True):
    """Adds the world's routes to the public and internal apps. `s` is the server module (database, accounts).
    record_start: False for the one-off commands (a backup is not a server start)."""
    config = s.CONFIG
    world_id = os.environ.get("WORLD_ID") or config.world_id
    rules_path = HERE / "world_rules.json"
    RULES = json.loads(rules_path.read_text(encoding="utf-8"))
    SKILLS = {skill["id"]: skill for skill in RULES["skills"]}
    presence = {}                 # player id -> {"state", "sector", "at"} from the host's latest report
    host_state = {"at": 0.0, "awake": False, "shutdown": False, "notices": []}
    lock = threading.Lock()

    def db():
        return s.DB

    # ------------------------------------------------------------------ the world record

    def ensure_world(con):
        row = con.execute("SELECT * FROM worlds WHERE id = ?", (world_id,)).fetchone()
        if row is None:
            con.execute("INSERT INTO worlds (id, name, created_at, schema_version) VALUES (?, ?, ?, ?)",
                        (world_id, config.world_name, s.now(), WORLD_SCHEMA_VERSION))
            con.execute("INSERT INTO world_lifecycle (world_id, kind, recorded_at, note) VALUES (?, 'FOUNDED', ?, ?)",
                        (world_id, s.now(), f"World '{config.world_name}' founded"))
            row = con.execute("SELECT * FROM worlds WHERE id = ?", (world_id,)).fetchone()
        return row

    def lifecycle(con, kind, note=""):
        world = ensure_world(con)
        con.execute("INSERT INTO world_lifecycle (world_id, kind, recorded_at, previous_state, note) VALUES (?, ?, ?, ?, ?)",
                    (world_id, kind, s.now(), world["lifecycle_state"], note))

    def definitions(con) -> dict:
        return json.loads(ensure_world(con)["definitions"] or "{}")

    def object_defs(con) -> dict:
        return {o["id"]: o for o in definitions(con).get("objects", [])}

    def object_name(defs, object_id):
        d = defs.get(object_id)
        return d["name"] if d else object_id

    def sector_name(con, sector_id):
        for sector in definitions(con).get("sectors", []):
            if sector["id"] == sector_id:
                return sector["name"]
        return sector_id or ""

    def nick_of(con, player_id):
        if not player_id:
            return "a former player"
        row = con.execute("SELECT nick FROM users WHERE id = ?", (player_id,)).fetchone()
        return row["nick"] if row else "a former player"

    # Startup: a session that did not end with SHUTDOWN ended unexpectedly; say so instead of inventing when.
    with s.Transaction() as con:
        world = ensure_world(con)
        if not record_start:
            return {"forget_player": None, "status": None, "snapshot": lambda c: snapshot(c), "world_id": world_id,
                    "host_alive": lambda: False}
        last = con.execute("SELECT kind FROM world_lifecycle WHERE world_id = ? AND kind IN ('START', 'SHUTDOWN', 'FOUNDED') "
                           "ORDER BY id DESC LIMIT 1", (world_id,)).fetchone()
        if last is not None and last["kind"] == "START":
            con.execute("INSERT INTO world_lifecycle (world_id, kind, recorded_at, previous_state, note) VALUES (?, 'CRASH_RECOVERY', ?, ?, ?)",
                        (world_id, s.now(), world["lifecycle_state"], "Previous session ended unexpectedly"))
        lifecycle(con, "START", "Online server started")
        con.execute("UPDATE worlds SET lifecycle_state = 'offline' WHERE id = ?", (world_id,))

    # ------------------------------------------------------------------ views

    def mutation_view(con, m, defs=None) -> dict:
        defs = defs if defs is not None else object_defs(con)
        world = ensure_world(con)
        name = object_name(defs, m["object_id"])
        nick = m["player_nick"] if m["player_id"] else "a former player"
        contributors = json.loads(m["contributors"] or "[]")
        later = con.execute("SELECT id, player_nick, player_id, type, occurred_at FROM world_mutations WHERE world_id = ? AND object_id = ? "
                            "AND revision > ? ORDER BY revision LIMIT 1", (world_id, m["object_id"], m["revision"])).fetchone()
        return {
            "id": m["id"], "revision": m["revision"], "type": m["type"], "verb": m["verb"],
            "object_id": m["object_id"], "object_name": name, "sector_id": m["sector_id"], "sector_name": sector_name(con, m["sector_id"]),
            "player_id": m["player_id"] or "", "player_nick": nick,
            "summary": f"{nick} " + DEED_TEXT.get(m["type"], "changed {object}").format(object=name),
            "before": m["before_state"], "after": m["after_state"], "effects": json.loads(m["effect_kinds"]),
            "contributors": [{"nick": c.get("nick", ""), "role": c.get("role", "")} for c in contributors],
            "occurred_at": s.iso(m["occurred_at"]), "day": world_day(world["created_at"], m["occurred_at"]),
            "position": [m["x"], m["y"], m["z"]],
            "still_current": later is None,
            "superseded_by": None if later is None else {
                "nick": later["player_nick"] if later["player_id"] else "a former player", "type": later["type"],
                "occurred_at": s.iso(later["occurred_at"])},
        }

    def legend_view(con, legend, roles=True) -> dict:
        world = ensure_world(con)
        view = {
            "id": legend["id"], "name": legend["name"], "story": legend["story"], "tier": legend["epic_tier"],
            "impact": round(legend["impact_score"], 1), "signals": json.loads(legend["signals"]),
            "sector_ids": json.loads(legend["sector_ids"]), "object_ids": json.loads(legend["object_ids"]),
            "mutation_ids": json.loads(legend["mutation_ids"]),
            "occurred_at": s.iso(legend["first_occurred_at"]), "recognized_at": s.iso(legend["recognized_at"]),
            "day": world_day(world["created_at"], legend["first_occurred_at"]), "roles": [],
        }
        if roles:
            for r in con.execute("SELECT * FROM legend_roles WHERE legend_id = ?", (legend["id"],)):
                view["roles"].append({"player_id": r["player_id"] or "", "nick": r["player_nick"] if r["player_id"] else "a former player",
                                      "role": r["role"]})
        return view

    def status_view(con) -> dict:
        world = ensure_world(con)
        t = s.now()
        alive = time.monotonic() - host_state["at"] < 10
        humans = [p for p, v in presence.items() if v["state"] == "human" and time.monotonic() - v["at"] < PRESENCE_SECONDS]
        echoes = [p for p, v in presence.items() if v["state"] == "echo" and time.monotonic() - v["at"] < PRESENCE_SECONDS]
        crash = con.execute("SELECT recorded_at FROM world_lifecycle WHERE world_id = ? AND kind = 'CRASH_RECOVERY' "
                            "ORDER BY id DESC LIMIT 1", (world_id,)).fetchone()
        recent = con.execute("SELECT * FROM world_mutations WHERE world_id = ? ORDER BY revision DESC LIMIT 5", (world_id,)).fetchall()
        defs = object_defs(con)
        return {
            "world_id": world_id, "name": world["name"],
            "state": ("awake" if host_state["awake"] else "asleep") if alive else "host_offline",
            "day": world_day(world["created_at"], t), "founded_at": s.iso(world["created_at"]),
            "time_awake_seconds": int(world["total_awake_seconds"]),
            "last_awake_at": s.iso(world["last_awake_at"]) if world["last_awake_at"] else None,
            "last_change_at": s.iso(world["last_human_change_at"]) if world["last_human_change_at"] else None,
            "revision": world["revision"], "players_online": len(humans), "echoes_active": len(echoes),
            "previous_session_crashed_at": s.iso(crash["recorded_at"]) if crash else None,
            "recent_changes": [mutation_view(con, m, defs)["summary"] for m in recent],
        }

    # ------------------------------------------------------------------ the rules (titles, skills, legends)

    def users_of_passage_after(con, passage, after, until, exclude):
        rows = con.execute("SELECT DISTINCT player_id FROM player_actions WHERE world_id = ? AND type = 'PASSAGE_USED' "
                           "AND json_extract(data, '$.passage') = ? AND occurred_at > ? AND occurred_at <= ? AND player_id IS NOT NULL",
                           (world_id, passage, after, until)).fetchall()
        return [r["player_id"] for r in rows if r["player_id"] not in exclude]

    def held_until(con, m):
        nxt = con.execute("SELECT occurred_at FROM world_mutations WHERE world_id = ? AND object_id = ? AND revision > ? "
                          "ORDER BY revision LIMIT 1", (world_id, m["object_id"], m["revision"])).fetchone()
        return nxt["occurred_at"] if nxt else s.now()

    def check(con, req, m, defs, facts) -> bool:
        """Whether a rule's conditions hold for mutation m (its author's deed); fills `facts` for the story."""
        author = m["player_id"]
        for key, value in req.items():
            if key == "mutation":
                if m["type"] not in value:
                    return False
            elif key == "any":
                if not any(check(con, sub, m, defs, facts) for sub in value):
                    return False
            elif key == "others_used_passage" or key == "others_used_object_passage":
                passage = value.get("passage") or defs.get(m["object_id"], {}).get("passage")
                if not passage:
                    return False
                others = users_of_passage_after(con, passage, m["occurred_at"], held_until(con, m), {author} | set(facts.get("authors_ids", [])))
                facts["others"] = max(facts.get("others", 0), len(others))
                facts["affected_ids"] = sorted(set(facts.get("affected_ids", [])) | set(others))
                if len(others) < value.get("min", 1):
                    return False
            elif key == "distinct_route_objects":
                rows = con.execute(f"SELECT DISTINCT object_id FROM world_mutations WHERE world_id = ? AND player_id = ? "
                                   f"AND occurred_at BETWEEN ? AND ?", (world_id, author, m["occurred_at"] - value["hours"] * 3600,
                                                                        m["occurred_at"])).fetchall()
                route = [r["object_id"] for r in rows if defs.get(r["object_id"], {}).get("type") in ROUTE_TYPES]
                facts["mutation_scope"] = route
                if len(route) < value["min"]:
                    return False
            elif key == "distinct_built":
                rows = con.execute("SELECT o.object_id FROM world_objects o JOIN world_mutations w ON w.id = o.last_mutation_id "
                                   "WHERE o.world_id = ? AND w.player_id = ? AND o.state = 'built'", (world_id, author)).fetchall()
                built = [r["object_id"] for r in rows if defs.get(r["object_id"], {}).get("type") == value["type"]]
                if len(built) < value["min"]:
                    return False
            elif key == "checkpoint_held_seconds":
                row = con.execute("SELECT o.held_awake_seconds FROM world_objects o JOIN world_mutations w ON w.id = o.last_mutation_id "
                                  "WHERE o.world_id = ? AND w.player_id = ? AND w.type = 'CHECKPOINT_CLAIMED'", (world_id, author)).fetchone()
                if row is None or row["held_awake_seconds"] < value:
                    return False
            elif key == "previous_owner_other_player":
                if not m["before_owner"] or m["before_owner"] == author:
                    return False
                facts["breaker_id"] = m["before_owner"]
            elif key == "repairs_other_player":
                prev = con.execute("SELECT player_id FROM world_mutations WHERE world_id = ? AND object_id = ? AND revision < ? "
                                   "ORDER BY revision DESC LIMIT 1", (world_id, m["object_id"], m["revision"])).fetchone()
                if prev is None or not prev["player_id"] or prev["player_id"] == author:
                    return False
                facts["breaker_id"] = prev["player_id"]
            elif key == "co_authors_on":
                rows = con.execute(f"SELECT id, player_id FROM world_mutations WHERE world_id = ? AND object_id IN "
                                   f"({','.join('?' * len(value['objects']))}) AND occurred_at BETWEEN ? AND ? AND player_id IS NOT NULL",
                                   (world_id, *value["objects"], m["occurred_at"] - value["hours"] * 3600, m["occurred_at"])).fetchall()
                people = sorted({r["player_id"] for r in rows})
                if len(people) < value["min_people"]:
                    return False
                facts["authors_ids"] = people
                facts["chain"] = [r["id"] for r in rows]
            elif key == "distinct_claimants":
                rows = con.execute("SELECT id, player_id FROM world_mutations WHERE world_id = ? AND object_id = ? AND type = 'CHECKPOINT_CLAIMED' "
                                   "AND occurred_at BETWEEN ? AND ? AND player_id IS NOT NULL",
                                   (world_id, value["object"], m["occurred_at"] - value["hours"] * 3600, m["occurred_at"])).fetchall()
                people = sorted({r["player_id"] for r in rows})
                if len(people) < value["min"]:
                    return False
                facts["authors_ids"] = people
                facts["chain"] = [r["id"] for r in rows]
            elif key == "distinct_objects_of_type":
                rows = con.execute("SELECT DISTINCT object_id FROM world_mutations WHERE world_id = ? AND player_id = ?",
                                   (world_id, author)).fetchall()
                if len([r for r in rows if defs.get(r["object_id"], {}).get("type") == value["type"]]) < value["min"]:
                    return False
            else:
                return False   # a condition this server does not know is never true
        return True

    def evaluate(con, mutations=None) -> list:
        """Runs every rule over the given mutations (default: the last week's). Awards each title and skill once per
        player and each legend once per deed. Returns notices for the world host."""
        notices = []
        defs = object_defs(con)
        if mutations is None:
            mutations = con.execute("SELECT * FROM world_mutations WHERE world_id = ? AND occurred_at > ? ORDER BY revision",
                                    (world_id, s.now() - 7 * 86400)).fetchall()
        for rule in RULES["rules"]:
            for m in mutations:
                if not m["player_id"]:
                    continue
                needs_mutation = "mutation" in rule["requires"]
                if needs_mutation and m["type"] not in rule["requires"]["mutation"]:
                    continue
                facts = {}
                if not check(con, rule["requires"], m, defs, facts):
                    continue
                authors = facts.get("authors_ids") or [m["player_id"]]
                winners = authors if rule.get("award_all_contributors") else [m["player_id"]]
                legend_id = None
                spec = rule.get("legend")
                if spec:
                    if spec.get("passage"):   # a legend that needs other people to have lived with the change
                        others = users_of_passage_after(con, spec["passage"], m["occurred_at"], held_until(con, m), set(authors))
                        facts["others"] = len(others)
                        facts["affected_ids"] = others
                    if facts.get("others", 0) >= spec.get("needs_others", 0):
                        legend_id, created = make_legend(con, rule, m, defs, facts, authors)
                        if created:
                            notices.append({"kind": "legend", "text": f"A LEGEND WAS BORN · {spec['name']}", "legend_id": legend_id})
                for player in winners:
                    if not player:
                        continue
                    nick = nick_of(con, player)
                    if rule.get("title"):
                        cur = con.execute("INSERT OR IGNORE INTO player_titles VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                                          (world_id, player, rule["id"], rule["title"], rule["reason"],
                                           json.dumps(facts.get("chain") or [m["id"]]), legend_id, s.now()))
                        if cur.rowcount:
                            notices.append({"kind": "title", "player_id": player,
                                            "text": f"YOUR LEGEND UPDATED · {nick} is now known as '{rule['title']}'"})
                    if rule.get("skill"):
                        cur = con.execute("INSERT OR IGNORE INTO player_skills VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                                          (world_id, player, rule["skill"], rule["id"], rule["reason"],
                                           json.dumps(facts.get("chain") or [m["id"]]), legend_id, s.now()))
                        if cur.rowcount:
                            notices.append({"kind": "skill", "player_id": player,
                                            "text": f"NEW SKILL · {SKILLS[rule['skill']]['name']} — {rule['reason']}"})
        return notices

    def make_legend(con, rule, m, defs, facts, authors):
        spec = rule["legend"]
        existing = con.execute("SELECT id FROM legends WHERE world_id = ? AND rule_id = ? AND anchor_mutation_id = ?",
                               (world_id, rule["id"], m["id"])).fetchone()
        if existing:
            return existing["id"], False
        chain = facts.get("chain") or [m["id"]]
        # One legend per chain: a later deed of the same chain does not start another one within a day.
        if len(chain) > 1:
            overlap = con.execute(f"SELECT id FROM legends WHERE world_id = ? AND rule_id = ? AND recognized_at > ?",
                                  (world_id, rule["id"], s.now() - 86400)).fetchall()
            for row in overlap:
                ids = set(json.loads(con.execute("SELECT mutation_ids FROM legends WHERE id = ?", (row["id"],)).fetchone()["mutation_ids"]))
                if ids & set(chain):
                    return row["id"], False
        muts = con.execute(f"SELECT * FROM world_mutations WHERE id IN ({','.join('?' * len(chain))}) ORDER BY revision", chain).fetchall()
        objects = sorted({x["object_id"] for x in muts})
        sectors = sorted({x["sector_id"] for x in muts})
        names = ", ".join(object_name(defs, o) for o in objects)
        author_nicks = [nick_of(con, a) for a in authors]
        until = held_until(con, m)
        first_of_kind = con.execute("SELECT COUNT(*) AS n FROM legends WHERE world_id = ? AND rule_id = ?", (world_id, rule["id"])).fetchone()["n"] == 0
        signals = {"objects_changed": len(objects), "other_people_affected": facts.get("others", 0), "deeds_in_chain": len(muts),
                   "co_authors": len(set(authors)), "hours_in_effect": round((until - m["occurred_at"]) / 3600, 1),
                   "first_of_its_kind": first_of_kind}
        impact = (10 * len(objects) + 8 * facts.get("others", 0) + 5 * len(muts) + 6 * len(set(authors))
                  + 2 * min((until - m["occurred_at"]) / 3600, 24) + (10 if first_of_kind else 5))
        tier = min(3, spec.get("tier", 1) + (1 if impact >= 60 else 0))
        story = spec["story"].format(author=nick_of(con, m["player_id"]), object=object_name(defs, m["object_id"]),
                                     others=facts.get("others", 0), authors=" and ".join(author_nicks),
                                     breaker=nick_of(con, facts.get("breaker_id")))
        legend_id = str(uuid.uuid4())
        con.execute("INSERT INTO legends VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    (legend_id, world_id, rule["id"], m["id"], spec["name"], story, tier, impact, json.dumps([x["id"] for x in muts]),
                     json.dumps(objects), json.dumps(sectors), json.dumps(signals), muts[0]["occurred_at"], s.now(), WORLD_SCHEMA_VERSION))
        for x in muts:
            if not x["player_id"]:
                continue
            role = "initiator" if x["id"] == muts[0]["id"] else "finisher" if x["id"] == muts[-1]["id"] and len(muts) > 1 else "co-author"
            if x["type"] in ("BRIDGE_DESTROYED", "WALL_BREACHED", "BARRICADE_REMOVED", "GRATE_CUT_OPEN", "POWER_OFF"):
                role = "breaker" if rule["id"] == "impossible-repair" else role
            if x["type"] in ("BRIDGE_REBUILT", "WALL_REBUILT") and rule["id"] == "impossible-repair":
                role = "restorer"
            con.execute("INSERT INTO legend_roles VALUES (?, ?, ?, ?, ?)", (legend_id, x["player_id"], x["player_nick"], role, json.dumps([x["id"]])))
        if facts.get("breaker_id") and rule["id"] == "impossible-repair":
            prev = con.execute("SELECT id, player_nick FROM world_mutations WHERE world_id = ? AND object_id = ? AND revision < ? "
                               "ORDER BY revision DESC LIMIT 1", (world_id, m["object_id"], m["revision"])).fetchone()
            if prev:
                con.execute("INSERT INTO legend_roles VALUES (?, ?, ?, 'breaker', ?)",
                            (legend_id, facts["breaker_id"], prev["player_nick"], json.dumps([prev["id"]])))
        print(f"[world] legend born: {spec['name']} ({story})")
        return legend_id, True

    # ------------------------------------------------------------------ internal API (the world host)

    def internal_only(request: Request):
        if not hmac.compare_digest(request.headers.get("x-internal-key", ""), config.internal_key()):
            raise HTTPException(403, "wrong key")

    class Definitions(BaseModel):
        name: str = ""
        sectors: list = []
        objects: list = []
        passages: list = []
        connectivity: list = []
        content_version: str = ""

    @internal.post("/internal/world/definitions")
    def register_definitions(body: Definitions, request: Request):
        internal_only(request)
        with s.Transaction() as con:
            ensure_world(con)
            con.execute("UPDATE worlds SET definitions = ? WHERE id = ?", (body.model_dump_json(), world_id))
            added = 0
            for o in body.objects:
                if o.get("type") not in TRANSITIONS:
                    continue
                cur = con.execute("INSERT OR IGNORE INTO world_objects (world_id, object_id, state) VALUES (?, ?, ?)",
                                  (world_id, o["id"], o["initial"]))
                added += cur.rowcount
        if added:
            print(f"[world] {added} new world object(s) registered")
        return {"ok": True}

    @internal.get("/internal/world/state")
    def world_state(request: Request):
        internal_only(request)
        with s.DB_LOCK:
            con = db()
            world = ensure_world(con)
            objects = []
            defs = object_defs(con)
            for o in con.execute("SELECT o.*, w.player_nick, w.player_id AS changed_by_id, w.occurred_at, w.type AS mutation_type "
                                 "FROM world_objects o LEFT JOIN world_mutations w ON w.id = o.last_mutation_id WHERE o.world_id = ?", (world_id,)):
                objects.append({"id": o["object_id"], "state": o["state"], "owner_id": o["owner_id"] or "", "owner_label": o["owner_label"] or "",
                                 "revision": o["revision"], "changed_by": (o["player_nick"] if o["changed_by_id"] else "a former player") if o["occurred_at"] else "",
                                 "changed_day": world_day(world["created_at"], o["occurred_at"]) if o["occurred_at"] else 0,
                                 "changed_type": o["mutation_type"] or ""})
            avatars = [dict(a) for a in con.execute("SELECT a.*, u.nick FROM avatars a JOIN users u ON u.id = a.player_id WHERE a.world_id = ?", (world_id,))]
            profiles = {r["subject"]: json.loads(r["data"]) for r in con.execute("SELECT subject, data FROM behavior_profiles WHERE world_id = ?", (world_id,))}
            return {"world_id": world_id, "name": world["name"], "revision": world["revision"], "created_at": world["created_at"],
                    "day": world_day(world["created_at"], s.now()), "objects": objects, "avatars": avatars, "profiles": profiles,
                    "skills": list(SKILLS.values())}

    class HostPlayer(BaseModel):
        user_id: str
        state: str = "human"       # human, grace, echo, echo_defeated
        sector: str = ""

    class WorldBeat(BaseModel):
        awake: bool = False
        players: list[HostPlayer] = []
        version: str = ""

    @internal.post("/internal/world/heartbeat")
    def world_heartbeat(body: WorldBeat, request: Request):
        internal_only(request)
        t = s.now()
        mono = time.monotonic()
        with lock:
            for p in body.players:
                presence[p.user_id] = {"state": p.state, "sector": p.sector, "at": mono}
            gap = mono - host_state["at"]
            was_awake = host_state["awake"] and gap < 10
            host_state["at"] = mono
            host_state["awake"] = body.awake
            notices, host_state["notices"] = host_state["notices"], []
        with s.Transaction() as con:
            world = ensure_world(con)
            state = "awake" if body.awake else "asleep"
            if world["lifecycle_state"] != state:
                lifecycle(con, "AWAKE" if body.awake else "SLEEP", f"{len(body.players)} character(s) in the world")
            add = gap if (was_awake and body.awake and gap < 10) else 0
            con.execute("UPDATE worlds SET lifecycle_state = ?, last_heartbeat_at = ?, total_awake_seconds = total_awake_seconds + ?, "
                        "last_awake_at = CASE WHEN ? THEN ? ELSE last_awake_at END WHERE id = ?",
                        (state, t, add, body.awake, t, world_id))
            if add:
                # The checkpoint's holder holds it only while the world is awake.
                con.execute("UPDATE world_objects SET held_awake_seconds = held_awake_seconds + ? WHERE world_id = ? AND state = 'held'",
                            (add, world_id))
            world = ensure_world(con)
            players = {}
            for p in body.players:
                skills = [r["skill_id"] for r in con.execute("SELECT skill_id FROM player_skills WHERE world_id = ? AND player_id = ?",
                                                             (world_id, p.user_id))]
                titles = [r["title"] for r in con.execute("SELECT title FROM player_titles WHERE world_id = ? AND player_id = ? ORDER BY unlocked_at",
                                                          (world_id, p.user_id))]
                squad = con.execute("SELECT squad_id FROM world_squad_members WHERE player_id = ?", (p.user_id,)).fetchone()
                players[p.user_id] = {"skills": skills, "titles": titles, "squad": squad["squad_id"] if squad else ""}
            if add and int(t) % 60 < 2:
                notices += evaluate(con)   # held checkpoints and late passage counts, about once a minute
        return {"revision": world["revision"], "day": world_day(world["created_at"], t), "players": players,
                "notices": notices, "shutdown": host_state["shutdown"]}

    class ActionIn(BaseModel):
        id: str
        player_id: str
        type: str
        object_id: str = ""
        verb: str = ""
        sector_id: str = ""
        x: float = 0
        y: float = 0
        z: float = 0
        data: dict = {}
        session_id: str = ""

    class MutationIn(BaseModel):
        action: ActionIn
        contributors: list = []     # [{"player_id", "role", "action_id"}] who else really did part of it

    @internal.post("/internal/world/mutation")
    def commit_mutation(body: MutationIn, request: Request):
        """The single place a world change becomes canonical. Refuses anything not done by a person the host reports
        connected right now, anything the object's kind does not allow, and anything that would cut a district off."""
        internal_only(request)
        a = body.action
        with lock:
            here = presence.get(a.player_id)
        if here is None or here["state"] != "human" or time.monotonic() - here["at"] > PRESENCE_SECONDS:
            raise HTTPException(403, "Only a connected player can change the world.")
        t = s.now()
        with s.Transaction() as con:
            done = con.execute("SELECT * FROM world_mutations WHERE action_id = ?", (a.id,)).fetchone()
            if done is not None:   # the same deed sent twice: the same answer, no second change
                return {"ok": True, "revision": done["revision"], "mutation": mutation_view(con, done), "notices": []}
            user = con.execute("SELECT * FROM users WHERE id = ?", (a.player_id,)).fetchone()
            if user is None:
                raise HTTPException(403, "Unknown player.")
            defs = object_defs(con)
            d = defs.get(a.object_id)
            obj = con.execute("SELECT * FROM world_objects WHERE world_id = ? AND object_id = ?", (world_id, a.object_id)).fetchone()
            if d is None or obj is None:
                raise HTTPException(404, "No such object in this world.")
            if d.get("immutable"):
                raise HTTPException(409, "This part of the world cannot be changed.")
            move = TRANSITIONS[d["type"]].get((obj["state"], a.verb))
            if move is None:
                raise HTTPException(409, f"{d['name']} cannot be changed that way now.")
            after, mutation_type, effects = move
            if mutation_type == "CHECKPOINT_CLAIMED" and obj["owner_id"] == a.player_id:
                raise HTTPException(409, f"{d['name']} is yours already.")
            last = con.execute("SELECT occurred_at FROM world_mutations WHERE world_id = ? AND object_id = ? ORDER BY revision DESC LIMIT 1",
                               (world_id, a.object_id)).fetchone()
            if last and t - last["occurred_at"] < OBJECT_COOLDOWN:
                raise HTTPException(429, f"{d['name']} was just changed. Wait a moment.")
            mine = con.execute("SELECT occurred_at FROM world_mutations WHERE world_id = ? AND player_id = ? ORDER BY revision DESC LIMIT 1",
                               (world_id, a.player_id)).fetchone()
            if mine and t - mine["occurred_at"] < PLAYER_COOLDOWN:
                raise HTTPException(429, "You just changed the world. Catch your breath.")
            # No district may be cut off: every connection must keep at least one way open.
            states = {r["object_id"]: r["state"] for r in con.execute("SELECT object_id, state FROM world_objects WHERE world_id = ?", (world_id,))}
            states[a.object_id] = after
            for link in definitions(con).get("connectivity", []):
                if not any(states.get(way["object"]) in way["states"] for way in link["any_of"]):
                    raise HTTPException(409, f"That would cut {link['name']} off from the world. Open another way first.")
            con.execute("INSERT OR IGNORE INTO player_actions VALUES (?, ?, ?, 'INTERACT_COMPLETED', ?, ?, ?, ?, ?, ?, ?)",
                        (a.id, world_id, a.player_id, a.sector_id, a.object_id, a.x, a.y, a.z, json.dumps({"verb": a.verb, **a.data}), t))
            world = ensure_world(con)
            revision = world["revision"] + 1
            contributors = [{"player_id": a.player_id, "nick": user["nick"], "role": "author", "action_id": a.id}]
            for c in body.contributors:
                if c.get("player_id") and c["player_id"] != a.player_id:
                    cu = con.execute("SELECT nick FROM users WHERE id = ?", (c["player_id"],)).fetchone()
                    if cu:
                        contributors.append({"player_id": c["player_id"], "nick": cu["nick"], "role": clean_label(c.get("role", "helper"), 24),
                                             "action_id": c.get("action_id", "")})
            mutation_id = str(uuid.uuid4())
            con.execute("INSERT INTO world_mutations VALUES (?, ?, ?, 'HUMAN', ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                        (mutation_id, world_id, revision, a.player_id, user["nick"], a.id, a.session_id, mutation_type, a.verb,
                         a.object_id, d["sector"], a.x, a.y, a.z, obj["state"], after, obj["owner_id"], json.dumps(effects),
                         json.dumps(contributors), json.dumps([obj["last_mutation_id"]] if obj["last_mutation_id"] else []), t,
                         WORLD_SCHEMA_VERSION))
            owner_id = a.player_id if mutation_type == "CHECKPOINT_CLAIMED" else None
            owner_label = user["nick"] if owner_id else None
            con.execute("UPDATE world_objects SET state = ?, owner_id = ?, owner_label = ?, revision = ?, last_mutation_id = ?, "
                        "held_awake_seconds = 0 WHERE world_id = ? AND object_id = ?",
                        (after, owner_id, owner_label, revision, mutation_id, world_id, a.object_id))
            con.execute("UPDATE worlds SET revision = ?, last_human_change_at = ? WHERE id = ?", (revision, t, world_id))
            m = con.execute("SELECT * FROM world_mutations WHERE id = ?", (mutation_id,)).fetchone()
            view = mutation_view(con, m, defs)
        print(f"[world] r{revision}: {view['summary']}")
        with s.Transaction() as con:
            notices = evaluate(con, [con.execute("SELECT * FROM world_mutations WHERE id = ?", (mutation_id,)).fetchone()])
        return {"ok": True, "revision": revision, "mutation": view, "notices": notices}

    class Batch(BaseModel):
        items: list = []

    @internal.post("/internal/world/actions")
    def record_actions(body: Batch, request: Request):
        internal_only(request)
        passages = 0
        with s.Transaction() as con:
            for a in body.items[:2000]:
                if not a.get("id") or not a.get("player_id") or not a.get("type"):
                    continue
                con.execute("INSERT OR IGNORE INTO player_actions VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                            (a["id"], world_id, a["player_id"], clean_label(a["type"], 32), a.get("sector_id", ""), a.get("object_id", ""),
                             a.get("x", 0), a.get("y", 0), a.get("z", 0), json.dumps(a.get("data", {}))[:2000], a.get("at") or s.now()))
                passages += a["type"] == "PASSAGE_USED"
            notices = evaluate(con) if passages else []
        if notices:
            with lock:
                host_state["notices"] += notices
        return {"ok": True}

    @internal.post("/internal/world/traces")
    def record_traces(body: Batch, request: Request):
        internal_only(request)
        with s.Transaction() as con:
            for tr in body.items[:500]:
                con.execute("INSERT INTO movement_traces (world_id, player_id, sector_id, started_at, ended_at, points) VALUES (?, ?, ?, ?, ?, ?)",
                            (world_id, tr["player_id"], tr.get("sector_id", ""), tr["started_at"], tr["ended_at"], str(tr["points"])[:20000]))
        return {"ok": True}

    @internal.post("/internal/world/profiles")
    def record_profiles(body: Batch, request: Request):
        internal_only(request)
        with s.Transaction() as con:
            for p in body.items[:500]:
                data = json.dumps(p.get("data", {}))
                if len(data) > 60000:
                    continue
                con.execute("INSERT INTO behavior_profiles VALUES (?, ?, ?, ?) ON CONFLICT(world_id, subject) DO UPDATE SET data = excluded.data, "
                            "updated_at = excluded.updated_at", (world_id, clean_label(p["subject"], 80), data, s.now()))
        return {"ok": True}

    @internal.post("/internal/world/avatars")
    def record_avatars(body: Batch, request: Request):
        internal_only(request)
        with s.Transaction() as con:
            for a in body.items[:500]:
                con.execute("INSERT INTO avatars VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?) ON CONFLICT(world_id, player_id) DO UPDATE SET "
                            "x = excluded.x, y = excluded.y, z = excluded.z, yaw = excluded.yaw, sector_id = excluded.sector_id, "
                            "health = excluded.health, weapon = excluded.weapon, echo_state = excluded.echo_state, intent = excluded.intent, "
                            "last_human_at = COALESCE(excluded.last_human_at, avatars.last_human_at), updated_at = excluded.updated_at",
                            (world_id, a["player_id"], a["x"], a["y"], a["z"], a.get("yaw", 0), a.get("sector_id", ""), a.get("health", 100),
                             a.get("weapon", ""), a.get("echo_state", "offline"), clean_label(a.get("intent", ""), 80), a.get("last_human_at"), s.now()))
        return {"ok": True}

    @internal.post("/internal/world/echo-diary")
    def record_echo_diary(body: Batch, request: Request):
        internal_only(request)
        with s.Transaction() as con:
            for e in body.items[:500]:
                con.execute("INSERT INTO echo_diary (world_id, player_id, kind, text, sector_id, occurred_at) VALUES (?, ?, ?, ?, ?, ?)",
                            (world_id, e["player_id"], clean_label(e.get("kind", ""), 24), clean_label(e.get("text", ""), 200),
                             e.get("sector_id", ""), e.get("at") or s.now()))
        return {"ok": True}

    class LifecycleIn(BaseModel):
        kind: str
        note: str = ""

    @internal.post("/internal/world/lifecycle")
    def record_lifecycle(body: LifecycleIn, request: Request):
        internal_only(request)
        if body.kind not in ("SHUTDOWN", "AWAKE", "SLEEP", "HOST_STARTED"):
            raise HTTPException(422, "unknown lifecycle kind")
        with s.Transaction() as con:
            lifecycle(con, body.kind, clean_label(body.note, 200))
            if body.kind == "SHUTDOWN":
                con.execute("UPDATE worlds SET lifecycle_state = 'offline' WHERE id = ?", (world_id,))
        return {"ok": True}

    @internal.post("/internal/world/request-shutdown")
    def request_shutdown(request: Request):
        """scripts/world-stop.ps1: the host tells everyone, saves the characters and confirms with SHUTDOWN."""
        internal_only(request)
        host_state["shutdown"] = True
        return {"ok": True, "host_alive": time.monotonic() - host_state["at"] < 10}

    # ------------------------------------------------------------------ public API

    def page_limit(limit):
        return max(1, min(int(limit or config.world_history_limit), 100))

    def cors(body):
        return JSONResponse(body, headers={"Access-Control-Allow-Origin": "*", "Cache-Control": "no-store"})

    @app.get("/world/status")
    def public_status():
        with s.DB_LOCK:
            return cors(status_view(db()))

    @app.get("/world/map")
    def public_map():
        with s.DB_LOCK:
            con = db()
            world = ensure_world(con)
            defs = definitions(con)
            objects = []
            for o in con.execute("SELECT o.*, w.player_nick, w.player_id AS by_id, w.occurred_at, w.type FROM world_objects o "
                                 "LEFT JOIN world_mutations w ON w.id = o.last_mutation_id WHERE o.world_id = ?", (world_id,)):
                d = next((x for x in defs.get("objects", []) if x["id"] == o["object_id"]), None)
                if d is None:
                    continue
                changes = con.execute("SELECT COUNT(*) AS n FROM world_mutations WHERE world_id = ? AND object_id = ?",
                                      (world_id, o["object_id"])).fetchone()["n"]
                objects.append({"id": o["object_id"], "name": d["name"], "type": d["type"], "sector": d["sector"],
                                "position": [d.get("x", 0), d.get("y", 0), d.get("z", 0)], "state": o["state"],
                                "owner": o["owner_label"] or "", "changes": changes,
                                "last_changed_by": (o["player_nick"] if o["by_id"] else "a former player") if o["occurred_at"] else "",
                                "last_changed_day": world_day(world["created_at"], o["occurred_at"]) if o["occurred_at"] else 0,
                                "last_change": o["type"] or ""})
            return cors({"world_id": world_id, "sectors": defs.get("sectors", []), "objects": objects,
                         "bounds": defs.get("bounds", [])})

    @app.get("/world/history")
    def public_history(sector: str = "", player: str = "", type: str = "", object: str = "", cursor: int = 0, limit: int = 0):
        n = page_limit(limit)
        where, args = ["world_id = ?"], [world_id]
        if sector:
            where.append("sector_id = ?"); args.append(sector)
        if player:
            where.append("player_id = ?"); args.append(player)
        if object:
            where.append("object_id = ?"); args.append(object)
        if type:
            kinds = {"built": ("BRIDGE_REBUILT", "BARRICADE_BUILT", "WALL_REBUILT", "MEMORIAL_RAISED"),
                     "destroyed": ("BRIDGE_DESTROYED", "BARRICADE_REMOVED", "WALL_BREACHED"),
                     "opened": ("GATE_OPENED", "GRATE_CUT_OPEN", "WALL_BREACHED"), "closed": ("GATE_CLOSED", "GRATE_WELDED"),
                     "power": ("POWER_OFF", "POWER_ON"), "control": ("CHECKPOINT_CLAIMED",),
                     "restored": ("BRIDGE_REBUILT", "WALL_REBUILT", "POWER_ON")}.get(type.lower(), (type.upper(),))
            where.append(f"type IN ({','.join('?' * len(kinds))})"); args += list(kinds)
        if cursor:
            where.append("revision < ?"); args.append(cursor)
        with s.DB_LOCK:
            con = db()
            defs = object_defs(con)
            rows = con.execute(f"SELECT * FROM world_mutations WHERE {' AND '.join(where)} ORDER BY revision DESC LIMIT ?", (*args, n)).fetchall()
            items = [mutation_view(con, m, defs) for m in rows]
        return cors({"items": items, "next_cursor": items[-1]["revision"] if len(items) == n else 0})

    @app.get("/world/objects/{object_id}/history")
    def object_history(object_id: str, cursor: int = 0, limit: int = 0):
        with s.DB_LOCK:
            con = db()
            world = ensure_world(con)
            defs = object_defs(con)
            d = defs.get(object_id)
            o = con.execute("SELECT * FROM world_objects WHERE world_id = ? AND object_id = ?", (world_id, object_id)).fetchone()
            if d is None or o is None:
                raise HTTPException(404, "No such object.")
            args = [world_id, object_id]
            extra = ""
            if cursor:
                extra = " AND revision < ?"; args.append(cursor)
            rows = con.execute(f"SELECT * FROM world_mutations WHERE world_id = ? AND object_id = ?{extra} ORDER BY revision DESC LIMIT ?",
                               (*args, page_limit(limit))).fetchall()
            legends = [legend_view(con, l, roles=False) for l in con.execute(
                "SELECT * FROM legends WHERE world_id = ? AND object_ids LIKE ? ORDER BY recognized_at DESC LIMIT 5", (world_id, f'%"{object_id}"%'))]
            return cors({"id": object_id, "name": d["name"], "type": d["type"], "sector": d["sector"], "sector_name": sector_name(con, d["sector"]),
                         "state": o["state"], "owner": o["owner_label"] or "", "initial": d.get("initial", ""),
                         "history": [mutation_view(con, m, defs) for m in rows], "legends": legends,
                         "day": world_day(world["created_at"], s.now())})

    @app.get("/world/legends")
    def public_legends(sector: str = "", player: str = "", cursor: float = 0, limit: int = 0):
        where, args = ["l.world_id = ?"], [world_id]
        if sector:
            where.append("l.sector_ids LIKE ?"); args.append(f'%"{sector}"%')
        if player:
            where.append("l.id IN (SELECT legend_id FROM legend_roles WHERE player_id = ?)"); args.append(player)
        if cursor:
            where.append("l.recognized_at < ?"); args.append(cursor)
        n = page_limit(limit)
        with s.DB_LOCK:
            con = db()
            rows = con.execute(f"SELECT l.* FROM legends l WHERE {' AND '.join(where)} ORDER BY l.epic_tier DESC, l.recognized_at DESC LIMIT ?",
                               (*args, n)).fetchall()
            items = [legend_view(con, l) for l in rows]
        return cors({"items": items})

    @app.get("/world/legends/{legend_id}")
    def public_legend(legend_id: str):
        with s.DB_LOCK:
            con = db()
            legend = con.execute("SELECT * FROM legends WHERE id = ? AND world_id = ?", (legend_id, world_id)).fetchone()
            if legend is None:
                raise HTTPException(404, "No such legend.")
            view = legend_view(con, legend)
            defs = object_defs(con)
            ids = json.loads(legend["mutation_ids"])
            view["deeds"] = [mutation_view(con, m, defs) for m in con.execute(
                f"SELECT * FROM world_mutations WHERE id IN ({','.join('?' * len(ids))}) ORDER BY revision", ids)] if ids else []
            return cors(view)

    def impact_view(con, player_id: str, private: bool) -> dict:
        world = ensure_world(con)
        defs = object_defs(con)
        user = con.execute("SELECT id, nick FROM users WHERE id = ?", (player_id,)).fetchone()
        if user is None:
            raise HTTPException(404, "No such player.")
        muts = con.execute("SELECT * FROM world_mutations WHERE world_id = ? AND player_id = ? ORDER BY revision DESC", (world_id, player_id)).fetchall()
        views = [mutation_view(con, m, defs) for m in muts]
        counts = {}
        for v in views:
            counts[v["type"]] = counts.get(v["type"], 0) + 1
        co = {}
        for m in muts:
            for c in json.loads(m["contributors"]):
                if c.get("player_id") and c["player_id"] != player_id:
                    co[c["nick"]] = co.get(c["nick"], 0) + 1
        for r in con.execute("SELECT DISTINCT lr2.player_nick FROM legend_roles lr JOIN legend_roles lr2 ON lr2.legend_id = lr.legend_id "
                             "WHERE lr.player_id = ? AND lr2.player_id != ?", (player_id, player_id)):
            co[r["player_nick"]] = co.get(r["player_nick"], 0) + 1
        titles = [{"title": t["title"], "reason": t["reason"], "legend_id": t["legend_id"] or "", "unlocked_at": s.iso(t["unlocked_at"]),
                   "deeds": json.loads(t["mutation_ids"])}
                  for t in con.execute("SELECT * FROM player_titles WHERE world_id = ? AND player_id = ? ORDER BY unlocked_at", (world_id, player_id))]
        skills = [{"id": k["skill_id"], "name": SKILLS.get(k["skill_id"], {}).get("name", k["skill_id"]),
                   "description": SKILLS.get(k["skill_id"], {}).get("description", ""), "key": SKILLS.get(k["skill_id"], {}).get("key", ""),
                   "cooldown": SKILLS.get(k["skill_id"], {}).get("cooldown", 0), "reason": k["reason"], "unlocked_at": s.iso(k["unlocked_at"])}
                  for k in con.execute("SELECT * FROM player_skills WHERE world_id = ? AND player_id = ? ORDER BY unlocked_at", (world_id, player_id))]
        legends = [legend_view(con, l) for l in con.execute(
            "SELECT DISTINCT l.* FROM legends l JOIN legend_roles r ON r.legend_id = l.id WHERE r.player_id = ? ORDER BY l.recognized_at DESC", (player_id,))]
        learned = []
        profile = con.execute("SELECT data FROM behavior_profiles WHERE world_id = ? AND subject = ?", (world_id, "player:" + player_id)).fetchone()
        style = ""
        if profile:
            data = json.loads(profile["data"])
            style = data.get("style", "")
            learned = data.get("taught", [])
        result = {
            "player_id": player_id, "nick": user["nick"], "titles": titles, "skills": skills, "legends": legends,
            "counts": counts, "changes_total": len(views),
            "still_standing": [v for v in views if v["still_current"]][:20],
            "changed_by_others": [v for v in views if not v["still_current"]][:20],
            "co_authors": [{"nick": k, "times": n} for k, n in sorted(co.items(), key=lambda x: -x[1])][:10],
            "bots_learned": learned, "echo_style": style,
        }
        if private:
            avatar = con.execute("SELECT * FROM avatars WHERE world_id = ? AND player_id = ?", (world_id, player_id)).fetchone()
            here = presence.get(player_id)
            live = here is not None and time.monotonic() - here["at"] < PRESENCE_SECONDS
            result["character"] = {
                "status": ("Online" if here["state"] == "human" else "Echo active" if here["state"] == "echo"
                           else "Echo defeated" if here["state"] == "echo_defeated" else "Connecting") if live
                else ("Echo sleeping" if avatar and avatar["echo_state"] in ("echo", "sleeping") else
                      "Echo defeated" if avatar and avatar["echo_state"] == "echo_defeated" else "Offline"),
                "sector": sector_name(con, avatar["sector_id"]) if avatar else "",
                "last_controlled_at": s.iso(avatar["last_human_at"]) if avatar and avatar["last_human_at"] else None,
                "exists": avatar is not None,
            }
            result["echo_diary"] = [{"kind": e["kind"], "text": e["text"], "sector": sector_name(con, e["sector_id"]),
                                     "at": s.iso(e["occurred_at"]), "day": world_day(world["created_at"], e["occurred_at"])}
                                    for e in con.execute("SELECT * FROM echo_diary WHERE world_id = ? AND player_id = ? ORDER BY occurred_at DESC LIMIT 30",
                                                         (world_id, player_id))]
            since = avatar["last_human_at"] if avatar and avatar["last_human_at"] else 0
            changes = con.execute("SELECT * FROM world_mutations WHERE world_id = ? AND occurred_at > ? AND (player_id IS NULL OR player_id != ?) "
                                  "ORDER BY revision DESC LIMIT 6", (world_id, since, player_id)).fetchall() if since else []
            result["since_you_were_here"] = [mutation_view(con, m, defs)["summary"] for m in changes]
        return result

    @app.get("/world/players/{player_id}/impact")
    def public_impact(player_id: str):
        with s.DB_LOCK:
            return cors(impact_view(db(), player_id, private=False))

    @app.get("/world/me/impact")
    def my_impact(user=Depends(s.current_user)):
        with s.DB_LOCK:
            return impact_view(db(), user["id"], private=True)

    @app.get("/me/legend")
    def my_legend(user=Depends(s.current_user)):
        with s.DB_LOCK:
            return impact_view(db(), user["id"], private=True)

    @app.get("/me/echo")
    def my_echo(user=Depends(s.current_user)):
        with s.DB_LOCK:
            view = impact_view(db(), user["id"], private=True)
            return {"character": view["character"], "echo_style": view["echo_style"], "echo_diary": view["echo_diary"]}

    @app.get("/players/online")
    def world_players(user=Depends(s.current_user)):
        mono = time.monotonic()
        with s.DB_LOCK:
            con = db()
            items = []
            for player_id, v in list(presence.items()):
                if mono - v["at"] > PRESENCE_SECONDS:
                    continue
                items.append({"player_id": player_id, "nick": nick_of(con, player_id), "kind": "echo" if v["state"].startswith("echo") else "human",
                              "state": v["state"], "sector": sector_name(con, v["sector"]),
                              "label": f"AI ECHO OF {nick_of(con, player_id).upper()}" if v["state"].startswith("echo") else nick_of(con, player_id)})
            return {"items": items}

    @app.post("/world/connect-ticket")
    def connect_ticket(request: Request, user=Depends(s.current_user)):
        s.check_version(request)
        s.limit(request, "world-ticket", 30, 60, user["id"])
        slot = config.world_slot
        if not slot:
            raise HTTPException(503, "The Living World is not set up on this server.")
        if time.monotonic() - host_state["at"] > 10:
            raise HTTPException(503, "The world's server is not running right now. Try again later.")
        room = {"id": world_id, "slot_id": "world"}
        return {"udp_host": slot["public_udp_host"], "udp_port": slot["public_udp_port"],
                "join_ticket": s.make_ticket(room, user, "world"), "world_id": world_id, "protocol": s.PROTOCOL_VERSION}

    # Squads: a few people who agree to stand together (no friendly fire, a shared compass).
    class SquadInvite(BaseModel):
        user_id: str = ""

    @app.get("/world/squad")
    def my_squad(user=Depends(s.current_user)):
        with s.DB_LOCK:
            con = db()
            row = con.execute("SELECT squad_id FROM world_squad_members WHERE player_id = ?", (user["id"],)).fetchone()
            members = []
            if row:
                members = [{"player_id": r["player_id"], "nick": r["nick"]} for r in con.execute(
                    "SELECT m.player_id, u.nick FROM world_squad_members m JOIN users u ON u.id = m.player_id WHERE m.squad_id = ?", (row["squad_id"],))]
            invites = [{"id": r["id"], "from": r["nick"]} for r in con.execute(
                "SELECT i.id, u.nick FROM world_squad_invites i JOIN users u ON u.id = i.from_id WHERE i.to_id = ?", (user["id"],))]
            return {"squad_id": row["squad_id"] if row else "", "members": members, "invites": invites}

    @app.post("/world/squad/invite")
    def squad_invite(body: SquadInvite, request: Request, user=Depends(s.current_user)):
        s.limit(request, "squad-invite", 20, 600, user["id"])
        with s.Transaction() as con:
            if not s.are_friends(con, user["id"], body.user_id):
                raise HTTPException(403, "You can invite your friends only.")
            row = con.execute("SELECT squad_id FROM world_squad_members WHERE player_id = ?", (user["id"],)).fetchone()
            if row is None:
                squad_id = str(uuid.uuid4())
                con.execute("INSERT INTO world_squads VALUES (?, ?, ?, ?)", (squad_id, world_id, f"{user['nick']}'s squad", s.now()))
                con.execute("INSERT INTO world_squad_members VALUES (?, ?, ?)", (squad_id, user["id"], s.now()))
            else:
                squad_id = row["squad_id"]
            if con.execute("SELECT COUNT(*) AS n FROM world_squad_members WHERE squad_id = ?", (squad_id,)).fetchone()["n"] >= 6:
                raise HTTPException(409, "A squad has six people at most.")
            con.execute("DELETE FROM world_squad_invites WHERE squad_id = ? AND to_id = ?", (squad_id, body.user_id))
            con.execute("INSERT INTO world_squad_invites VALUES (?, ?, ?, ?, ?)", (str(uuid.uuid4()), squad_id, user["id"], body.user_id, s.now()))
        return {"ok": True}

    @app.post("/world/squad/invites/{invite_id}/accept")
    def squad_accept(invite_id: str, user=Depends(s.current_user)):
        with s.Transaction() as con:
            inv = con.execute("SELECT * FROM world_squad_invites WHERE id = ? AND to_id = ?", (invite_id, user["id"])).fetchone()
            if inv is None:
                raise HTTPException(404, "That invitation is gone.")
            con.execute("DELETE FROM world_squad_members WHERE player_id = ?", (user["id"],))
            con.execute("INSERT INTO world_squad_members VALUES (?, ?, ?)", (inv["squad_id"], user["id"], s.now()))
            con.execute("DELETE FROM world_squad_invites WHERE to_id = ?", (user["id"],))
        return {"ok": True}

    @app.post("/world/squad/leave")
    def squad_leave(user=Depends(s.current_user)):
        with s.Transaction() as con:
            con.execute("DELETE FROM world_squad_members WHERE player_id = ?", (user["id"],))
            con.execute("DELETE FROM world_squads WHERE id NOT IN (SELECT squad_id FROM world_squad_members)")
        return {"ok": True}

    # ------------------------------------------------------------------ for the server module

    def forget_player(con, player_id):
        """Account deleted: its name leaves the public history ('a former player'); what it did to the world stays."""
        con.execute("UPDATE world_mutations SET player_nick = 'a former player' WHERE player_id = ?", (player_id,))
        con.execute("UPDATE legend_roles SET player_nick = 'a former player' WHERE player_id = ?", (player_id,))
        con.execute("UPDATE world_objects SET owner_label = 'a former player' WHERE owner_id = ?", (player_id,))

    def snapshot(con):
        """A checkable picture of the world's state at its current revision (kept with each backup)."""
        world = ensure_world(con)
        objects = [dict(r) for r in con.execute("SELECT object_id, state, owner_id, revision, last_mutation_id FROM world_objects "
                                                 "WHERE world_id = ? ORDER BY object_id", (world_id,))]
        data = json.dumps({"world_id": world_id, "revision": world["revision"], "objects": objects}, sort_keys=True)
        checksum = hashlib.sha256(data.encode()).hexdigest()
        con.execute("INSERT INTO world_snapshots (world_id, up_to_revision, schema_version, created_at, checksum, data) VALUES (?, ?, ?, ?, ?, ?)",
                    (world_id, world["revision"], WORLD_SCHEMA_VERSION, s.now(), checksum, data))
        con.execute("DELETE FROM world_snapshots WHERE world_id = ? AND id NOT IN (SELECT id FROM world_snapshots WHERE world_id = ? "
                    "ORDER BY id DESC LIMIT 50)", (world_id, world_id))
        return world["revision"], checksum

    return {"forget_player": forget_player, "status": lambda: status_view(db()), "snapshot": snapshot, "world_id": world_id,
            "host_alive": lambda: time.monotonic() - host_state["at"] < 10}


def backup_database(source: sqlite3.Connection, database_path: Path, directories: list, keep: int, world_info: dict) -> Path:
    """A consistent copy with SQLite's online backup (correct with WAL, while the server runs), plus a manifest
    (world, revision, schema, checksum). Keeps the newest `keep` copies in each directory."""
    stamp = datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%S")
    first = None
    for directory in directories:
        if not directory:
            continue
        directory = Path(directory)
        directory.mkdir(parents=True, exist_ok=True)
        target = directory / f"lowstrike-{stamp}.sqlite3"
        if first is None:
            dest = sqlite3.connect(target)
            with dest:
                source.backup(dest)
            dest.close()
            first = target
        else:
            shutil.copy2(first, target)
        digest = hashlib.sha256(target.read_bytes()).hexdigest()
        manifest = dict(world_info, file=target.name, created_at=datetime.now(timezone.utc).isoformat(timespec="seconds"),
                        sha256=digest, database=str(database_path.name))
        target.with_suffix(".json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
        copies = sorted(directory.glob("lowstrike-*.sqlite3"))
        for old in copies[:-keep] if keep > 0 else []:
            old.unlink(missing_ok=True)
            old.with_suffix(".json").unlink(missing_ok=True)
    return first
