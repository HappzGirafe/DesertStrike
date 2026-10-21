#!/bin/sh
# Low Strike online server: first-time setup on a Linux server (the Linux twin of scripts/setup.ps1).
# Makes the Python environment (.venv), installs the requirements, and writes .env from .env.example with two new
# random secrets, the game version, and the path of the Linux game host next to it (the package's game folder).
# Run it again any time: an existing .env is kept.
#
#   sh online-server/scripts/linux/setup.sh
set -e
root=$(cd "$(dirname "$0")/../.." && pwd)
cd "$root"

python=""
for candidate in python3.13 python3.12 python3.11 python3.10 python3; do
    if command -v "$candidate" >/dev/null 2>&1 &&
        "$candidate" -c 'import sys; sys.exit(0 if sys.version_info >= (3, 10) else 1)' 2>/dev/null; then
        python=$candidate
        break
    fi
done
if [ -z "$python" ]; then
    echo "Python 3.10 or newer is not installed. Install it, then run this again, e.g.:"
    echo "  sudo apt install python3 python3-venv        (Debian, Ubuntu, Kali)"
    echo "  sudo dnf install python3                     (Fedora)"
    exit 1
fi

if [ ! -x .venv/bin/python ]; then
    echo "Making the Python environment (.venv)..."
    if ! "$python" -m venv .venv; then
        rm -rf .venv
        echo "Python cannot make an environment here: install its venv module, then run this again, e.g.:"
        echo "  sudo apt install python3-venv"
        exit 1
    fi
fi
echo "Installing the requirements..."
.venv/bin/python -m pip install --upgrade --quiet --disable-pip-version-check -r requirements.txt

package=$(dirname "$root")
game="$package/game/LowStrikeServer.x86_64"
[ -f "$game" ] && chmod +x "$game"

if [ -f .env ]; then
    echo ".env exists already: kept as it is."
else
    .venv/bin/python - "$game" "$package/VERSION.txt" <<'EOF'
import os, secrets, sys
game, version_file = sys.argv[1], sys.argv[2]
text = open(".env.example", encoding="utf-8").read()
text = text.replace("AUTH_SECRET=REPLACE_WITH_STRONG_RANDOM_SECRET", "AUTH_SECRET=" + secrets.token_urlsafe(48))
text = text.replace("GAME_JOIN_SECRET=REPLACE_WITH_DIFFERENT_STRONG_RANDOM_SECRET", "GAME_JOIN_SECRET=" + secrets.token_urlsafe(48))
if os.path.isfile(game):
    text = text.replace("UNITY_SERVER_EXECUTABLE=PATH_TO_UNITY_BUILD", "UNITY_SERVER_EXECUTABLE=" + game)
if os.path.isfile(version_file):
    text = text.replace("GAME_BUILD_VERSION=REPLACE_ME", "GAME_BUILD_VERSION=" + open(version_file).readline().strip())
with open(".env", "w", encoding="utf-8") as f:
    f.write(text)
os.chmod(".env", 0o600)
print("Wrote .env with two new random secrets (keep this file private).")
EOF
fi

mkdir -p data logs
missing=""
grep -q "YOUR_PLAYIT_HOST" .env && missing="$missing\n  ROOM_SLOTS_JSON           the address playit.gg gives your UDP tunnel (README step 2)"
grep -q "PUBLIC_API_URL=https://api.example.com" .env && missing="$missing\n  PUBLIC_API_URL            the tunnel's https address for the API (README step 3)"
grep -q "UNITY_SERVER_EXECUTABLE=PATH_TO" .env && missing="$missing\n  UNITY_SERVER_EXECUTABLE   the Linux game host (game/LowStrikeServer.x86_64)"
grep -q "GAME_BUILD_VERSION=REPLACE" .env && missing="$missing\n  GAME_BUILD_VERSION        the game's version, e.g. 1.20"
echo ""
if [ -n "$missing" ]; then
    echo "Still to fill in, in online-server/.env:"
    printf "%b\n" "$missing"
    echo ""
fi
echo "Then start it:  sh online-server/scripts/linux/start.sh"
echo "(or install it as a service that starts with the machine: online-server/scripts/linux/lowstrike.service)"
