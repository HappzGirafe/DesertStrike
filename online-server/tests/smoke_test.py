"""Starts the online server with a throw-away database and checks the whole API over HTTP: accounts, friends,
presence, rooms, tickets, invites, the game hosts' reports, limits and account deletion.

Run:  python tests/smoke_test.py      (from online-server/, with the requirements installed)
"""

import base64
import hashlib
import hmac
import json
import os
import secrets
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.request
from pathlib import Path

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


def check(name, condition, detail=""):
    global failures
    print(("  ok    " if condition else "  FAIL  ") + name + ("" if condition else f"   {detail}"))
    if not condition:
        failures += 1


def main():
    work = Path(tempfile.mkdtemp(prefix="lowstrike-test-"))
    join_secret = secrets.token_urlsafe(48)
    env_file = work / "test.env"
    env_file.write_text("\n".join([
        "API_HOST=127.0.0.1", f"API_PORT={PORT}", f"INTERNAL_PORT={INTERNAL}", f"DATABASE_PATH={work / 'test.sqlite3'}",
        f"AUTH_SECRET={secrets.token_urlsafe(48)}", f"GAME_JOIN_SECRET={join_secret}", "GAME_BUILD_VERSION=1.19",
        'ROOM_SLOTS_JSON=[{"id":"room-1","local_udp_port":27015,"public_udp_host":"test.example","public_udp_port":40000}]',
    ]), encoding="utf-8")
    env = dict(os.environ, LOWSTRIKE_ENV=str(env_file))
    for key in ("API_PORT", "INTERNAL_PORT", "DATABASE_PATH", "AUTH_SECRET", "GAME_JOIN_SECRET", "ROOM_SLOTS_JSON", "UNITY_SERVER_EXECUTABLE"):
        env.pop(key, None)
    server = subprocess.Popen([sys.executable, str(HERE / "lowstrike_server.py")], env=env, cwd=HERE,
                              stdout=open(work / "server.log", "w"), stderr=subprocess.STDOUT)
    try:
        for _ in range(100):
            try:
                if call("GET", "/health")[0] == 200:
                    break
            except OSError:
                time.sleep(0.2)
        run(join_secret)
    finally:
        server.terminate()
        server.wait(10)
    log = (work / "server.log").read_text()
    check("the server log holds no password", "secret-pass" not in log and "Hunter2hunter" not in log)
    print(f"\n{'ALL PASSED' if failures == 0 else f'{failures} FAILED'}   (server log: {work / 'server.log'})")
    sys.exit(1 if failures else 0)


def beat(room_id="", state="lobby", players=(), key=None, match=0):
    internal_key = key or hmac.new(JOIN_SECRET.encode(), b"lowstrike-internal", hashlib.sha256).hexdigest()
    return call("POST", "/internal/slots/room-1/heartbeat", {
        "room_id": room_id, "state": state, "protocol": 5, "version": "1.19", "match_number": match, "terrorist_wins": 8,
        "swat_wins": 3, "map": "dune", "players": [{"user_id": u, "team": t, "connected": True} for u, t in players]},
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
    check("Alice sees bob_2 online", presence["friends"] == [{"id": b_id, "nick": "bob_2", "status": "online"}], presence)
    call("PATCH", "/me", {"hide_status": True}, b)
    status, presence = call("GET", "/presence", token=a)
    check("hidden bob_2 shows offline", presence["friends"][0]["status"] == "offline", presence)
    status, presence = call("GET", "/presence", token=b)
    check("hidden bob_2 sees no statuses", presence["friends"][0]["status"] == "hidden", presence)
    call("PATCH", "/me", {"hide_status": False}, b)

    print("rooms")
    settings = {"name": "Alice's room", "map": "dune", "team_size": 2, "difficulty": "Hard", "rounds": 5, "friendly_fire": False,
                "private": True, "team": "t"}
    status, body = call("POST", "/rooms", settings, a)
    check("no game host running: refused", status == 503 and "offline" in body["detail"], body)
    check("wrong internal key refused", beat(key="0" * 64)[0] == 403)
    status, answer = beat()
    check("game host reports in", status == 200 and answer == {"has_room": False}, answer)
    status, body = call("POST", "/rooms", settings, a, headers={"X-Game-Version": "1.18"})
    check("old game version told to update", status == 426 and "Update" in body["detail"], body)
    status, joined = call("POST", "/rooms", settings, a)
    check("Alice creates a room", status == 200 and joined["udp_host"] == "test.example" and joined["udp_port"] == 40000, joined)
    room = joined["room"]
    body_part, signature = joined["join_ticket"].split(".")
    expected = base64.urlsafe_b64encode(hmac.new(join_secret.encode(), body_part.encode(), hashlib.sha256).digest()).decode().rstrip("=")
    fields = base64.urlsafe_b64decode(body_part + "=" * (-len(body_part) % 4)).decode().split("|")
    check("ticket is signed with GAME_JOIN_SECRET", hmac.compare_digest(signature, expected))
    check("ticket names the room, Alice and her team", fields[1] == room["id"] and fields[3] == a_id and fields[4] == "Alice"
          and fields[5] == "t" and int(fields[6]) > time.time(), fields)
    status, answer = beat()
    check("host gets the room's settings", answer["has_room"] and answer["room"]["team_size"] == 2 and answer["room"]["difficulty"] == "Hard"
          and answer["room"]["members"] == [{"user_id": a_id, "nick": "Alice", "team": "t"}], answer)
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
    beat(room["id"], state="over", players=[(a_id, "t"), (b_id, "swat")], match=1)
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
    codes = [call("POST", "/auth/login", {"nick": "carol", "password": f"wrong-{i}"})[0] for i in range(9)]
    check("wrong passwords get limited", codes[-1] == 429, codes)


if __name__ == "__main__":
    main()
