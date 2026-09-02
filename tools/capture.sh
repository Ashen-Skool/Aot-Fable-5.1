#!/usr/bin/env bash
# tools/capture.sh <piece|all> [pose,pose,...]
# Renders tools/poses.json poses to shots/<piece>/<pose>.png on the Studio.
# From a laptop: runs it over ssh, then rsyncs shots/ back here.
set -u
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$HERE/tools/_remote.sh"
PIECE="${1:-harness}"; POSES="${2:-all}"
[[ "$PIECE" == "all" ]] && PIECE="harness"
if ! on_studio; then
  remote "git pull -q --ff-only 2>/dev/null; tools/capture.sh $PIECE $POSES"; CODE=$?
  mkdir -p "$HERE/shots"
  rsync -aq "$REMOTE_HOST:~/$REMOTE_REPO/shots/" "$HERE/shots/" && echo "synced shots/ -> $HERE/shots"
  exit $CODE
fi
START=$(date +%s)
"$HERE/tools/unity.sh" "capture-$PIECE" -executeMethod Capture.Entry.Run -piece "$PIECE" -poses "$POSES" -shots "$HERE/shots"
CODE=$?
LOG="$HERE/logs/capture-$PIECE.log"
LINE=$(grep -E "^CAPTURE_(OK|FAIL)" "$LOG" | tail -1)
[[ -z "$LINE" ]] && LINE="CAPTURE_FAIL no verdict line in log (exit=$CODE)"
# keep the newest shot per piece as latest.png (committed; the progress page shows it)
DIR="$HERE/shots/$PIECE"
if [[ -d "$DIR" ]]; then
  NEWEST=$(ls -t "$DIR"/*.png 2>/dev/null | grep -v latest.png | head -1)
  [[ -n "$NEWEST" ]] && cp "$NEWEST" "$DIR/latest.png"
fi
echo "$LINE ($(( $(date +%s) - START ))s)"
[[ "$LINE" == CAPTURE_OK* ]] || exit 1
