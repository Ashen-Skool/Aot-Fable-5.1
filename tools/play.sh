#!/usr/bin/env bash
# tools/play.sh [seed]  -> launches the mac build on the Studio with a fixed seed (for the smoother).
set -u
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$HERE/tools/_remote.sh"
SEED="${1:-42}"; shift || true
EXTRA="$*"
if ! on_studio; then remote "tools/play.sh $SEED $EXTRA"; exit $?; fi
BIN="$HERE/builds/mac/AOT.app/Contents/MacOS/AOT"
[[ -x "$BIN" ]] || { echo "no mac build; run tools/build.sh mac"; exit 1; }
mkdir -p "$HERE/logs"
nohup "$BIN" -seed "$SEED" -screen-width 1920 -screen-height 1080 -screen-fullscreen 0 -logFile "$HERE/logs/play.log" -shotDir "$HERE/shots/play" $EXTRA >/dev/null 2>&1 &
echo "PLAY_STARTED pid=$! seed=$SEED log=logs/play.log"
