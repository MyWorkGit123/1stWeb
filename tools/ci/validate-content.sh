#!/usr/bin/env bash
# Validates the authored content files and checks they agree with the shipped code defaults.
# A content set that loads but is unplayable is worse than one that fails outright, so this
# checks structure as well as syntax.
set -euo pipefail
cd "$(dirname "$0")/../.."
dotnet build Brinehold.sln -c Release --nologo -v q
exec dotnet run -c Release --no-build --project src/Brinehold.Tools.ContentCheck -- --compare-default
