#!/usr/bin/env bash
# tools/test.sh [editmode|playmode|all]  -> runs Unity tests in batch mode, prints counts.
set -u
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$HERE/tools/_remote.sh"
WHICH="${1:-all}"
if ! on_studio; then remote "git pull -q --ff-only 2>/dev/null; tools/test.sh $WHICH"; exit $?; fi
mkdir -p "$HERE/test-results"
FAIL=0; START=$(date +%s)
run() {
  local P="$1" XML="$HERE/test-results/$1.xml" S=$(date +%s)
  rm -f "$XML"
  "$HERE/tools/unity.sh" "test-$P" -runTests -testPlatform "$P" -testResults "$XML" >/dev/null 2>&1
  local CODE=$?
  if [[ ! -f "$XML" ]]; then echo "TEST_FAIL $P: no results xml (unity exit=$CODE)"; FAIL=1; return; fi
  local ROOT; ROOT=$(grep -m1 '<test-run ' "$XML")
  local total passed failed
  total=$(sed -n 's/.* total="\([0-9]*\)".*/\1/p' <<<"$ROOT"); passed=$(sed -n 's/.* passed="\([0-9]*\)".*/\1/p' <<<"$ROOT"); failed=$(sed -n 's/.* failed="\([0-9]*\)".*/\1/p' <<<"$ROOT")
  echo "$P: total=$total passed=$passed failed=$failed ($(( $(date +%s) - S ))s)"
  if [[ "${failed:-1}" != "0" || "${total:-0}" == "0" ]]; then
    FAIL=1
    grep -o '<test-case [^>]*result="Failed"[^>]*' "$XML" | sed -n 's/.*name="\([^"]*\)".*/  FAILED: \1/p'
  fi
}
[[ "$WHICH" == "all" || "$WHICH" == "editmode" ]] && run EditMode
[[ "$WHICH" == "all" || "$WHICH" == "playmode" ]] && run PlayMode
if [[ $FAIL -eq 0 ]]; then echo "TEST_OK ($(( $(date +%s) - START ))s)"; else echo "TEST_FAIL ($(( $(date +%s) - START ))s)"; exit 1; fi
