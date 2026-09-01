#!/usr/bin/env bash
# Runs a dedicated server and two headless clients as separate processes over real UDP sockets.
#
#   tools/dev/run-networked-match.sh [port] [seconds]
#
# This is the two-machine test running on one machine. To use two machines, start the server with
#   dotnet run -c Release --project src/Brinehold.Server -- --port 7777 --players 2
# and on each client machine
#   dotnet run -c Release --project src/Brinehold.Tools.TestClient -- --host <server-ip> --port 7777 --name <name>
set -euo pipefail
cd "$(dirname "$0")/../.."

PORT="${1:-7777}"
SECONDS_TO_RUN="${2:-40}"
TICKS=$(( SECONDS_TO_RUN * 20 + 200 ))

dotnet build Brinehold.sln -c Release --nologo -v q

echo "Starting server on port $PORT…"
dotnet run -c Release --no-build --project src/Brinehold.Server -- \
    --port "$PORT" --players 2 --ticks "$TICKS" &
SERVER_PID=$!
trap 'kill $SERVER_PID 2>/dev/null || true' EXIT
sleep 3

dotnet run -c Release --no-build --project src/Brinehold.Tools.TestClient -- \
    --host 127.0.0.1 --port "$PORT" --name PlayerA --seconds "$SECONDS_TO_RUN" &
A=$!
sleep 1
dotnet run -c Release --no-build --project src/Brinehold.Tools.TestClient -- \
    --host 127.0.0.1 --port "$PORT" --name PlayerB --seconds "$SECONDS_TO_RUN" &
B=$!

wait $A $B
sleep 2
echo "Done."
