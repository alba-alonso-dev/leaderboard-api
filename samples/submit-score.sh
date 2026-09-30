#!/usr/bin/env bash
# Submits a score as a game server, signing the request with HMAC-SHA256.
#
#   ./samples/submit-score.sh <game-id> <key-id> <secret> <player-id> <value> [metadata-json]
#
# Environment: BASE_URL (default http://localhost:8080), NONCE (optional: reuse one to test idempotency).
# Requires: bash, curl, openssl.
#
# canonical = METHOD \n PATH \n TIMESTAMP \n NONCE \n hex(SHA256(body))
# signature = Base64(HMAC-SHA256(secret, canonical))
set -euo pipefail

if [[ $# -lt 5 ]]; then
  sed -n '2,10p' "$0" >&2
  exit 64
fi

game_id=$1 key_id=$2 secret=$3 player_id=$4 value=$5 metadata=${6:-}
base_url=${BASE_URL:-http://localhost:8080}
path="/api/v1/games/${game_id}/scores"

if [[ -n $metadata ]]; then
  body=$(printf '{"playerId":"%s","value":%s,"metadata":%s}' "$player_id" "$value" "$metadata")
else
  body=$(printf '{"playerId":"%s","value":%s}' "$player_id" "$value")
fi

timestamp=$(date +%s)
nonce=${NONCE:-$(openssl rand -hex 16)}
body_hash=$(printf '%s' "$body" | openssl dgst -sha256 -hex | awk '{print $NF}')
canonical=$(printf 'POST\n%s\n%s\n%s\n%s' "$path" "$timestamp" "$nonce" "$body_hash")
signature=$(printf '%s' "$canonical" | openssl dgst -sha256 -hmac "$secret" -binary | base64)

curl -sS -X POST "${base_url}${path}" \
  -H "Content-Type: application/json" \
  -H "X-Api-Key: ${key_id}" \
  -H "X-Timestamp: ${timestamp}" \
  -H "X-Nonce: ${nonce}" \
  -H "X-Signature: ${signature}" \
  --data-binary "$body" \
  -w '\nHTTP %{http_code}\n'
