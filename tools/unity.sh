#!/usr/bin/env bash
# Thin Unity batch wrapper. Runs ON the Studio.
#   tools/unity.sh <logname> [unity args...]
# Sets the Unity path, project path, log file (logs/<logname>.log), waits for exit
# without `timeout` (macOS has none), enforces UNITY_MAX_SEC (default 3600), returns
# Unity's exit code. Prints the log tail on failure.
set -u
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.3.20f1/Unity.app/Contents/MacOS/Unity}"
NAME="${1:?logname}"; shift
LOG="$HERE/logs/$NAME.log"
mkdir -p "$HERE/logs"
: > "$LOG"
MAX="${UNITY_MAX_SEC:-3600}"
START=$(date +%s)
"$UNITY" -batchmode -projectPath "$HERE/unity" -logFile "$LOG" "$@" >/dev/null 2>&1 &
PID=$!
while kill -0 "$PID" 2>/dev/null; do
  sleep 2
  if (( $(date +%s) - START > MAX )); then
    echo "UNITY_TIMEOUT after ${MAX}s, killing $PID" >&2
    kill -9 "$PID" 2>/dev/null
    break
  fi
done
wait "$PID" 2>/dev/null; CODE=$?
ELAPSED=$(( $(date +%s) - START ))
echo "unity[$NAME] exit=$CODE elapsed=${ELAPSED}s log=$LOG"
if [[ $CODE -ne 0 ]]; then
  echo "--- log tail ---" >&2
  grep -E "error CS|Exception|CAPTURE_FAIL|BUILD_FAIL|Aborting batchmode|Scripts have compiler errors" "$LOG" | head -30 >&2
  tail -20 "$LOG" >&2
fi
exit $CODE
