"""Lists the game's texts that have no translation yet (Assets/Resources/Localization/<ua|de|fr|it|es>.json).

The game is written in English and GameUI translates what it draws (Assets/Scripts/Loc.cs). This reads the C# files
that make player-visible text, takes every string in them (an interpolated string's {holes} become {0}, {1}...), and
prints those the translation files do not know, with where they are. Texts made of parts are checked part by part
(lines, and the parts between "  ·  "), and a {hole} may be filled the way the game fills it, as the game does.

    python Tools/check_localization.py

Some of what it lists is not shown to players (names, log text): leave those, or add them to IGNORE below.
"""
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
FILES = ["GameUI.cs", "GameUI.Online.cs", "TouchControls.cs", "GameManager.cs", "BombManager.cs", "OnlineClient.cs",
         "NetSession.cs", "Accounts.cs", "Friends.cs", "VoiceChat.cs", "GameSettings.cs"]
LANGUAGES = ["ua", "de", "fr", "it", "es"]
SKIP_LINE = re.compile(r"Debug\.Log|PlayerPrefs\.|case \"|args\[|== \"-ds|StartsWith\(\"-|GetString\(|Resources\.Load|"
                       r"Shader\.Find|\bconst string \w+Key\b|nameof|SetString|GetInt|DeleteKey|UnityWebRequest|"
                       r"SetRequestHeader|application/json|Path\.Combine|\.png\"")
# Not shown to players, or the same in every language.
IGNORE = {
    "LOW STRIKE", "SWAT", "SHIFT", "ABC", "TAB", "II", "AUTO", "STICK", "HP", "Low Strike {0}", "{0}  SWAT", "@account-name",
    "Player", "Bot ", "Remote ", "Net ", "Main Camera", "MainCamera", "Sun", "Pose ", "crouch walk", "DSTRIKE", "LSFRIENDS",
    "Led", "GET", "POST", "PATCH", "DELETE", "code:", " room", "{0}'s room", "yyyy-MM-dd", "Voice ", "Silence", "Friends",
    "{0} FPS", "{0} ms", "CPU {0} ms    GPU {1}", "30 FPS", "60 FPS", "120 FPS", "Site", "WELCOME,",
    "No skins found in Assets/Resources/Skins.", "was sent away:", "did not come back", "timed out", "(culling off)",
    "Viper", "Jackal", "Cobra", "Scorpion", "Raven", "Wolf", "Dagger", "Ghost", "Hawk", "Falcon", "Ranger", "Bishop", "Echo",
    "Titan", "Sentinel", "Patriot", "Mamba", "Hyena", "Fang", "Shadow", "Eagle", "Knight", "Shield", "Arrow",
    "loaded in {0} ms", "OFF", "Halloween", "[Net] {0} brought a LAN pass that is no admin's (or the online server could not be asked)",
}
# Joined with the next line in the code, or only a piece of what is shown (the translation files hold the whole text,
# or a template for it), or text for tests and logs.
JOINED = re.compile(r"^(Add your own|with main\.png|Play with people anywhere|phone, keeps your|Rooms run on the Low Strike|"
                    r"Voice chat: hold \{0\}, in the room|Use \{0\}, and this computer|with it and come along|"
                    r"On (a Mac|Windows|Android|iPhone)|Could not (start hosting|open the network|search the network):$|"
                    r": speak, the bar|\{0\} sensitivity|\{0\} \{1\} to (buy|sell|take)|"
                    r"\{0\} (invitation|request)\{1\}$|bodies in view|CPU \{0\} ms, GPU|tris \{0\}|\$\{0\}, main|level now|\^\[A-Za-z)")


def literals(line):
    """(interpolated?, body) of each C# string on the line."""
    out, i = [], 0
    while i < len(line):
        if line.startswith("//", i):
            break
        interp = line.startswith('$"', i)
        if interp or line[i] == '"':
            j, body, depth = i + (2 if interp else 1), [], 0
            while j < len(line):
                c = line[j]
                if c == "\\" and j + 1 < len(line):
                    body.append(line[j:j + 2])
                    j += 2
                    continue
                if interp and c == "{":
                    depth += 1
                elif interp and c == "}" and depth > 0:
                    depth -= 1
                elif c == '"' and depth == 0:
                    break
                elif c == '"':
                    k = j + 1
                    while k < len(line) and line[k] != '"':
                        k += 2 if line[k] == "\\" else 1
                    body.append(line[j:k + 1])
                    j = k + 1
                    continue
                body.append(c)
                j += 1
            out.append((interp, "".join(body)))
            i = j + 1
            continue
        if line[i] == "'":
            i += 3 if i + 2 < len(line) and line[i + 2] == "'" else 1
            continue
        i += 1
    return out


def template(body):
    """An interpolated string's {expressions} as {0}, {1}..."""
    result, n, i = [], 0, 0
    while i < len(body):
        if body[i] == "{":
            depth, k = 1, i + 1
            while k < len(body) and depth:
                depth += {"{": 1, "}": -1}.get(body[k], 0)
                k += 1
            result.append("{%d}" % n)
            n += 1
            i = k
            continue
        result.append(body[i])
        i += 1
    return "".join(result)


def parts(text):
    for line in text.replace("\\n", "\n").split("\n"):
        for part in line.split("  ·  "):
            part = part.strip()
            if re.search(r"[A-Za-z]{2}", re.sub(r"\{\d+\}", "", part)) and part not in IGNORE:
                yield part


def compile_templates(keys):
    patterns = []
    for key in keys:
        if re.search(r"\{\d\}", key):
            pieces = re.split(r"\{\d\}", key)
            patterns.append(re.compile("^" + "(.*)".join(re.escape(p) for p in pieces) + "$", re.S))
    return patterns


def known(part, exact, patterns):
    """Whether the game finds a translation: as written, or with its {holes} filled (by something, or nothing)."""
    counter = iter(range(99))
    if re.sub(r"\{\d+\}", lambda m: "{%d}" % next(counter), part) in exact:
        return True
    for fill in ("X", ""):
        sample = re.sub(r"\{\d+\}", fill, part).strip()
        if sample in exact or any(p.match(sample) for p in patterns):
            return True
    return False


def main():
    keys, patterns = {}, {}
    for code in LANGUAGES:
        with open(os.path.join(ROOT, "Assets", "Resources", "Localization", code + ".json"), encoding="utf-8") as f:
            keys[code] = set(json.load(f))
        patterns[code] = compile_templates(keys[code])
    missing = {}
    for name in FILES:
        path = os.path.join(ROOT, "Assets", "Scripts", name)
        for number, line in enumerate(open(path, encoding="utf-8"), 1):
            if SKIP_LINE.search(line) or line.strip().startswith("//"):
                continue
            for interp, body in literals(line):
                text = template(body) if interp else body
                if re.fullmatch(r"[a-z_.\-/0-9:]+", text) or text.startswith(("http", "/")) or text in IGNORE:
                    continue
                for part in parts(text):
                    if JOINED.match(part) or re.fullmatch(r"(\{\d+\}\s*)+", part):
                        continue
                    for code in LANGUAGES:
                        if not known(part, keys[code], patterns[code]):
                            missing.setdefault(part, {"where": f"{name}:{number}", "languages": set()})["languages"].add(code)
    if not missing:
        print("Every text has a translation in", ", ".join(LANGUAGES))
        return 0
    for text, info in missing.items():
        print(f"{info['where']:<26} {','.join(sorted(info['languages'])):<16} {text}")
    print(f"\n{len(missing)} texts without a translation (some may never be shown: see IGNORE)")
    return 1


if __name__ == "__main__":
    sys.exit(main())
