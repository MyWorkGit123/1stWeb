#!/usr/bin/env bash
# Runs a headless two-player match locally and prints the state hash every ten seconds.
#
#   tools/dev/run-local-match.sh              # real-time match, ctrl-c to stop
#   tools/dev/run-local-match.sh --benchmark  # as fast as the machine allows
set -euo pipefail
cd "$(dirname "$0")/../.."
exec dotnet run -c Release --project src/Brinehold.Server -- "$@"
