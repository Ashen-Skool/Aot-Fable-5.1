#!/usr/bin/env bash
# tools/play.sh [seed]  -> launches the mac build on the Studio with a fixed seed (for the smoother).
set -u
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$HERE/tools/_remote.sh"
SEED="${1:-42}"; shift || true
EXTRA="$*"
if ! on_studio; then remote "tools/play.sh $SEED $EXTRA"; exit $?; fi
# The executable inside the bundle is named after PlayerSettings.productName, not the .app, so do not
# hardcode it: renaming the product silently broke this script once already.
BIN="$(find "$HERE/builds/mac/AOT.app/Contents/MacOS" -maxdepth 1 -type f -perm -u+x 2>/dev/null | head -1)"
[[ -n "$BIN" && -x "$BIN" ]] || { echo "no mac build; run tools/build.sh mac"; exit 1; }
mkdir -p "$HERE/logs"
nohup "$BIN" -seed "$SEED" -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 -logFile "$HERE/logs/play.log" -shotDir "$HERE/shots/play" $EXTRA >/dev/null 2>&1 &
echo "PLAY_STARTED pid=$! seed=$SEED log=logs/play.log"
