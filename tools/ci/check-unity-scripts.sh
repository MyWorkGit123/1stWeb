#!/usr/bin/env bash
# Compiles the Unity client's scripts against a stub of the UnityEngine API.
#
# Unity itself cannot run in this project's CI, so without this the client's MonoBehaviours would
# never see a compiler at all. A clean run here means the code is structurally sound; it does not
# mean the client works. Only opening the editor proves that.
set -euo pipefail
cd "$(dirname "$0")/../.."
exec dotnet build tools/unity-compile-check/Brinehold.Unity.CompileCheck.csproj -c Release --nologo
