#!/usr/bin/env bash
# tools/deploy-web.sh [--build]
# Ships builds/webgl to the Railway service as a static site. --build rebuilds the player first.
#
# The site is served by Caddy from a tiny Dockerfile staged outside the repo: `builds/` is gitignored, and
# `railway up` honours .gitignore, so deploying from inside the repo would upload nothing.
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="${AOT_RAILWAY_PROJECT:-6aa3c2dc-4efa-4bb1-9564-5f9ce5eda6b8}"
SERVICE="${AOT_RAILWAY_SERVICE:-9f9ad891-2e26-4b09-8c96-001a6b4c8061}"
ENVIRONMENT="${AOT_RAILWAY_ENV:-6a5367f2-cf4c-4d24-a51a-26aadad46686}"
URL="https://aot-unity-test-production.up.railway.app"

[[ "${1:-}" == "--build" ]] && "$HERE/tools/build.sh" webgl
[[ -f "$HERE/builds/webgl/index.html" ]] || { echo "DEPLOY_FAIL no builds/webgl; run tools/build.sh webgl"; exit 1; }

STAGE="$(mktemp -d)/aot-web"
trap 'rm -rf "$(dirname "$STAGE")"' EXIT
mkdir -p "$STAGE/site"
cp -R "$HERE/builds/webgl/." "$STAGE/site/"
cat > "$STAGE/Caddyfile" <<'CADDY'
:{$PORT:80} {
	root * /srv
	# .unityweb files are gzip streams: declaring the encoding lets the browser inflate them (the fast
	# path) instead of Unity inflating in JavaScript. Caddy's own encoder must not touch them.
	@unityweb path *.unityweb
	header @unityweb Content-Encoding gzip
	header @unityweb Content-Type application/octet-stream
	@static path *.js *.html *.css
	encode @static gzip
	file_server
}
CADDY
cat > "$STAGE/Dockerfile" <<'DOCKER'
FROM caddy:2-alpine
COPY Caddyfile /etc/caddy/Caddyfile
COPY site /srv
DOCKER

cd "$STAGE"
export RAILWAY_CALLER="skill:use-railway@1.3.0" RAILWAY_AGENT_SESSION="aot-deploy-$(date +%s)"
railway up --detach -y --project "$PROJECT" --environment "$ENVIRONMENT" --service "$SERVICE" -m "web build $(date -u +%Y-%m-%dT%H:%MZ)"

for _ in $(seq 1 60); do
  S=$(railway deployment list --project "$PROJECT" --environment "$ENVIRONMENT" --service "$SERVICE" --json 2>/dev/null \
      | python3 -c "import sys,json;print(json.load(sys.stdin)[0]['status'])" 2>/dev/null || true)
  case "$S" in
    SUCCESS) echo "DEPLOY_OK $URL"; exit 0;;
    FAILED|CRASHED) echo "DEPLOY_FAIL status=$S"; exit 1;;
  esac
  sleep 10
done
echo "DEPLOY_FAIL timed out waiting for a terminal status"; exit 1
