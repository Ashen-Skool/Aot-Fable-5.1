#!/usr/bin/env bash
# Runs the autonomous Studio session described in docs/STUDIO-HANDOFF.md inside tmux (session "aot").
# Usage on the Studio: tools/studio_session.sh start | status | log | stop
cd "$(dirname "$0")/.." || exit 1
case "${1:-start}" in
  start)
    mkdir -p logs
    tmux kill-session -t aot 2>/dev/null
    tmux new-session -d -s aot -c "$PWD" "claude -p \"\$(cat docs/STUDIO-HANDOFF.md)\" --dangerously-skip-permissions --verbose --output-format stream-json > logs/studio-claude.log 2>&1; echo EXIT=\$? >> logs/studio-claude.log; sleep 86400"
    sleep 3; tmux ls; echo started ;;
  status) tmux ls 2>/dev/null; ps aux | grep -c '[c]laude -p'; git log --oneline -5 ;;
  log) tail -c 3000 logs/studio-claude.log ;;
  stop) tmux kill-session -t aot; echo stopped ;;
esac
