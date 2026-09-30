#!/usr/bin/env bash
# End-to-end smoke test against a running API (docker compose or dotnet run):
# health → Swagger → register → login → create game → issue API key → signed score → ranking.
#
#   BASE_URL=http://localhost:8080 ./scripts/smoke-test.sh
#
# Requires: bash, curl, openssl, python3.
set -euo pipefail

base_url=${BASE_URL:-http://localhost:8080}
api="${base_url}/api/v1"
root=$(cd "$(dirname "$0")/.." && pwd)
suffix=$(openssl rand -hex 4)

json() { python3 -c "import sys, json; print(json.load(sys.stdin)$1)"; }
step() { printf '\n==> %s\n' "$*"; }
fail() { echo "SMOKE TEST FAILED: $*" >&2; exit 1; }

step "Waiting for ${base_url}/health/ready"
for _ in $(seq 1 60); do
  curl -fsS "${base_url}/health/ready" >/dev/null 2>&1 && break
  sleep 2
done
curl -fsS "${base_url}/health/ready" || fail "API not ready"

step "Swagger UI"
curl -fsS -o /dev/null "${base_url}/swagger/index.html" || fail "Swagger UI not served"
curl -fsS -o /dev/null "${base_url}/openapi/v1.json" || fail "OpenAPI document not served"
echo "ok"

step "Register and login"
email="smoke_${suffix}@example.com"
player_id=$(curl -fsS -H 'Content-Type: application/json' -X POST "${api}/auth/register" \
  -d "{\"username\":\"smoke_${suffix}\",\"email\":\"${email}\",\"password\":\"Sm0keTest-Passw0rd\"}" | json '["id"]')
token=$(curl -fsS -H 'Content-Type: application/json' -X POST "${api}/auth/login" \
  -d "{\"email\":\"${email}\",\"password\":\"Sm0keTest-Passw0rd\"}" | json '["accessToken"]')
echo "player ${player_id}"

step "Create game and API key"
game_id=$(curl -fsS -H 'Content-Type: application/json' -H "Authorization: Bearer ${token}" -X POST "${api}/games" \
  -d "{\"name\":\"Smoke ${suffix}\",\"slug\":\"smoke-${suffix}\",\"scoreOrder\":\"HigherIsBetter\",\"minScore\":0,\"maxScore\":1000000}" | json '["id"]')
key=$(curl -fsS -H 'Content-Type: application/json' -H "Authorization: Bearer ${token}" -X POST "${api}/games/${game_id}/api-keys" -d '{"name":"smoke"}')
key_id=$(echo "$key" | json '["keyId"]')
secret=$(echo "$key" | json '["secret"]')
echo "game ${game_id}, key ${key_id}"

step "Submit HMAC-signed score"
submit=$(BASE_URL="$base_url" "${root}/samples/submit-score.sh" "$game_id" "$key_id" "$secret" "$player_id" 4242 '{"source":"smoke"}')
echo "$submit"
grep -q 'HTTP 201' <<<"$submit" || fail "score submission was not accepted"

step "Read ranking"
top=$(curl -fsS "${api}/games/${game_id}/leaderboard/top?n=5")
echo "$top"
[[ $(echo "$top" | json '[0]["score"]') == 4242 ]] || fail "score not in Top N"
rank=$(curl -fsS "${api}/games/${game_id}/leaderboard/players/${player_id}" | json '["rank"]')
[[ $rank == 1 ]] || fail "unexpected rank ${rank}"

printf '\nSMOKE TEST PASSED\n'
