# ADR-001 · PostgreSQL como almacén principal

> **Estado:** Aceptado · **Fecha:** 2027-01 · **Decisores:** autora del proyecto  
> **Relacionados:** [Arquitectura §3](../architecture.md#3-modelo-de-datos) · [ADR-002](0002-clean-architecture.md) · [ADR-004](0004-future-improvements.md)

## Contexto

La API necesita:

- **Integridad transaccional** entre el histórico de puntuaciones y la proyección «mejor marca por jugador» (RNF-07).
- **Consultas de ranking** eficientes: Top N, página arbitraria, rango absoluto y ventana relativa (RNF-01: p95 < 100 ms con 100 000 jugadores).
- **Unicidad garantizada por la base de datos** para emails, slugs y nonces (idempotencia y anti-replay, RF-29).
- Un entorno local reproducible con `docker compose up` y tests de integración contra el motor real.
- Encaje natural con EF Core (requisito del stack).

## Decisión

Usar **PostgreSQL 18** (imagen oficial `postgres:18-alpine`) con **EF Core 10 + Npgsql**, aprovechando capacidades específicas del motor:

| Capacidad | Uso concreto |
|---|---|
| `INSERT … ON CONFLICT DO UPDATE` | Upsert atómico de la mejor marca (`leaderboard_entries`) sin lecturas previas ni bloqueos explícitos. |
| Índices B-tree compuestos con `INCLUDE` y comparación de tuplas | `(game_id, rank_key, achieved_at, player_id) INCLUDE (best_score, score_id)` sirve Top N y páginas (*Index Only Scan*), el conteo de rango y la ventana relativa (`(…) < (…)`, *Index Scan Backward*). |
| `citext` | Unicidad de `username`/`email` insensible a mayúsculas sin `LOWER()` en cada consulta. |
| `xmin` | Concurrencia optimista en `games` sin columna adicional (`If-Match`/`ETag`). |
| `jsonb` | Metadatos libres de cada puntuación (nivel, duración…) validados como objeto JSON. |
| Índices únicos | `ux_scores_api_key_id_nonce`, `ux_games_slug`… traducidos a errores de dominio (`409`) o respuestas idempotentes. |

## Alternativas consideradas

| Alternativa | A favor | En contra | Veredicto |
|---|---|---|---|
| **SQL Server** | Excelente integración con .NET, `MERGE`, índices descendentes. | Imagen Docker pesada (~1,5 GB) y licencia para producción; `MERGE` tiene problemas conocidos de concurrencia sin `HOLDLOCK`. | Descartado: peor DX local y coste. |
| **MongoDB** | Esquema flexible, `$setOnInsert`/`$max` para mejores marcas. | Sin transacciones multi-documento triviales en standalone; EF Core provider joven; el dominio es relacional (jugadores ↔ juegos ↔ puntuaciones). | Descartado. |
| **Redis como almacén primario** (Sorted Sets) | `ZADD`/`ZREVRANK` O(log n), ideal para rankings. | Durabilidad y consultas relacionales limitadas; requiere otra base de datos para usuarios, juegos y auditoría; fuera de alcance. | Pospuesto como **caché de ranking** (ADR-004). |
| **SQLite** | Cero infraestructura. | Sin concurrencia de escritura real, sin `citext`/`xmin`; tests no representativos. | Descartado. |

## Consecuencias

**Positivas**

- Las invariantes críticas (una entrada por jugador y juego, un nonce por clave) las garantiza el motor, no el código.
- Tests de integración con **Testcontainers** usando la misma imagen que producción: el SQL específico se prueba de verdad (no hay proveedor InMemory).
- Coste de rango `COUNT(*)` sobre índice: O(rango), aceptable hasta ~1 M de entradas por juego.

**Negativas / riesgos**

- Parte del SQL es específico de PostgreSQL (upsert, `FromSql` en el read service). Cambiar de motor exigiría reescribir `ScoreRepository` y `LeaderboardReadService` (aislados en Infrastructure).
- El cálculo de rango crece linealmente con la posición. Mitigación documentada: Redis Sorted Sets como caché si un juego supera ~1 M de jugadores (ADR-004).

## Validación

- Benchmark k6 con 100 000 jugadores y `EXPLAIN ANALYZE` en [performance.md](../performance.md): p95 < 50 ms en todas las lecturas tras normalizar la clave de ranking (la primera versión, con un índice de direcciones mixtas, no cumplía el objetivo).
- Test de integración concurrente: 40 envíos paralelos del mismo jugador dejan una sola entrada con el máximo y `submissions_count = 40`.
