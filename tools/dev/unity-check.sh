#!/usr/bin/env bash
# Compiles and play-mode-tests the Brinehold Unity client headlessly, then prints a short report.
#
# Unity cannot run in this project's CI, so this is the check that closes the loop on a machine
# that has an editor. Batch mode means no interaction and a real pass/fail.
#
#   tools/dev/unity-check.sh [path-to-unity-editor]
#
# Send back unity.log and unity-tests.xml if anything fails.
set -uo pipefail
cd "$(dirname "$0")/../.."
REPO="$PWD"
PROJECT="$REPO/unity/BrineholdClient"
UNITY="${1:-}"

if [ -z "$UNITY" ]; then
  for candidate in \
    "$HOME"/Unity/Hub/Editor/*/Editor/Unity \
    /Applications/Unity/Hub/Editor/*/Unity.app/Contents/MacOS/Unity ; do
    [ -x "$candidate" ] && UNITY="$candidate"
  done
fi

if [ -z "$UNITY" ] || [ ! -x "$UNITY" ]; then
  echo "Could not find Unity. Pass the editor path as the first argument." >&2
  exit 2
fi

echo "Unity:   $UNITY"
echo "Project: $PROJECT"

echo
echo "Building the .NET side first..."
dotnet build "$REPO/Brinehold.sln" -c Release --nologo -v q || {
  echo "The .NET build failed; fix that before opening Unity." >&2; exit 1; }

LOG="$REPO/unity.log"
RESULTS="$REPO/unity-tests.xml"
rm -f "$LOG" "$RESULTS"

echo
echo "Running play-mode tests (batch mode). The first import takes a few minutes..."
"$UNITY" -batchmode -projectPath "$PROJECT" -runTests -testPlatform PlayMode \
         -testResults "$RESULTS" -logFile "$LOG"
UNITY_EXIT=$?

echo
echo "──────── compile errors ────────"
if [ -f "$LOG" ]; then
  grep -oE "[^ ]+\.cs\([0-9]+,[0-9]+\): error CS[0-9]+: .*" "$LOG" | sort -u | head -40 || echo "  none"
else
  echo "  no log was produced — Unity may not have started"
fi

if [ -f "$RESULTS" ]; then
  echo
  echo "──────── play-mode tests ────────"
  python3 - "$RESULTS" <<'PY' 2>/dev/null || echo "  (install python3 to summarise, or read $RESULTS)"
import sys, xml.etree.ElementTree as ET
run = ET.parse(sys.argv[1]).getroot()
print(f"  total {run.get('total')}  passed {run.get('passed')}  "
      f"failed {run.get('failed')}  skipped {run.get('skipped')}")
for case in run.iter('test-case'):
    if case.get('result') == 'Failed':
        print(f"   - {case.get('fullname')}")
        failure = case.find('failure/message')
        if failure is not None and failure.text:
            print(f"     {failure.text.strip()}")
PY
fi

echo
echo "Full log:     $LOG"
echo "Test results: $RESULTS"
echo
echo "If anything failed, send those two files back."
exit $UNITY_EXIT
