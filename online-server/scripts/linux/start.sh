#!/bin/sh
# Starts the Low Strike online server on this Linux machine, in the background: the API (lowstrike_server.py), which
# starts the game hosts itself and restarts any that stops. Safe to run again: a running API is left alone.
# cloudflared and playit run as their own services on Linux (README steps 2 and 3).
#
#   sh online-server/scripts/linux/start.sh
root=$(cd "$(dirname "$0")/../.." && pwd)
cd "$root"
if [ ! -f .env ]; then echo "online-server/.env is missing: run scripts/linux/setup.sh first."; exit 1; fi
if [ ! -x .venv/bin/python ]; then echo "The Python environment is missing: run scripts/linux/setup.sh first."; exit 1; fi
mkdir -p logs
if [ -f logs/api.pid ] && kill -0 "$(cat logs/api.pid)" 2>/dev/null; then
    echo "API: running (process $(cat logs/api.pid))"
    exit 0
fi
nohup .venv/bin/python lowstrike_server.py --log logs/api.log >/dev/null 2>&1 &
echo $! > logs/api.pid
echo "API: started (process $!; log: online-server/logs/api.log; game hosts: logs/unity-<slot>.log)"
