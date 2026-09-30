# Rendimiento (RNF-01)

> **Documento:** `docs/performance.md` · **Fecha de la medición:** 2027-03 · **Versión de la API:** 1.0  
> **Relacionados:** [Requerimientos RNF-01](requirements.md#3-requerimientos-no-funcionales) · [Arquitectura §3](architecture.md#3-modelo-de-datos) · [ADR-001](adr/0001-postgresql.md)

## 1. Objetivo

| Operación | Objetivo p95 |
|---|---|
| Top N, página del ranking, posición absoluta, ventana relativa | **< 100 ms** |
| Envío de puntuación firmado (HMAC) | **< 150 ms** |

Con **100 000 jugadores** en un mismo juego y el entorno `docker compose` local.

## 2. Metodología

| Elemento | Valor |
|---|---|
| Entorno | `docker compose` (API en imagen *chiseled* + `postgres:18-alpine`), 4 vCPU, misma máquina que el generador de carga |
| Datos | 100 000 jugadores con una puntuación cada uno (`perf/seed.sql`), `VACUUM ANALYZE` tras la carga |
| Herramienta | [k6](https://k6.io) (`perf/leaderboard.js`), escenarios de tasa constante durante 60 s |
| Carga | **100 lecturas/s** (25 % Top 10, 25 % página aleatoria de 20 entre las primeras 5 000, 25 % posición absoluta, 25 % ventana `range=5`) + **20 envíos firmados/s** |
| Configuración | `RATE_LIMITING_ENABLED=false` (si no, el rate limiting por IP limita al propio k6) |

Reproducción:

```bash
RATE_LIMITING_ENABLED=false docker compose up -d --build --wait
# 1) Crear juego + API key (Swagger o scripts/smoke-test.sh) y anotar GAME_ID, API_KEY_ID (uuid), KEY_ID y SECRET
docker compose exec -T postgres psql -U leaderboard -d leaderboard \
  -v game_id=$GAME_ID -v api_key_id=$API_KEY_ID -v players=100000 < perf/seed.sql
docker run --rm --network host -v "$PWD/perf:/perf" \
  -e GAME_ID=$GAME_ID -e KEY_ID=$KEY_ID -e SECRET=$SECRET grafana/k6 run /perf/leaderboard.js
```

## 3. Resultados

La primera medición **no cumplía** el objetivo; el análisis de los planes de ejecución llevó a un rediseño del índice y de las consultas.

| Operación (p95) | Diseño inicial | Diseño final | Objetivo |
|---|---:|---:|---:|
| Top 10 | 263 ms | **11,6 ms** | < 100 ms ✅ |
| Página (offset ≤ 100 000) | 4 670 ms | **48,8 ms** | < 100 ms ✅ |
| Posición absoluta | 782 ms | **32,9 ms** | < 100 ms ✅ |
| Ventana relativa (±5) | 2 850 ms | **38,8 ms** | < 100 ms ✅ |
| Envío firmado | 1 090 ms | **52,9 ms** | < 150 ms ✅ |
| Throughput sostenido | 57 req/s (saturado, 50 % de iteraciones descartadas) | **120 req/s** (0 descartadas) | 120 req/s |
| Errores | 0 % | 0 % | < 1 % |

## 4. Diagnóstico y cambios

| Síntoma (diseño inicial) | Causa en el plan | Cambio |
|---|---|---|
| Página: 168 ms en frío, segundos bajo carga | *Hash Join* de las 100 000 filas con `players` + *external merge sort* en disco: se paginaba **después** del join. | Paginar el índice primero (`ORDER BY … OFFSET … LIMIT` en subconsulta) y unir usuarios solo para las filas devueltas. |
| Cada petición de ranking pagaba 17 ms extra | `COUNT(*)` total con *Seq Scan* para calcular `totalCount`/percentil. | Contador exacto `leaderboard_stats.player_count`, mantenido **en la misma sentencia** del upsert (`RETURNING (xmax = 0)` detecta la primera entrada del jugador). |
| Ventana relativa en segundos | El índice `(sort_key DESC, achieved_at ASC, player_id ASC)` mezclaba direcciones: no admite comparación de tuplas y PostgreSQL ordenaba decenas de miles de filas por petición. | Clave normalizada **`rank_key` (menor es mejor)** e índice **totalmente ascendente** `(game_id, rank_key, achieved_at, player_id) INCLUDE (best_score, score_id)`. Las búsquedas usan `(rank_key, achieved_at, player_id) < (…)` → *Index Scan Backward* con `LIMIT k`. |

## 5. Planes de ejecución (diseño final)

**Top 10** — 0,4 ms, *Index Only Scan* + 10 búsquedas por PK:

```text
Nested Loop (actual time=0.082..0.276 rows=10)
  ->  Limit
        ->  Index Only Scan using ix_leaderboard_entries_rank (rows=10)  Index Cond: (game_id = <game>)
  ->  Index Scan using pk_players on players p (loops=10)
Execution Time: 0.378 ms
```

**Ventana relativa (5 por encima del jugador 50 001)** — 0,08 ms:

```text
Limit (actual time=0.037..0.045 rows=5)
  ->  Index Scan Backward using ix_leaderboard_entries_rank
        Index Cond: ((game_id = <game>) AND (ROW(rank_key, achieved_at, player_id) < ROW(...)))
Execution Time: 0.081 ms
```

**Página profunda (OFFSET 50 000)** — 39 ms, *Index Only Scan* que recorre 50 020 entradas del índice:

```text
Nested Loop (rows=20)
  ->  Limit  ->  Index Only Scan using ix_leaderboard_entries_rank (rows=50020)
  ->  Index Scan using pk_players (loops=20)
Execution Time: 38.769 ms
```

**Posición absoluta del jugador 50 001** — 19 ms, cuenta las 50 000 entradas por delante:

```text
Aggregate  ->  Seq Scan on leaderboard_entries  Filter: (ROW(rank_key, achieved_at, player_id) < ROW(...))
Execution Time: 19.180 ms
```

## 6. Límites conocidos

| Límite | Coste | Cuándo importa | Evolución (ADR-004) |
|---|---|---|---|
| Paginación por offset | O(offset) | Páginas muy profundas (> 100 000) | Paginación *keyset* con cursor opaco (`?after=`) reutilizando la misma comparación de tuplas. |
| Rango absoluto | O(rank) | Juegos con > 1 M de jugadores y consultas de jugadores en la cola | Redis Sorted Sets (`ZREVRANK`, O(log n)) como caché del ranking. |
| Contador de jugadores | 1 fila caliente por juego al entrar jugadores nuevos | Picos de miles de altas por segundo en un mismo juego | Contador particionado o agregación asíncrona. |
| Generador y sistema bajo prueba en la misma máquina | Resultados conservadores | — | Medir en infraestructura separada antes de producción. |
