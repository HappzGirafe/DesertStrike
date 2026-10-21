#!/bin/sh
# Shows whether the Low Strike online server runs on this Linux machine: the API, the game hosts, cloudflared and
# playit, and what the API answers here.
#
#   sh online-server/scripts/linux/status.sh
root=$(cd "$(dirname "$0")/../.." && pwd)
cd "$root"
show() { if pgrep -f "$2" >/dev/null 2>&1; then printf "%-14s running\n" "$1"; else printf "%-14s NOT running\n" "$1"; fi; }
show "API" "lowstrike_server.py"
printf "%-14s %s running\n" "Game hosts" "$(pgrep -fc 'LowStrikeServer.x86_64.*-ds-server' 2>/dev/null || echo 0)"
show "cloudflared" "cloudflared"
show "playit" "playit"
port=$(sed -n 's/^API_PORT=//p' .env 2>/dev/null | tr -d '"\r')
port=${port:-8000}
if command -v curl >/dev/null 2>&1; then
    printf "%-14s " "This machine"
    curl -s -m 5 "http://127.0.0.1:$port/health" || printf "the API does not answer on port %s" "$port"
    echo ""
fi
