#!/usr/bin/env bash
set -euo pipefail

log() { printf '[entrypoint] %s\n' "$*"; }

log "Starting Expo (no tunnel) on port ${EXPO_PORT}"

log "EXPO_PACKAGER_PROXY_URL=${EXPO_PACKAGER_PROXY_URL:-}"

npx expo start --port "$EXPO_PORT" --host lan &
EXPO_PID=$!

for i in $(seq 1 60); do
  curl -4 -sS --max-time 1 "http://127.0.0.1:${EXPO_PORT}/" >/dev/null 2>&1 && break
  sleep 0.5
done

log "Ready. API: $EXPO_PUBLIC_API_URL"

wait ${EXPO_PID:-0}