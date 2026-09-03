#!/usr/bin/env bash
# Measures tick cost and per-player bandwidth for a busy match.
#
# Reports the numbers the prototype's acceptance criteria are stated in: milliseconds per tick,
# bytes per second per client, and the message counts per replication tier. A jump in the
# correction count means intent replication has regressed.
set -euo pipefail
cd "$(dirname "$0")/../.."
TICKS="${1:-12000}"
exec dotnet run -c Release --project src/Brinehold.Server -- --benchmark --busy --ticks "$TICKS"
