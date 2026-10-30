"""Starts the online server with a throw-away database and checks the whole API over HTTP: accounts, friends,
presence, rooms, tickets, invites, the game hosts' reports, the bots' knowledge, reviews, Tokens, the admin
panel, limits and account deletion.

Run:  python tests/smoke_test.py      (from online-server/, with the requirements installed)
"""

import base64
import socket
import email
import email.policy
import hashlib
import hmac
import json
import os
import re
import secrets
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
from pathlib import Path

from websockets.sync.client import connect as ws_connect

HERE = Path(__file__).resolve().parent.parent
PORT, INTERNAL = 18000, 18001
API = f"http://127.0.0.1:{PORT}"
CONTROL = f"http://127.0.0.1:{INTERNAL}"
failures = 0


def call(method, path, body=None, token=None, base=API, headers=None, raw=None):
    data = raw if raw is not None else (json.dumps(body).encode() if body is not None else None)
    request = urllib.request.Request(base + path, data=data, method=method)
    request.add_header("Content-Type", "application/json")
    request.add_header("X-Game-Version", "1.19")
    if token:
        request.add_header("Authorization", "Bearer " + token)
    for key, value in (headers or {}).items():
        request.add_header(key, value)
    try:
        with urllib.request.urlopen(request, timeout=20) as response:
            return response.status, json.loads(response.read() or b"{}")
    except urllib.error.HTTPError as e:
        return e.code, json.loads(e.read() or b"{}")


def headers_of(method, path, headers):
    """The answer's headers (CORS)."""
    request = urllib.request.Request(API + path, method=method)
    for key, value in headers.items():
        request.add_header(key, value)
    try:
        with urllib.request.urlopen(request, timeout=20) as response:
            return response.status, {k.lower(): v for k, v in response.headers.items()}
    except urllib.error.HTTPError as e:
        return e.code, {k.lower(): v for k, v in e.headers.items()}


def check(name, condition, detail=""):
    global failures
    print(("  ok    " if condition else "  FAIL  ") + name + ("" if condition else f"   {detail}"))
    if not condition:
        failures += 1


def main():
    global MAIL_DIR
    work = Path(tempfile.mkdtemp(prefix="lowstrike-test-"))
    MAIL_DIR = work / "mail"
    join_secret = secrets.token_urlsafe(48)
    auth_secret = secrets.token_urlsafe(48)
    logs = []

    def serve(admins, then):
        """Starts the server (ADMIN_NICKS=admins) on the test database, runs the checks, stops it."""
        env_file = work / "test.env"
        env_file.write_text("\n".join([
            "API_HOST=127.0.0.1", f"API_PORT={PORT}", f"INTERNAL_PORT={INTERNAL}", f"DATABASE_PATH={work / 'test.sqlite3'}",
            f"AUTH_SECRET={auth_secret}", f"GAME_JOIN_SECRET={join_secret}", "GAME_BUILD_VERSION=1.19",
            "REVIEW_ORIGINS=https://lowstrike.lokvetia.com", f"ADMIN_NICKS={admins}", f"MAIL_DIR={MAIL_DIR}",
            'ROOM_SLOTS_JSON=[{"id":"room-1","local_udp_port":27015,"public_udp_host":"test.example","public_udp_port":40000}]',
        ]), encoding="utf-8")
        env = dict(os.environ, LOWSTRIKE_ENV=str(env_file))
        for key in ("API_PORT", "INTERNAL_PORT", "DATABASE_PATH", "AUTH_SECRET", "GAME_JOIN_SECRET", "ROOM_SLOTS_JSON",
                    "UNITY_SERVER_EXECUTABLE", "ADMIN_NICKS"):
            env.pop(key, None)
        for key in [k for k in env if k.startswith(("SMTP_", "MAIL_"))]:
            env.pop(key)   # mails go to MAIL_DIR, never out
        log = work / f"server-{len(logs) + 1}.log"
        logs.append(log)
        server = subprocess.Popen([sys.executable, str(HERE / "lowstrike_server.py")], env=env, cwd=HERE,
                                  stdout=open(log, "w"), stderr=subprocess.STDOUT)
        try:
            for _ in range(100):
                try:
                    if call("GET", "/health")[0] == 200:
                        break
                except OSError:
                    time.sleep(0.2)
            then()
        finally:
            server.terminate()
            server.wait(10)

    serve("ghost_admin", lambda: run(join_secret))
    serve("alice,ghost_admin", run_admin)   # the same database: Alice (made in the first run) is now an admin
    text = "".join(log.read_text() for log in logs)
    check("the server log holds no password", "secret-pass" not in text and "Hunter2hunter" not in text and "carol-pass" not in text
          and "brand-new-pass" not in text and "admin-given-pass" not in text)
    check("nor a recovery code", not any(code in text for code in CODES_SEEN), CODES_SEEN)
    print(f"\n{'ALL PASSED' if failures == 0 else f'{failures} FAILED'}   (server logs: {work})")
    sys.exit(1 if failures else 0)


def beat(room_id="", state="lobby", players=(), key=None, match=0):
    internal_key = key or hmac.new(JOIN_SECRET.encode(), b"lowstrike-internal", hashlib.sha256).hexdigest()
    return call("POST", "/internal/slots/room-1/heartbeat", {
        "room_id": room_id, "state": state, "protocol": 5, "version": "1.19", "match_number": match, "terrorist_wins": 8,
        "swat_wins": 3, "map": "dune",
        "players": [{"user_id": p[0], "team": p[1], "connected": True, "kills": p[2] if len(p) > 2 else 0} for p in players]},
        base=CONTROL, headers={"X-Internal-Key": internal_key})


def run(join_secret):
    global JOIN_SECRET
    JOIN_SECRET = join_secret
    print("health and accounts")
    status, health = call("GET", "/health")
    check("health answers", status == 200 and health["status"] == "ok", health)
    check("no game host yet", health["servers"] == 0, health)

    check("short password refused", call("POST", "/auth/register", {"nick": "alice", "password": "short"})[0] == 422)
    check("bad nickname refused", call("POST", "/auth/register", {"nick": "a b", "password": "secret-pass"})[0] == 422)
    status, alice = call("POST", "/auth/register", {"nick": "Alice", "password": "secret-pass"})
    check("register Alice", status == 200 and alice["token"] and alice["user"]["nick"] == "Alice", alice)
    check("nickname taken (any case)", call("POST", "/auth/register", {"nick": "ALICE", "password": "secret-pass"})[0] == 409)
    status, bob = call("POST", "/auth/register", {"nick": "bob_2", "password": "Hunter2hunter"})
    check("register bob_2", status == 200, bob)
    check("user id is not the nickname", alice["user"]["id"] != "Alice" and len(alice["user"]["id"]) == 36)
    check("wrong password refused", call("POST", "/auth/login", {"nick": "alice", "password": "nope-nope"})[0] == 401)
    status, again = call("POST", "/auth/login", {"nick": "alice", "password": "secret-pass"})
    check("log in (nickname in another case)", status == 200 and again["user"]["id"] == alice["user"]["id"], again)
    a, b = alice["token"], bob["token"]
    a_id, b_id = alice["user"]["id"], bob["user"]["id"]
    check("no token: 401", call("GET", "/me")[0] == 401)
    check("made-up token: 401", call("GET", "/me", token="made-up")[0] == 401)

    status, me = call("PATCH", "/me", {"profile": {"skins": [{"weapon": "ak47", "skin": "web"}]}, "linked_local": True}, a)
    check("save skins and settings", status == 200 and me["profile"]["skins"][0]["skin"] == "web" and me["linked_local"], me)
    check("too big a profile refused", call("PATCH", "/me", {"profile": {"x": "y" * 9000}}, a)[0] == 413)
    check("request over 16 KB refused", call("PATCH", "/me", raw=b"{" + b" " * 20000 + b"}", token=a)[0] == 413)

    print("friends and presence")
    status, found = call("GET", "/players?search=bo", token=a)
    check("search finds bob_2", status == 200 and [p["nick"] for p in found["players"]] == ["bob_2"], found)
    check("request to nobody: 404", call("POST", "/friends/requests", {"nick": "nobody"}, a)[0] == 404)
    check("Alice asks bob_2", call("POST", "/friends/requests", {"nick": "bob_2"}, a)[0] == 200)
    status, friends = call("GET", "/friends", token=b)
    check("bob_2 sees the request", status == 200 and friends["incoming"][0]["nick"] == "Alice", friends)
    check("only bob_2 can accept it", call("POST", f"/friends/requests/{friends['incoming'][0]['id']}/accept", token=a)[0] == 404)
    check("bob_2 accepts", call("POST", f"/friends/requests/{friends['incoming'][0]['id']}/accept", token=b)[0] == 200)
    status, presence = call("GET", "/presence", token=a)
    check("Alice sees bob_2 online", presence["friends"] == [{"id": b_id, "nick": "bob_2", "status": "online", "played": 0}], presence)
    call("PATCH", "/me", {"hide_status": True}, b)
    status, presence = call("GET", "/presence", token=a)
    check("hidden bob_2 shows offline", presence["friends"][0]["status"] == "offline", presence)
    status, presence = call("GET", "/presence", token=b)
    check("hidden bob_2 sees no statuses", presence["friends"][0]["status"] == "hidden", presence)
    call("PATCH", "/me", {"hide_status": False}, b)

    print("rooms")
    settings = {"name": "Alice's room", "map": "dune", "team_size": 2, "difficulty": "Hard", "rounds": 5, "friendly_fire": False,
                "private": True, "team": "t", "closed_site": "a"}
    status, body = call("POST", "/rooms", settings, a)
    check("no game host running: refused", status == 503 and "offline" in body["detail"], body)
    check("wrong internal key refused", beat(key="0" * 64)[0] == 403)
    status, answer = beat()
    check("game host reports in", status == 200 and answer == {"has_room": False}, answer)
    status, body = call("POST", "/rooms", settings, a, headers={"X-Game-Version": "1.18"})
    check("old game version told to update", status == 426 and "Update" in body["detail"], body)
    status, body = call("POST", "/rooms", settings, a, headers={"X-Game-Version": "1.20"})
    check("a newer release is told the server is older", status == 426 and "still runs" in body["detail"], body)
    status, joined = call("POST", "/rooms", settings, a, headers={"X-Game-Version": "1.19.1"})   # a fix plays with its release
    check("Alice creates a room", status == 200 and joined["udp_host"] == "test.example" and joined["udp_port"] == 40000, joined)
    room = joined["room"]
    body_part, signature = joined["join_ticket"].split(".")
    expected = base64.urlsafe_b64encode(hmac.new(join_secret.encode(), body_part.encode(), hashlib.sha256).digest()).decode().rstrip("=")
    fields = base64.urlsafe_b64decode(body_part + "=" * (-len(body_part) % 4)).decode().split("|")
    check("ticket is signed with GAME_JOIN_SECRET", hmac.compare_digest(signature, expected))
    check("ticket names the room, Alice and her team", fields[1] == room["id"] and fields[3] == a_id and fields[4] == "Alice"
          and fields[5] == "t" and int(fields[6]) > time.time(), fields)
    check("ticket version 2, Alice not marked as an admin (no cheats)", fields[0] == "2" and len(fields) == 9 and fields[8] == "0", fields)
    status, answer = beat()
    check("host gets the room's settings", answer["has_room"] and answer["room"]["team_size"] == 2 and answer["room"]["difficulty"] == "Hard"
          and answer["room"]["members"] == [{"user_id": a_id, "nick": "Alice", "team": "t"}], answer)
    check("host gets the closed site", answer["room"]["closed_site"] == "A", answer)
    check("only site A or B can be closed", call("PATCH", f"/rooms/{room['id']}", {"closed_site": "C"}, a)[0] == 422)
    check("the owner closes site B instead", call("PATCH", f"/rooms/{room['id']}", {"closed_site": "B"}, a)[0] == 200)
    check("the room shows site B closed", call("GET", f"/rooms/{room['id']}", token=a)[1]["closed_site"] == "B")
    check("the owner opens the whole map", call("PATCH", f"/rooms/{room['id']}", {"closed_site": "off"}, a)[1]["closed_site"] == "OFF")
    check("and closes site B again", call("PATCH", f"/rooms/{room['id']}", {"closed_site": "B"}, a)[1]["closed_site"] == "B")
    shown = call("GET", f"/rooms/{room['id']}", token=a)[1]
    check("an older game's team_size sets both teams", shown["terrorists"] == 2 and shown["swat"] == 2 and shown["max_players"] == 4, shown)
    status, changed = call("PATCH", f"/rooms/{room['id']}", {"terrorists": 1, "swat": 7}, a)
    check("teams of different sizes", status == 200 and changed["terrorists"] == 1 and changed["swat"] == 7
          and changed["team_size"] == 7 and changed["max_players"] == 8, changed)
    check("a team of 11 is refused", call("PATCH", f"/rooms/{room['id']}", {"swat": 11}, a)[0] == 422)
    check("a team smaller than its players is refused", call("PATCH", f"/rooms/{room['id']}", {"terrorists": 0}, a)[0] == 422)
    status, answer = beat()
    check("host gets each team's size", answer["room"]["terrorists"] == 1 and answer["room"]["swat"] == 7, answer)
    check("back to 2 against 2", call("PATCH", f"/rooms/{room['id']}", {"terrorists": 2, "swat": 2}, a)[0] == 200)
    status, rooms = call("GET", "/rooms", token=b)
    check("private room hidden from others", status == 200 and rooms["rooms"] == [], rooms)
    status, body = call("POST", "/rooms", dict(settings, name="Bob room"), b)
    check("only slot in use: All rooms are busy", status == 503 and "All rooms are busy" in body["detail"], body)
    check("private room needs the code", call("POST", f"/rooms/{room['id']}/join", {"team": "t"}, b)[0] == 403)
    check("wrong code: 404", call("POST", "/rooms/code/AAAAAA/join", {"team": "t"}, b)[0] == 404)
    status, bob_join = call("POST", f"/rooms/code/{room['code']}/join", {"team": "t"}, b)
    check("bob_2 joins by code", status == 200 and bob_join["room"]["players"] == 2, bob_join)
    check("Alice is owner, bob_2 is not", bob_join["room"]["is_owner"] is False and bob_join["room"]["owner"] == "Alice", bob_join["room"])
    check("only the owner starts", call("POST", f"/rooms/{room['id']}/start", token=b)[0] == 403)
    check("start waits for someone on the host", call("POST", f"/rooms/{room['id']}/start", token=a)[0] == 409)
    beat(room["id"], players=[(a_id, "t"), (b_id, "swat")])
    status, details = call("GET", f"/rooms/{room['id']}", token=a)
    check("host's teams and connections shown", [(m["nick"], m["team"], m["connected"]) for m in details["members"]]
          == [("Alice", "t", True), ("bob_2", "swat", True)], details)
    check("Alice starts the match", call("POST", f"/rooms/{room['id']}/start", token=a)[0] == 200)
    status, answer = beat(room["id"], players=[(a_id, "t"), (b_id, "swat")])
    check("host is told to start", answer["room"]["start"] is True, answer)
    beat(room["id"], state="playing", players=[(a_id, "t"), (b_id, "swat")])
    status, answer = beat(room["id"], state="playing", players=[(a_id, "t"), (b_id, "swat")])
    check("start is not repeated", answer["room"]["start"] is False, answer)
    status, presence = call("GET", "/presence", token=b)
    check("Alice shows In match", presence["friends"][0]["status"] == "in_match", presence)
    beat(room["id"], state="over", players=[(a_id, "t", 7), (b_id, "swat", 2)], match=1)
    beat(room["id"], state="over", players=[(a_id, "t", 7), (b_id, "swat", 2)], match=1)   # reported twice: paid once
    beat(room["id"], state="lobby", players=[(a_id, "t"), (b_id, "swat")])
    status, details = call("GET", f"/rooms/{room['id']}", token=a)
    check("after the match: waiting again", details["status"] == "waiting", details)

    print("invites")
    status, carol = call("POST", "/auth/register", {"nick": "carol", "password": "carol-pass"})
    c = carol["token"]
    check("cannot invite a stranger", call("POST", f"/rooms/{room['id']}/invite", {"user_id": carol["user"]["id"]}, a)[0] == 403)
    call("POST", "/friends/requests", {"nick": "carol"}, a)
    call("POST", "/friends/requests", {"nick": "Alice"}, c)   # asking back makes them friends
    check("invite a friend", call("POST", f"/rooms/{room['id']}/invite", {"user_id": carol["user"]["id"]}, a)[0] == 200)
    status, invites = call("GET", "/invites", token=c)
    check("carol sees the invitation", invites["invites"][0]["from"] == "Alice" and invites["invites"][0]["room_name"] == "Alice's room", invites)
    status, rooms = call("GET", "/rooms", token=c)
    check("invited carol sees the private room", len(rooms["rooms"]) == 1, rooms)
    status, carol_join = call("POST", f"/invites/{invites['invites'][0]['id']}/accept", {"team": "t"}, c)
    check("carol joins the Terrorists (2 of 2)", status == 200 and [m["team"] for m in carol_join["room"]["members"] if m["nick"] == "carol"]
          == ["t"], carol_join)
    status, dave = call("POST", "/auth/register", {"nick": "dave", "password": "dave-pass"})
    status, dave_join = call("POST", f"/rooms/code/{room['code']}/join", {"team": "t"}, dave["token"])
    check("Terrorists full: dave goes to SWAT", status == 200 and [m["team"] for m in dave_join["room"]["members"] if m["nick"] == "dave"]
          == ["swat"], dave_join)
    status, eve = call("POST", "/auth/register", {"nick": "eve", "password": "eve-pass1"})
    status, eve_join = call("POST", f"/rooms/code/{room['code']}/join", {"team": "swat"}, eve["token"])
    check("both teams full: eve watches", status == 200 and [m["team"] for m in eve_join["room"]["members"] if m["nick"] == "eve"]
          == ["spectator"], eve_join)
    call("POST", f"/rooms/{room['id']}/leave", token=dave["token"])
    call("POST", f"/rooms/{room['id']}/leave", token=eve["token"])

    print("leaving and closing")
    check("Alice leaves", call("POST", f"/rooms/{room['id']}/leave", token=a)[0] == 200)
    status, details = call("GET", f"/rooms/{room['id']}", token=b)
    check("bob_2 is the owner now", details["owner"] == "bob_2" and details["is_owner"], details)
    status, answer = beat(room["id"], players=[(b_id, "swat")])
    check("host no longer lists Alice", [m["nick"] for m in answer["room"]["members"]] == ["bob_2", "carol"], answer)
    check("bob_2 closes the room", call("DELETE", f"/rooms/{room['id']}", token=b)[0] == 200)
    status, answer = beat(room["id"])
    check("host is told the room is gone", answer == {"has_room": False}, answer)
    status, health = call("GET", "/health")
    check("slot free again", health["rooms_free"] == 1 and health["servers"] == 1, health)

    print("time online in the game")
    call("GET", "/presence", token=c)   # carol's game is open (in the menus)
    status, before = call("GET", "/players/played?nicks=carol,nobody", token=c)
    check("unknown names left out", status == 200 and [p["nick"] for p in before["players"]] == ["carol"], before)
    had = before["players"][0]["played"]
    time.sleep(2.2)
    status, mine = call("GET", "/presence", token=c)
    check("time in the menus counts too, and the game hears its own total", 2 <= mine["played"] - had <= 3, mine)
    status, after = call("GET", "/players/played?nicks=carol", token=c)
    check("others see it", after["players"][0]["played"] == mine["played"], after)
    status, friends = call("GET", "/friends", token=a)
    check("friends show their time online", any(f["nick"] == "carol" and f["played"] == mine["played"] for f in friends["friends"]), friends)
    check("so does the account itself (/me)", call("GET", "/me", token=c)[1]["played"] == mine["played"])
    time.sleep(0.5)
    call("GET", "/presence?playing=1", token=c)
    check("in a match it goes on counting", call("GET", "/me", token=c)[1]["played"] >= mine["played"])

    print("the bots' knowledge")
    flat = lambda *cells: [v for cell in cells for v in cell]
    status, known = call("GET", "/bot-knowledge/dune")
    check("knowledge starts empty", status == 200 and known["stand"] == [] and known["kill"] == [] and known["matches"] == 0, known)
    check("unknown map: 404", call("GET", "/bot-knowledge/nowhere")[0] == 404)
    check("a player's upload needs a login", call("POST", "/bot-knowledge", {"map": "dune", "cells": flat([0, 1, 3, 0, 4, 2, 2, 0])})[0] == 401)
    check("bad cells refused", call("POST", "/bot-knowledge", {"map": "dune", "cells": [0, 1, 3]}, c)[0] == 422)
    check("too heavy a cell refused", call("POST", "/bot-knowledge", {"map": "dune", "cells": flat([0, 1, 3, 0, 4, 99, 0, 0])}, c)[0] == 422)
    status, sent = call("POST", "/bot-knowledge", {"map": "dune", "mode": "solo",
                                                   "cells": flat([0, 1, 3, 0, 4, 2, 2, 0], [1, 0, -5, 0, 7, 10, 0, 0])}, c)
    check("a player's game sends what people did", status == 200 and sent["cells"] == 2, sent)
    check("one upload a minute per player", call("POST", "/bot-knowledge", {"map": "dune", "cells": flat([0, 0, 1, 0, 1, 1, 0, 0])}, c)[0] == 429)
    internal_key = hmac.new(JOIN_SECRET.encode(), b"lowstrike-internal", hashlib.sha256).hexdigest()
    status, sent = call("POST", "/internal/bot-knowledge", {"map": "dune", "mode": "online",
                                                            "cells": flat([0, 1, 3, 0, 4, 4, 0, 4], [0, 1, 9, 0, 9, 1, 1, 0])},
                        base=CONTROL, headers={"X-Internal-Key": internal_key})
    check("a game host sends what people did", status == 200 and sent["cells"] == 2, sent)
    check("game hosts need the key", call("POST", "/internal/bot-knowledge", {"map": "dune", "cells": []}, base=CONTROL,
                                          headers={"X-Internal-Key": "0" * 64})[0] == 403)
    status, known = call("GET", "/bot-knowledge/dune")
    kills = sorted([known["kill"][i:i + 7] for i in range(0, len(known["kill"]), 7)], key=lambda k: -k[4])
    stands = [known["stand"][i:i + 5] for i in range(0, len(known["stand"]), 5)]
    # cell 3,0,4: the player's 2 kills count half (1, shot east) + the host's 4 (shot north) = 5, mostly north
    check("both games add up (a player's counts half)", known["matches"] == 2 and len(kills) == 2 and kills[0][:5] == [0, 3, 0, 4, 1.0]
          and kills[1][4] == 0.2 and abs(kills[0][5] - 0.243) < 0.01 and abs(kills[0][6] - 0.97) < 0.01, known)
    check("where people stood", stands == [[1, -5, 0, 7, 1.0]], known)
    status, same = call("GET", "/internal/bot-knowledge/dune", base=CONTROL, headers={"X-Internal-Key": internal_key})
    check("game hosts read the same knowledge", status == 200 and same["kill"] == known["kill"], same)

    print("player styles (bots that copy players)")
    key = {"X-Internal-Key": internal_key}
    way = {"site": "B", "points": [0, 0, 0, 0, 5, 0, 1, 2.5, 10, 0, 3, 9], "lookX": 1, "lookZ": 0, "hold": 4, "crouch": True}
    style = {"v": 1, "t": {"rounds": 3, "siteA": 0, "siteB": 2, "paths": [way], "buys": [9, 3], "armor": 3, "helmet": 1, "kit": 0,
                           "kills": [10, 0, 3, 0, 1], "killCount": 1, "headshots": 1},
             "swat": {"rounds": 0, "paths": []}}
    status, answer = call("GET", f"/internal/tactics/dune?users={a_id}", base=CONTROL, headers=key)
    check("no styles yet", status == 200 and answer["players"] == [] and answer["others"] == [], answer)
    check("styles: game hosts need the key", call("GET", "/internal/tactics/dune", base=CONTROL, headers={"X-Internal-Key": "0" * 64})[0] == 403)
    check("styles: unknown map 404", call("GET", "/tactics/nowhere")[0] == 404)
    bad = dict(style, t=dict(style["t"], paths=[dict(way, points=[1, 2, 3])]))
    status, sent = call("POST", "/internal/tactics", {"map": "dune", "players": [
        {"user_id": a_id, "data": style}, {"user_id": b_id, "data": bad}, {"user_id": "nobody", "data": style},
        {"user_id": a_id, "data": {"t": {"paths": [{"points": [0, 0, 0, float("nan")] * 2}]}}}]}, base=CONTROL, headers=key)
    check("a game host keeps its players' styles (bad ones and unknown accounts left out)", status == 200 and sent["kept"] == 1, sent)
    far = dict(way, points=[9999, 0, 0, 0, 5, 0, 1, 999], hold=500, site="C", lookX=7)
    call("POST", "/internal/tactics", {"map": "dune", "players": [{"user_id": b_id, "data": {"t": {"rounds": 1, "paths": [far] * 12}}}]},
         base=CONTROL, headers=key)
    status, answer = call("GET", f"/internal/tactics/dune?users={a_id}", base=CONTROL, headers=key)
    mine = answer["players"][0]["data"] if answer.get("players") else {}
    check("a player's style comes back as sent", status == 200 and len(answer["players"]) == 1 and answer["players"][0]["user_id"] == a_id
          and mine["t"]["paths"][0] == way and mine["t"]["buys"] == [9, 3] and mine["t"]["siteB"] == 2, answer)
    check("others for the bots leave out the room's players", len(answer["others"]) == 1, answer)
    b_way = answer["others"][0]["t"]["paths"] if answer["others"] else []
    check("numbers are kept in range, at most 8 ways", len(b_way) == 8 and b_way[0]["points"][0] == 2000 and b_way[0]["points"][7] == 300
          and b_way[0]["hold"] == 120 and b_way[0]["site"] == "" and b_way[0]["lookX"] == 1, b_way[:1])
    status, public = call("GET", "/tactics/dune")
    check("anyone gets styles without names", status == 200 and len(public["others"]) == 2 and "user_id" not in json.dumps(public), public)

    print("reviews")
    site = {"Origin": "https://lowstrike.lokvetia.com"}
    review = {"nick": "carol", "password": "carol-pass", "rating": 5, "text": "Great game,   really fun!\n\n\n\nThe bots are tough.", "lang": "en"}
    status, listed = call("GET", "/reviews")
    check("no reviews yet", status == 200 and listed["count"] == 0 and listed["reviews"] == [] and listed["average"] is None, listed)
    check("wrong password: no review", call("POST", "/reviews", dict(review, password="nope-nope"), headers=site)[0] == 401)
    check("an unknown nickname: no review", call("POST", "/reviews", dict(review, nick="nobody"), headers=site)[0] == 401)
    check("other websites cannot write", call("POST", "/reviews", review, headers={"Origin": "https://evil.example"})[0] == 403)
    check("links refused", call("POST", "/reviews", dict(review, text="Cheap skins at www.spam-site.com"), headers=site)[0] == 422)
    check("1 to 5 stars", call("POST", "/reviews", dict(review, rating=6), headers=site)[0] == 422)
    check("a few words at least", call("POST", "/reviews", dict(review, text="ok"), headers=site)[0] == 422)
    status, posted = call("POST", "/reviews", review, headers=site)
    check("carol writes a review", status == 200 and posted["replaced"] is False and posted["nick"] == "carol", posted)
    status, posted = call("POST", "/reviews", dict(review, nick="CAROL", rating=4, text="Changed my mind: <b>4 stars</b>"), headers=site)
    check("a second review replaces the first", status == 200 and posted["replaced"] is True, posted)
    status, posted = call("POST", "/reviews", {"nick": "bob_2", "password": "Hunter2hunter", "rating": 3, "text": "Nice maps, more please."},
                          headers=site)
    check("bob_2 writes one too", status == 200, posted)
    status, listed = call("GET", "/reviews")
    check("reviews: newest first, count and average", listed["count"] == 2 and listed["average"] == 3.5
          and [r["nick"] for r in listed["reviews"]] == ["bob_2", "carol"], listed)
    check("the text stays plain text", listed["reviews"][1]["text"] == "Changed my mind: <b>4 stars</b>", listed)
    status, posted = call("POST", "/reviews", dict(review, rating=5), headers=site)
    status, listed = call("GET", "/reviews")
    check("spaces and blank lines tidied", listed["reviews"][0]["text"] == "Great game, really fun!\n\nThe bots are tough.", listed)
    status, headers = headers_of("GET", "/reviews", {"Origin": "https://anyone.example"})
    check("anyone may read (CORS)", headers.get("access-control-allow-origin") == "*", headers)
    status, headers = headers_of("OPTIONS", "/reviews", {"Origin": "https://lowstrike.lokvetia.com", "Access-Control-Request-Method": "POST"})
    check("the website may write (CORS)", status in (200, 204) and headers.get("access-control-allow-origin") == "https://lowstrike.lokvetia.com"
          and "POST" in headers.get("access-control-allow-methods", ""), headers)
    status, headers = headers_of("OPTIONS", "/reviews", {"Origin": "https://evil.example", "Access-Control-Request-Method": "POST"})
    check("other websites may not (CORS)", "access-control-allow-origin" not in headers, headers)

    print("the browser version: the API from the website, online rooms over a WebSocket")
    status, headers = headers_of("OPTIONS", "/me", {"Origin": "https://lowstrike.lokvetia.com", "Access-Control-Request-Method": "GET",
                                                   "Access-Control-Request-Headers": "authorization,x-game-version"})
    check("the website's page may call the API with a login", status == 200 and headers.get("access-control-allow-origin") == "https://lowstrike.lokvetia.com"
          and "authorization" in headers.get("access-control-allow-headers", "").lower(), headers)
    status, headers = headers_of("OPTIONS", "/me", {"Origin": "https://evil.example", "Access-Control-Request-Method": "GET"})
    check("other websites' pages may not", "access-control-allow-origin" not in headers, headers)
    host = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)   # stands in for room-1's game host (UDP 27015)
    host.bind(("127.0.0.1", 27015))
    host.settimeout(5)
    try:
        with ws_connect(f"ws://127.0.0.1:{PORT}/rooms/ws/room-1", origin="https://lowstrike.lokvetia.com", open_timeout=5) as link:
            link.send(b"hello")
            data, sender = host.recvfrom(2048)
            check("a browser's message reaches the room's game host as one UDP packet", data == b"hello" and sender[0] == "127.0.0.1", (data, sender))
            host.sendto(b"ewelcome", sender)
            check("and the host's answer comes back as one message", link.recv(timeout=5) == b"ewelcome")
            link.send(b"x" * 1401)
            link.send(b"after")
            check("a packet over 1400 bytes is dropped", host.recvfrom(2048)[0] == b"after")
    finally:
        host.close()
    for path, origin, name in (("/rooms/ws/room-9", "https://lowstrike.lokvetia.com", "no such room slot"),
                               ("/rooms/ws/room-1", "https://evil.example", "another website's page")):
        try:
            with ws_connect(f"ws://127.0.0.1:{PORT}{path}", origin=origin, open_timeout=5) as link:
                link.recv(timeout=5)
            refused = False
        except Exception:
            refused = True
        check(f"{name}: no link", refused)

    print("tokens")
    status, mine = call("GET", "/tokens", token=a)
    check("a room's match pays its players once, times its Hard bots (x1.5): Alice's 7 kills and the win",
          status == 200 and mine["tokens"] == 18 and mine["owned"] == [] and len(mine["shop"]) == 64, mine)
    check("bob_2 lost: his 2 kills x1.5", call("GET", "/tokens", token=b)[1]["tokens"] == 3)
    match = {"kills": 10, "won": True, "rounds": 8, "seconds": 600}
    check("a match too short to be real is refused", call("POST", "/tokens/match", dict(match, seconds=30), a)[0] == 422)
    status, paid = call("POST", "/tokens/match", dict(match, difficulty="Easy"), a)
    check("Easy bots pay nothing (x0)", status == 200 and paid["earned"] == 0 and paid["tokens"] == 18 and paid["today"] == 0, paid)
    status, paid = call("POST", "/tokens/match", match, a)
    check("own match, no difficulty said (an older game): as Normal, 10 kills and the win", status == 200
          and paid["earned"] == 15 and paid["tokens"] == 33 and paid["today"] == 15, paid)
    status, paid = call("POST", "/tokens/match", dict(match, kills=3, won=False, difficulty="Extreme"), a)
    check("Extreme bots pay double", paid["earned"] == 6 and paid["tokens"] == 39, paid)
    status, paid = call("POST", "/tokens/match", dict(match, kills=40, difficulty="Hard"), a)
    check("own matches stop at the day's limit", paid["earned"] == 19 and paid["tokens"] == 58 and paid["today"] == 40, paid)
    check("nothing more today", call("POST", "/tokens/match", match, a)[1]["earned"] == 0)
    check("a skin that is not for sale", call("POST", "/tokens/buy", {"weapon": "ak47", "skin": "Gold"}, a)[0] == 404)
    check("58 tokens are not enough for the Halloween AK-47 (150)",
          call("POST", "/tokens/buy", {"weapon": "ak47", "skin": "Halloween"}, a)[0] == 409)


    print("passwords, logout, deletion, limits")
    check("change password needs the old one", call("POST", "/me/password", {"old_password": "x", "new_password": "new-secret-pass"}, a)[0] == 401)
    check("change password", call("POST", "/me/password", {"old_password": "secret-pass", "new_password": "new-secret-pass"}, a)[0] == 200)
    check("the other login was ended", call("GET", "/me", token=again["token"])[0] == 401)
    check("old password no longer works", call("POST", "/auth/login", {"nick": "Alice", "password": "secret-pass"})[0] == 401)
    check("log out", call("POST", "/auth/logout", token=a)[0] == 200)
    check("token dead after log out", call("GET", "/me", token=a)[0] == 401)
    check("delete needs the password", call("DELETE", "/me", {"password": "wrong"}, b)[0] == 401)
    check("bob_2 deletes the account", call("DELETE", "/me", {"password": "Hunter2hunter"}, b)[0] == 200)
    check("bob_2 can no longer log in", call("POST", "/auth/login", {"nick": "bob_2", "password": "Hunter2hunter"})[0] == 401)
    status, found = call("GET", "/players?search=bob", token=c)
    check("bob_2 is gone from search", found["players"] == [], found)
    status, listed = call("GET", "/reviews")
    check("bob_2's review went with the account", [r["nick"] for r in listed["reviews"]] == ["carol"], listed)
    codes = [call("POST", "/auth/login", {"nick": "carol", "password": f"wrong-{i}"})[0] for i in range(9)]
    check("wrong passwords get limited", codes[-1] == 429, codes)


def run_admin():
    global c   # carol logs in again after resetting her password
    print("admin panel (the server started again, with Alice on ADMIN_NICKS)")
    a = call("POST", "/auth/login", {"nick": "Alice", "password": "new-secret-pass"})[1]["token"]
    c = call("POST", "/auth/login", {"nick": "carol", "password": "carol-pass"})[1]["token"]
    check("Alice is an admin now", call("GET", "/me", token=a)[1]["admin"] is True)
    check("carol is not", call("GET", "/me", token=c)[1]["admin"] is False)
    check("an admin's name without an account cannot be signed up",
          call("POST", "/auth/register", {"nick": "Ghost_Admin", "password": "secret-pass"})[0] == 409)
    check("not an admin: 403", call("GET", "/admin/player?nick=Alice", token=c)[0] == 403)
    check("not an admin cannot give tokens", call("POST", "/admin/tokens", {"nick": "carol", "amount": 500}, c)[0] == 403)
    status, found = call("GET", "/admin/player?nick=CAROL", token=a)
    check("find a player (any case)", status == 200 and found["nick"] == "carol" and found["owned"] == []
          and len(found["shop"]) == 64 and "created_at" in found, found)
    had = found["tokens"]
    check("no such player: 404", call("GET", "/admin/player?nick=nobody_here", token=a)[0] == 404)
    status, given = call("POST", "/admin/tokens", {"nick": "carol", "amount": 100}, a)
    check("give 100 tokens", status == 200 and given["tokens"] == had + 100, given)
    status, taken = call("POST", "/admin/tokens", {"nick": "carol", "amount": -100000}, a)
    check("take more than there is: 0", status == 200 and taken["tokens"] == 0, taken)
    check("0 tokens refused", call("POST", "/admin/tokens", {"nick": "carol", "amount": 0}, a)[0] == 422)
    check("too many at once refused", call("POST", "/admin/tokens", {"nick": "carol", "amount": 100001}, a)[0] == 422)
    status, gift = call("POST", "/admin/skins", {"nick": "carol", "weapon": "knife", "skin": "Classic Halloween", "owned": True}, a)
    check("give a skin", status == 200 and gift["owned"] == ["knife/Classic Halloween"] and gift["tokens"] == 0, gift)
    check("carol has it as her own", call("GET", "/tokens", token=c)[1]["owned"] == ["knife/Classic Halloween"])
    status, back = call("POST", "/admin/skins", {"nick": "carol", "weapon": "knife", "skin": "Classic Halloween", "owned": False}, a)
    check("take it back", status == 200 and back["owned"] == [], back)
    check("only shop skins", call("POST", "/admin/skins", {"nick": "carol", "weapon": "ak47", "skin": "Gold"}, a)[0] == 404)
    status, log = call("GET", "/admin/log", token=a)
    check("every change is logged, newest first", status == 200 and [e["action"] for e in log["entries"]] == [
        "took knife/Classic Halloween", "gave knife/Classic Halloween", f"-100000 tokens ({had + 100} -> 0)",
        f"+100 tokens ({had} -> {had + 100})"] and all(e["admin"] == "Alice" and e["target"] == "carol" for e in log["entries"]), log)
    check("not an admin cannot read the log", call("GET", "/admin/log", token=c)[0] == 403)

    print("LAN pass (an admin's cheats as a LAN guest)")
    status, lan = call("POST", "/admin/lan-pass", token=a)
    check("an admin gets a LAN pass", status == 200 and len(lan["pass"]) > 20 and lan["seconds"] == 3600, lan)
    status, checked = call("POST", "/admin/lan-pass/check", {"pass": lan["pass"]})
    check("a LAN host checks it (no login needed): Alice, an admin", status == 200 and checked == {"admin": True, "nick": "Alice"}, checked)
    check("a made-up pass is no admin", call("POST", "/admin/lan-pass/check", {"pass": "made-up"})[1]["admin"] is False)
    check("not an admin: no pass", call("POST", "/admin/lan-pass", token=c)[0] == 403)

    print("buying skins")
    call("POST", "/admin/tokens", {"nick": "carol", "amount": 1000}, a)
    status, bought = call("POST", "/tokens/buy", {"weapon": "ak47", "skin": "Halloween"}, c)
    check("carol buys the Halloween AK-47 for 150", status == 200 and bought["tokens"] == 850 and bought["owned"] == ["ak47/Halloween"], bought)
    check("buying it again costs nothing", call("POST", "/tokens/buy", {"weapon": "ak47", "skin": "Halloween"}, c)[1]["tokens"] == 850)
    status, bought = call("POST", "/tokens/buy", {"weapon": "deagle", "skin": "digle"}, c)
    check("and the digle Desert Eagle for 750", status == 200 and bought["tokens"] == 100, bought)
    check("100 are not enough for nogektestskin (1000)", call("POST", "/tokens/buy", {"weapon": "knife", "skin": "nogektestskin"}, c)[0] == 409)
    prices = {(i["weapon"], i["skin"]): i["price"] for i in bought["shop"]}
    neon = {(w, f"Neon {c}"): 100 if w == "knife" else 75 if c == "Pink" else 90
            for w in ("knife", "glock", "usp", "tec9", "m1911", "deagle", "uzi", "mp5", "shotgun", "ak47",
                      "m4a1", "awp", "rpg", "svd") for c in ("Cyan", "Pink", "Green", "Yellow")}
    check("the shop's prices", prices == {("ak47", "Halloween"): 150, ("m4a1", "Halloween"): 150, ("knife", "Classic Halloween"): 500,
                                          ("knife", "karambit"): 400, ("knife", "nogektestskin"): 1000, ("deagle", "digle"): 750,
                                          ("rpg", "Web"): 400, ("awp", "AWPOLD"): 750, **neon}, prices)

    print("cases")
    cases = {k["id"]: k for k in bought["cases"]}
    check("three cases: neon 85, old 800, Halloween 350", {k: (v["price"], len(v["items"])) for k, v in cases.items()}
          == {"neon": (85, 56), "old": (800, 4), "halloween": (350, 4)}, cases)
    check("no such case", call("POST", "/tokens/open", {"case": "gold"}, c)[0] == 404)
    check("100 tokens are not enough for the old case", call("POST", "/tokens/open", {"case": "old"}, c)[0] == 409)
    call("POST", "/admin/tokens", {"nick": "carol", "amount": 2000}, a)
    halloween = set(cases["halloween"]["items"])
    status, opened = call("POST", "/tokens/open", {"case": "halloween"}, c)
    won = opened.get("won")
    check("carol opens the Halloween case: one she has not got", status == 200 and opened["tokens"] == 1750 and won in halloween
          and won != "ak47/Halloween" and won in opened["owned"], opened)
    for _ in range(2):
        status, opened = call("POST", "/tokens/open", {"case": "halloween"}, c)
    check("two more: she has the whole case", status == 200 and halloween <= set(opened["owned"]) and opened["tokens"] == 1050, opened)
    status, again = call("POST", "/tokens/open", {"case": "halloween"}, c)
    check("then it does not open (and costs nothing)", status == 409 and call("GET", "/tokens", token=c)[1]["tokens"] == 1050, again)
    status, opened = call("POST", "/tokens/open", {"case": "neon"}, c)
    check("a neon case gives a neon skin for 85", status == 200 and opened["won"] in cases["neon"]["items"] and opened["tokens"] == 965, opened)
    status, opened = call("POST", "/tokens/open", {"case": "old"}, c)
    check("the old case gives an old skin carol has not got (not the digle she bought)", status == 200
          and opened["won"] in cases["old"]["items"] and opened["won"] != "deagle/digle" and opened["tokens"] == 165, opened)


    print("password recovery by email")
    check("an email needs the password", call("POST", "/me/email", {"email": "carol@example.com", "password": "x"}, c)[0] == 401)
    check("not an email address", call("POST", "/me/email", {"email": "carol-at-example", "password": "carol-pass"}, c)[0] == 422)
    status, asked = call("POST", "/me/email", {"email": "carol@example.com", "password": "carol-pass", "lang": "de"}, c)
    check("carol adds an email: it waits for its code", status == 200 and asked["pending"] == "carol@example.com" and asked["email"] == "", asked)
    to, subject, code = next_mail()
    check("the code goes to that address, in her language", to == "carol@example.com" and "bestätigen" in subject and code, (to, subject))
    check("/me shows it waiting", call("GET", "/me", token=c)[1]["email_pending"] == "carol@example.com")
    check("a wrong code", call("POST", "/me/email/confirm", {"code": wrong(code)}, c)[0] == 422)
    status, confirmed = call("POST", "/me/email/confirm", {"code": code}, c)
    check("the right code confirms it", status == 200 and confirmed["email"] == "carol@example.com", confirmed)
    me = call("GET", "/me", token=c)[1]
    check("/me shows the email", me["email"] == "carol@example.com" and me["email_pending"] == "", me)

    status, answer = call("POST", "/auth/forgot", {"nick": "nobody_here"})
    check("forgot: an unknown nickname gets the usual answer, and no mail", status == 200 and answer["ok"] and next_mail(wait=1.5) is None, answer)
    status, same = call("POST", "/auth/forgot", {"nick": "CAROL", "lang": "en"})
    check("forgot: carol gets the same answer", status == 200 and same == answer, same)
    to, subject, code = next_mail()
    check("and a reset code at her email", to == "carol@example.com" and "reset" in subject and code, (to, subject))
    check("reset: a wrong code", call("POST", "/auth/reset", {"nick": "carol", "code": wrong(code), "new_password": "brand-new-pass"})[0] == 422)
    check("reset: a too short password", call("POST", "/auth/reset", {"nick": "carol", "code": code, "new_password": "short"})[0] == 422)
    status, reset = call("POST", "/auth/reset", {"nick": "carol", "code": code, "new_password": "brand-new-pass"})
    check("reset with the code logs her in", status == 200 and reset["user"]["nick"] == "carol" and reset["token"], reset)
    check("her other logins ended", call("GET", "/me", token=c)[0] == 401)
    c = reset["token"]
    check("the code works once", call("POST", "/auth/reset", {"nick": "carol", "code": code, "new_password": "another-pass"})[0] == 422)
    check("the new password logs in", call("POST", "/auth/login", {"nick": "carol", "password": "brand-new-pass"})[0] == 200)
    check("the old one does not", call("POST", "/auth/login", {"nick": "carol", "password": "carol-pass"})[0] == 401)
    call("POST", "/auth/forgot", {"nick": "carol"})
    to, subject, code = next_mail()
    for _ in range(5):
        call("POST", "/auth/reset", {"nick": "carol", "code": wrong(code), "new_password": "another-pass"})
    check("five wrong tries use the code up", call("POST", "/auth/reset", {"nick": "carol", "code": code, "new_password": "another-pass"})[0] == 422)
    check("an account without an email gets no mail", call("POST", "/auth/forgot", {"nick": "Alice"})[0] == 200 and next_mail(wait=1.5) is None)
    status, removed = call("POST", "/me/email", {"email": "", "password": "brand-new-pass"}, c)
    check("carol removes her email", status == 200 and removed["email"] == "" and call("GET", "/me", token=c)[1]["email"] == "", removed)

    print("an admin's recovery code (no email needed)")
    check("not an admin: no recovery code", call("POST", "/admin/recovery-code", {"nick": "Alice"}, c)[0] == 403)
    check("no such player", call("POST", "/admin/recovery-code", {"nick": "nobody_here"}, a)[0] == 404)
    status, made = call("POST", "/admin/recovery-code", {"nick": "CAROL"}, a)
    check("Alice (an admin) makes one for carol", status == 200 and made["nick"] == "carol" and len(made["code"]) == 8 and made["hours"] == 24, made)
    CODES_SEEN.append(made["code"])
    check("and no mail goes out", next_mail(wait=1.5) is None)
    status, reset = call("POST", "/auth/reset", {"nick": "carol", "code": made["code"], "new_password": "admin-given-pass"})
    check("carol sets her own new password with it", status == 200 and reset["user"]["nick"] == "carol", reset)
    c = reset["token"]
    check("and logs in with it", call("POST", "/auth/login", {"nick": "carol", "password": "admin-given-pass"})[0] == 200)
    entries = call("GET", "/admin/log", token=a)[1]["entries"]
    check("the admin log says a code was made (not which)", entries[0]["action"] == "made a recovery code" and entries[0]["target"] == "carol"
          and made["code"] not in str(entries), entries[0])


MAIL_DIR = None
MAILS_READ = set()
CODES_SEEN = []


def next_mail(wait=8.0):
    """The next mail the server wrote to MAIL_DIR: (to, subject, the 8-digit code in it), or None."""
    until = time.time() + wait
    while time.time() < until:
        new = sorted(f for f in MAIL_DIR.glob("*.eml") if f.name not in MAILS_READ) if MAIL_DIR.exists() else []
        if new:
            MAILS_READ.add(new[0].name)
            mail = email.message_from_bytes(new[0].read_bytes(), policy=email.policy.default)
            found = re.search(r"\b\d{8}\b", mail.get_content())
            if found:
                CODES_SEEN.append(found.group(0))
            return mail["To"], mail["Subject"], found.group(0) if found else None
        time.sleep(0.1)
    return None


def wrong(code):
    return f"{(int(code) + 1) % 10 ** 8:08d}"


if __name__ == "__main__":
    main()
