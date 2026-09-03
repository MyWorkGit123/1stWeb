#!/usr/bin/env bash
# Re-simulates every replay in the golden corpus and checks it reproduces its recorded state hashes.
# This is the determinism gate; CI runs it on Linux, Windows and macOS.
set -euo pipefail
cd "$(dirname "$0")/../.."
dotnet build Brinehold.sln -c Release --nologo -v q
exec dotnet run -c Release --no-build --project src/Brinehold.Tools.ReplayCheck -- --dir tests/replays
