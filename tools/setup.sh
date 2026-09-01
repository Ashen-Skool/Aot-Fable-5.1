#!/usr/bin/env bash
# One-time project setup on the Studio: resolve packages, then create URP assets,
# base materials, Main scene, player settings. Safe to re-run.
set -u
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$HERE/tools/_remote.sh"
if ! on_studio; then remote "tools/setup.sh"; exit $?; fi
"$HERE/tools/unity.sh" setup -quit -executeMethod Setup.All.Run || exit 1
grep -q SETUP_OK "$HERE/logs/setup.log" && echo SETUP_OK || { echo SETUP_FAIL; exit 1; }
