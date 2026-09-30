# ADR-004 · Mejoras futuras y límites de la versión 1.0

> **Estado:** Aceptado · **Fecha:** 2027-03 · **Decisores:** autora del proyecto  
> **Relacionados:** [Requerimientos §5](../requirements.md#5-supuestos-restricciones-y-fuera-de-alcance) · [Rendimiento §6](../performance.md#6-limites-conocidos) · [ADR-001](0001-postgresql.md) · [ADR-003](0003-anti-cheat.md)

## Contexto

La versión 1.0 prioriza un monolito modular, correcto y medible. Varias capacidades se dejaron **fuera de alcance deliberadamente** (frontend, tiempo real, microservicios, Redis). Este ADR registra por qué, qué señal justificaría incorporarlas y cómo encajarían en la arquitectura actual sin reescrituras.

## Decisión

No implementar ninguna de las siguientes mejoras en 1.0. Cada una tiene un **disparador medible** y un **punto de extensión** ya previsto.

| Mejora | Disparador | Punto de extensión | Diseño propuesto |
|---|---|---|---|
| **Redis Sorted Sets como caché de ranking** | p95 de posición absoluta > 100 ms o juegos con > 1 M de jugadores ([performance.md](../performance.md)). | `ILeaderboardReadService` (Application): se añade una implementación Redis sin tocar handlers. | `ZADD game:{id} <rank_key> <player>` en el mismo caso de uso (o por *outbox*), `ZRANK`/`ZRANGE` para rango y ventana; PostgreSQL sigue siendo la fuente de verdad y permite reconstruir la caché. |
| **Rate limiting distribuido** | Más de una réplica de la API. | Políticas de `Microsoft.AspNetCore.RateLimiting` (`RateLimitingSetup`). | `PartitionedRateLimiter` respaldado por Redis (p. ej. `RedisRateLimiting`) con las mismas particiones (IP, API key). |
| **Tiempo real (WebSockets / SignalR)** | Clientes que necesiten «nuevo líder» sin sondeo. | Evento de dominio tras un envío con `IsPersonalBest` y rango ≤ N. | Hub SignalR `/hubs/leaderboard/{gameId}` con backplane Redis; notificación publicada tras el commit (*outbox*). |
| **Microservicios** | Equipos independientes o escala muy dispar entre ingesta y lectura. | Límites ya separados: *Scoring* (comandos) vs *Ranking* (consultas). | Extraer la ingesta de puntuaciones detrás de una cola; el ranking consume eventos. Hasta entonces, un monolito modular es más simple de operar. |
| **Frontend** | Demo pública para no técnicos. | Contrato OpenAPI estable. | SPA ligera generada desde OpenAPI; Swagger UI cubre la demo técnica. |
| **Paginación keyset (cursor)** | Páginas > 100 000 o listados infinitos. | `GetRangeAsync` ya usa el índice con comparación de tuplas. | `?after=<cursor opaco>` que codifica `(rank_key, achieved_at, player_id)`. |
| **Temporadas / rankings por periodo** | Necesidad de producto. | `leaderboard_entries` indexado por `game_id`. | Añadir `season_id` a la clave y al índice; particionado por temporada. |
| **Detección estadística de trampas** | Moderación manual insuficiente. | `ScoreStatus.PendingReview` ya existe y no entra en el ranking. | Puntuación > p99,9 del juego o salto > X veces el récord personal → `PendingReview`; cola de revisión para administradores. |
| **Observabilidad avanzada** | Despliegue en un entorno con backend de trazas. | `Activity`/`TraceId` ya presentes en logs y ProblemDetails. | OpenTelemetry (trazas + métricas de ASP.NET Core, EF Core y Npgsql) exportando por OTLP. |
| **Firma asimétrica o JWT de servidor** | Integradores que no puedan custodiar secretos compartidos. | Esquema de autenticación `ApiKeyHmac` aislado. | Ed25519 por clave (el servidor guarda solo la pública) o *client credentials* con JWT de corta vida. |
| **Autenticación externa (OIDC)** | Usuarios de plataformas existentes. | Esquema `Bearer` configurable. | Entra ID / Auth0 como emisor; `sub` externo mapeado a `Player`. |

## Consecuencias

- La 1.0 se mantiene pequeña y operable con un solo `docker compose up`.
- Cada mejora tiene una interfaz o esquema existente donde conectarse, por lo que su coste es aditivo.
- Los límites están cuantificados ([performance.md](../performance.md)), así que la decisión de invertir en ellos puede basarse en datos y no en intuición.
