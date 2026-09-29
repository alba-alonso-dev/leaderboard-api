// k6 load test for the Leaderboard API (RNF-01).
//
//   docker run --rm --network host -v "$PWD/perf:/perf" \
//     -e GAME_ID=... -e KEY_ID=... -e SECRET=... grafana/k6 run /perf/leaderboard.js
//
// Reads: Top N, random page, absolute rank and relative window of random players (100 req/s).
// Writes: HMAC-signed score submissions (20 req/s). Rate limiting must be disabled for the run.
import http from 'k6/http';
import crypto from 'k6/crypto';
import { check } from 'k6';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';
const GAME_ID = __ENV.GAME_ID;
const KEY_ID = __ENV.KEY_ID;
const SECRET = __ENV.SECRET;

export const options = {
  scenarios: {
    reads: { executor: 'constant-arrival-rate', rate: 100, timeUnit: '1s', duration: '60s', preAllocatedVUs: 50, exec: 'reads' },
    writes: { executor: 'constant-arrival-rate', rate: 20, timeUnit: '1s', duration: '60s', preAllocatedVUs: 20, exec: 'writes' },
  },
  thresholds: {
    'http_req_failed': ['rate<0.01'],
    'http_req_duration{kind:top}': ['p(95)<100'],
    'http_req_duration{kind:page}': ['p(95)<100'],
    'http_req_duration{kind:rank}': ['p(95)<100'],
    'http_req_duration{kind:around}': ['p(95)<100'],
    'http_req_duration{kind:submit}': ['p(95)<150'],
  },
};

export function setup() {
  const ids = [];
  for (let i = 0; i < 10; i++) {
    const page = 1 + Math.floor(Math.random() * 1000);
    const res = http.get(`${BASE_URL}/api/v1/games/${GAME_ID}/leaderboard?page=${page}&pageSize=100`);
    res.json('items').forEach((item) => ids.push(item.playerId));
  }
  return { ids };
}

const pick = (items) => items[Math.floor(Math.random() * items.length)];
const board = `${BASE_URL}/api/v1/games/${GAME_ID}/leaderboard`;

export function reads(data) {
  const roll = Math.random();
  let res;
  if (roll < 0.25) {
    res = http.get(`${board}/top?n=10`, { tags: { kind: 'top' } });
  } else if (roll < 0.5) {
    res = http.get(`${board}?page=${1 + Math.floor(Math.random() * 5000)}&pageSize=20`, { tags: { kind: 'page' } });
  } else if (roll < 0.75) {
    res = http.get(`${board}/players/${pick(data.ids)}`, { tags: { kind: 'rank' } });
  } else {
    res = http.get(`${board}/players/${pick(data.ids)}/around?range=5`, { tags: { kind: 'around' } });
  }
  check(res, { 'status 200': (r) => r.status === 200 });
}

export function writes(data) {
  const path = `/api/v1/games/${GAME_ID}/scores`;
  const body = JSON.stringify({ playerId: pick(data.ids), value: Math.floor(Math.random() * 1000000) });
  const timestamp = Math.floor(Date.now() / 1000).toString();
  const nonce = crypto.hexEncode(crypto.randomBytes(16));
  const canonical = ['POST', path, timestamp, nonce, crypto.sha256(body, 'hex')].join('\n');
  const signature = crypto.hmac('sha256', SECRET, canonical, 'base64');

  const res = http.post(`${BASE_URL}${path}`, body, {
    tags: { kind: 'submit' },
    headers: {
      'Content-Type': 'application/json',
      'X-Api-Key': KEY_ID,
      'X-Timestamp': timestamp,
      'X-Nonce': nonce,
      'X-Signature': signature,
    },
  });
  check(res, { 'status 201': (r) => r.status === 201 });
}
