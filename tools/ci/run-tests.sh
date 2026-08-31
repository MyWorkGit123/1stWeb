#!/usr/bin/env bash
# The check a contributor runs before pushing. Mirrors the CI build job.
set -euo pipefail
cd "$(dirname "$0")/../.."
dotnet build Brinehold.sln -c Release --nologo
dotnet test Brinehold.sln -c Release --no-build --nologo
