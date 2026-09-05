#!/usr/bin/env bash
# Shared helper: are we on the Studio (Unity present) or on a laptop that must ssh?
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.3.20f1/Unity.app/Contents/MacOS/Unity}"
REMOTE_HOST="${AOT_STUDIO:-studio}"
REMOTE_REPO="${AOT_STUDIO_REPO:-dev/lanes/director}"   # the director lane is the working copy that builds; the plain clone is stale
on_studio() { [[ -x "$UNITY" && -z "${AOT_FORCE_REMOTE:-}" ]]; }
remote() { ssh -o BatchMode=yes "$REMOTE_HOST" "cd ~/$REMOTE_REPO && git checkout -q -- . 2>/dev/null; $*"; }   # captures dirty latest.png on the Studio; never let that block a pull
