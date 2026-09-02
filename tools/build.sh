#!/usr/bin/env bash
# tools/build.sh mac|webgl   -> builds/mac/AOT.app | builds/webgl/
set -u
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$HERE/tools/_remote.sh"
T="${1:-mac}"
if ! on_studio; then remote "git pull -q --ff-only 2>/dev/null; tools/build.sh $T"; exit $?; fi
case "$T" in
  mac)   METHOD=Build.Builder.Mac;   TARGET=OSXUniversal ;;
  webgl) METHOD=Build.Builder.WebGL; TARGET=WebGL ;;
  *) echo "usage: build.sh mac|webgl"; exit 2 ;;
esac
START=$(date +%s)
"$HERE/tools/unity.sh" "build-$T" -quit -buildTarget "$TARGET" -executeMethod "$METHOD"
CODE=$?
LINE=$(grep -E "^BUILD_(OK|FAIL)" "$HERE/logs/build-$T.log" | tail -1)
[[ -z "$LINE" ]] && LINE="BUILD_FAIL $T no verdict line in log (exit=$CODE)"
echo "$LINE (wall $(( $(date +%s) - START ))s)"
[[ "$LINE" == BUILD_OK* ]] || exit 1
