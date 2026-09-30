-- Seeds :players players with one accepted score each into an existing game (psql variables: game_id, api_key_id, players).
-- Usage: psql -v game_id=... -v api_key_id=... -v players=100000 -f perf/seed.sql
BEGIN;

CREATE TEMP TABLE seed AS
SELECT uuidv7() AS player_id,
       uuidv7() AS score_id,
       'perf_' || g AS username,
       (random() * 1000000)::bigint AS value,
       now() - (random() * interval '30 days') AS at
FROM generate_series(1, :players) AS g;

INSERT INTO players (id, username, email, password_hash, role, created_at)
SELECT player_id, username, username || '@perf.local', 'not-a-real-hash', 'player', at FROM seed;

INSERT INTO scores (id, game_id, player_id, api_key_id, value, nonce, metadata, status, submitted_at)
SELECT score_id, :'game_id', player_id, :'api_key_id', value, 'seed-' || score_id, NULL, 0, at FROM seed;

-- HigherIsBetter game: rank_key = -value (lower is better).
INSERT INTO leaderboard_entries (game_id, player_id, best_score, rank_key, score_id, achieved_at, submissions_count)
SELECT :'game_id', player_id, value, -value, score_id, at, 1 FROM seed;

INSERT INTO leaderboard_stats (game_id, player_count)
SELECT :'game_id', COUNT(*) FROM leaderboard_entries WHERE game_id = :'game_id'
ON CONFLICT (game_id) DO UPDATE SET player_count = EXCLUDED.player_count;

COMMIT;
ANALYZE players;
ANALYZE scores;
VACUUM ANALYZE leaderboard_entries; -- sets the visibility map so index-only scans apply
