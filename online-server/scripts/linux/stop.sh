#!/bin/sh
# Stops the Low Strike online server on this Linux machine: the API and the game hosts it started.
# (When it runs as the systemd service, stop that instead: sudo systemctl stop lowstrike)
#
#   sh online-server/scripts/linux/stop.sh
root=$(cd "$(dirname "$0")/../.." && pwd)
cd "$root"
stopped=""
if [ -f logs/api.pid ] && kill "$(cat logs/api.pid)" 2>/dev/null; then
    stopped="yes"
    echo "Stopped the API (process $(cat logs/api.pid))"
fi
rm -f logs/api.pid
# Game hosts (and an API started some other way).
for pattern in "lowstrike_server.py" "LowStrikeServer.x86_64.*-ds-server"; do
    if pkill -f "$pattern" 2>/dev/null; then
        stopped="yes"
        echo "Stopped: $pattern"
    fi
done
[ -z "$stopped" ] && echo "The server was not running."
exit 0
