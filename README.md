# 🏆 Leaderboard REST API

**API de clasificaciones para videojuegos**: los servidores de juego envían puntuaciones de forma segura y cualquier cliente consulta rankings, Top N y la posición absoluta o relativa de un jugador.

![.NET 10 LTS](https://img.shields.io/badge/.NET-10%20LTS-512BD4?logo=dotnet)
![PostgreSQL](https://img.shields.io/badge/PostgreSQL-18-4169E1?logo=postgresql&logoColor=white)
![Docker](https://img.shields.io/badge/Docker-Compose-2496ED?logo=docker&logoColor=white)
![Clean Architecture](https://img.shields.io/badge/architecture-Clean-success)
![Status](https://img.shields.io/badge/status-design%20phase-orange)
![License](https://img.shields.io/badge/license-MIT-blue)

> **Estado actual: Fase 0 — Diseño completado.** Este repositorio contiene hoy el análisis, la arquitectura y el plan de trabajo. La implementación empieza en noviembre siguiendo el [roadmap](docs/roadmap-and-tasks.md).

---

## ¿Por qué este proyecto?

Es un proyecto de portafolio pensado para demostrar, en un dominio pequeño y fácil de entender, las prácticas que se esperan de un backend profesional en .NET:

| Área | Qué demuestra |
|---|---|
| **Diseño de API** | Contrato REST versionado, paginación, validación con FluentValidation y errores **RFC 9457 ProblemDetails** con códigos estables. |
| **Arquitectura** | **Clean Architecture** (Domain · Application · Infrastructure · API) con CQRS liviano y reglas verificadas por tests de arquitectura. |
| **Seguridad** | Dos esquemas separados: **JWT** con refresh tokens rotatorios para jugadores y **API Key + firma HMAC-SHA256** con anti-replay para servidores de juego. Controles OWASP API Top 10. |
| **Datos** | Modelo evento (histórico de scores) + proyección (mejor marca) en **PostgreSQL**, upsert atómico `ON CONFLICT` e índice compuesto para rangos en O(log n + k). |
| **Calidad** | xUnit + `WebApplicationFactory` + **Testcontainers** contra PostgreSQL real, cobertura con umbral en CI. |
| **Operación** | **Docker Compose**, **GitHub Actions**, logs estructurados con **Serilog**, health checks y **rate limiting** nativo. |
| **Ingeniería** | Decisiones registradas en **ADRs**, requisitos trazables a tareas y tests. |

## Arquitectura

```mermaid
flowchart LR
    subgraph Clients["Clientes"]
        P["👤 Jugador<br/>JWT Bearer"]
        GS["🎮 Servidor de juego<br/>API Key + HMAC"]
        V["🌐 Visitante<br/>anónimo"]
    end

    subgraph Api["Leaderboard.Api (ASP.NET Core · .NET 10)"]
        direction TB
        MW["Middleware<br/>Serilog · ProblemDetails · Rate Limiter · AuthN/AuthZ"]
        EP["Minimal API Endpoints /api/v1"]
        MW --> EP
    end

    subgraph Core["Núcleo"]
        direction TB
        APP["Leaderboard.Application<br/>Commands · Queries · Validators"]
        DOM["Leaderboard.Domain<br/>Player · Game · Score · LeaderboardEntry"]
        APP --> DOM
    end

    INF["Leaderboard.Infrastructure<br/>EF Core · Npgsql · JWT · Data Protection"]
    DB[("PostgreSQL 18")]

    P --> MW
    GS --> MW
    V --> MW
    EP --> APP
    INF -. implementa puertos .-> APP
    INF --> DB
```

Las dependencias apuntan siempre hacia el **Dominio**. Detalle completo de capas, modelo E/R, endpoints y flujos de seguridad en [docs/architecture.md](docs/architecture.md).

### Endpoints principales

| Método | Ruta | Auth | Descripción |
|---|---|---|---|
| `POST` | `/api/v1/auth/register` · `/login` · `/refresh` | — | Registro, login JWT y rotación de tokens |
| `POST` | `/api/v1/games` | JWT | Registrar un juego (el usuario pasa a ser propietario) |
| `POST` | `/api/v1/games/{gameId}/api-keys` | JWT (owner) | Emitir credenciales para el servidor del juego |
| `POST` | `/api/v1/games/{gameId}/scores` | API Key + HMAC | Enviar una puntuación (idempotente por nonce) |
| `GET` | `/api/v1/games/{gameId}/leaderboard/top?n=10` | — | Top N |
| `GET` | `/api/v1/games/{gameId}/leaderboard?page=1&pageSize=20` | — | Ranking paginado |
| `GET` | `/api/v1/games/{gameId}/leaderboard/players/{playerId}` | — | Posición absoluta y percentil |
| `GET` | `/api/v1/games/{gameId}/leaderboard/players/{playerId}/around?range=5` | — | Jugadores por encima y por debajo |

Catálogo completo (22 endpoints), payloads y códigos de error: [architecture.md §4](docs/architecture.md#4-diseño-de-endpoints-restful).

## Stack

| Capa | Tecnología |
|---|---|
| Runtime / Web | ASP.NET Core **.NET 10 LTS**, Minimal APIs |
| Persistencia | EF Core 10 + Npgsql · **PostgreSQL 18** |
| Seguridad | JWT Bearer, `PasswordHasher<T>` (PBKDF2), HMAC-SHA256, ASP.NET Core Data Protection |
| Contrato | OpenAPI 3 (`Microsoft.AspNetCore.OpenApi`) + Swagger UI |
| Validación / errores | FluentValidation · ProblemDetails (RFC 9457) |
| Tests | xUnit v3 · `WebApplicationFactory` · Testcontainers · Respawn · Shouldly · NSubstitute · NetArchTest |
| Observabilidad | Serilog (JSON estructurado) · Health Checks · Rate Limiting nativo |
| DevOps | Docker (imagen *chiseled*, no root) · Docker Compose · GitHub Actions |

## Roadmap

| Mes | Fase | Entregable | Estado |
|---|---|---|---|
| Sep–Oct | **0 · Diseño** | Requerimientos, arquitectura, plan y estándares | ✅ Completado |
| Noviembre | **1 · Base funcional** | Auth JWT, juegos, API Keys, envío de scores, rankings y Swagger | ⏳ Pendiente |
| Diciembre | **2 · Calidad** | Tests unitarios + integración (Testcontainers), refresh tokens, README extenso | ⏳ Pendiente |
| Enero | **3 · Arquitectura** | Refactor a Clean Architecture, firma HMAC + idempotencia, ADR-001/002/003 | ⏳ Pendiente |
| Febrero | **4 · DevOps** | Dockerfile, Docker Compose con PostgreSQL, CI en GitHub Actions | ⏳ Pendiente |
| Marzo | **5 · Producción** | Serilog estructurado, Health Checks, Rate Limiting, benchmark, `v1.0.0` | ⏳ Pendiente |

Desglose en más de 100 tareas con checklists, trazabilidad RF → tarea y riesgos: [docs/roadmap-and-tasks.md](docs/roadmap-and-tasks.md).

## Inicio rápido

> 🚧 **Placeholder** — disponible a partir de la Fase 4 (Febrero). Así será el flujo definitivo:

```bash
git clone https://github.com/alba-alonso-dev/leaderboard-api.git
cd leaderboard-api
cp .env.example .env          # valores de desarrollo (clave JWT, contraseña de PostgreSQL)
docker compose up -d --wait   # API + PostgreSQL; aplica migraciones al arrancar

open http://localhost:8080/swagger   # (Linux: xdg-open)
curl http://localhost:8080/health/ready
```

**Recorrido de demo en Swagger:** `POST /auth/register` → `POST /auth/login` → *Authorize* → `POST /games` → `POST /games/{id}/api-keys` → `POST /games/{id}/scores` (firmado; script en `samples/`) → `GET /games/{id}/leaderboard/top`.

**Ejecutar tests** (requiere .NET 10 SDK y Docker):

```bash
dotnet test
```

## Documentación de diseño

La documentación está en Markdown (fuente) y en HTML navegable con diagramas renderizados.

| Documento | Markdown | HTML |
|---|---|---|
| Índice de documentación | [docs/index.md](docs/index.md) | [docs/index.html](docs/index.html) |
| Análisis de requerimientos | [docs/requirements.md](docs/requirements.md) | [docs/requirements.html](docs/requirements.html) |
| Arquitectura y diseño | [docs/architecture.md](docs/architecture.md) | [docs/architecture.html](docs/architecture.html) |
| Roadmap y tareas | [docs/roadmap-and-tasks.md](docs/roadmap-and-tasks.md) | [docs/roadmap-and-tasks.html](docs/roadmap-and-tasks.html) |
| Estándares de código y testing | [docs/coding-standards.md](docs/coding-standards.md) | [docs/coding-standards.html](docs/coding-standards.html) |

Para ver los HTML en local:

```bash
python3 -m pip install -r scripts/requirements-docs.txt   # una sola vez
python3 scripts/build_docs.py           # regenera docs/*.html desde docs/*.md
python3 -m http.server 8000 -d docs     # abrir http://localhost:8000
```

> Los HTML también pueden publicarse con **GitHub Pages** (Settings → Pages → *Deploy from branch* → `main` / `/docs`).

## Decisiones de arquitectura (ADR)

| ADR | Tema | Fase |
|---|---|---|
| ADR-001 | PostgreSQL como almacén principal | Enero |
| ADR-002 | Adopción de Clean Architecture | Enero |
| ADR-003 | Anti-cheat: HMAC + nonce + rate limiting + plausibilidad | Enero |
| ADR-004 | Mejoras futuras: Redis, tiempo real, microservicios, frontend | Marzo |

## Fuera de alcance

Frontend, WebSockets/tiempo real, microservicios y Redis quedan fuera de esta versión de forma deliberada; se justificarán y describirán como evolución en el ADR-004 de *Mejoras futuras*.

## Criterio de aceptación del proyecto

> Clonar el repositorio → `docker compose up` → Swagger accesible en local para probar **Auth + Score Submit + Ranking** → pipeline de CI **en verde** en GitHub Actions.

## Licencia

[MIT](LICENSE)
