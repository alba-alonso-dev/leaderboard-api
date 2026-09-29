# Documentación de diseño · Leaderboard REST API

Punto de entrada a la documentación de **análisis, diseño, decisiones y resultados** del proyecto (v1.0 implementada). Cada documento existe en Markdown (fuente, versionado junto al código) y en HTML navegable (generado con `scripts/build_docs.py`).

| Documento | Qué responde | Markdown |
|---|---|---|
| [Requerimientos](requirements.html) | ¿Qué debe hacer el sistema y con qué calidad? Actores, RF, RNF medibles, historias de usuario en Gherkin, fuera de alcance. | [requirements.md](./requirements.md) |
| [Arquitectura y diseño](architecture.html) | ¿Cómo se construye? Capas Clean Architecture, modelo E/R, contrato REST, errores, seguridad JWT vs API Key + HMAC. | [architecture.md](./architecture.md) |
| [Roadmap y tareas](roadmap-and-tasks.html) | ¿Cuándo y en qué orden? Fases Noviembre → Marzo, checklists por tarea, trazabilidad y riesgos. | [roadmap-and-tasks.md](./roadmap-and-tasks.md) |
| [Estándares de código](coding-standards.html) | ¿Cómo se escribe y se prueba? Convenciones C#, CQRS liviano, Repository, Result + ProblemDetails, pirámide de tests. | [coding-standards.md](./coding-standards.md) |
| [Rendimiento](performance.html) | ¿Cumple RNF-01? Benchmark k6 con 100 000 jugadores, antes/después y planes de ejecución. | [performance.md](./performance.md) |

## Decisiones de arquitectura (ADR)

| ADR | Decisión | Markdown |
|---|---|---|
| [ADR-001 · PostgreSQL](adr/0001-postgresql.html) | PostgreSQL 18 como almacén principal (upsert atómico, índices con `INCLUDE`, `citext`, `xmin`). | [0001-postgresql.md](./adr/0001-postgresql.md) |
| [ADR-002 · Clean Architecture](adr/0002-clean-architecture.html) | Cuatro capas con dependencias hacia el dominio, CQRS liviano sin mediador, límites verificados por tests. | [0002-clean-architecture.md](./adr/0002-clean-architecture.md) |
| [ADR-003 · Anti-cheat](adr/0003-anti-cheat.html) | API Key + HMAC-SHA256, nonce idempotente, rate limiting, plausibilidad y moderación. | [0003-anti-cheat.md](./adr/0003-anti-cheat.md) |
| [ADR-004 · Mejoras futuras](adr/0004-future-improvements.html) | Redis, tiempo real, microservicios y frontend: disparadores medibles y puntos de extensión. | [0004-future-improvements.md](./adr/0004-future-improvements.md) |

## Lectura recomendada

- **Reclutador / tech lead con 5 minutos:** [README](../README.md) → [Arquitectura §1–2](architecture.html#1-vision-general) → [Roadmap §1](roadmap-and-tasks.html#1-vision-temporal).
- **Revisor técnico:** [Modelo de datos](architecture.html#3-modelo-de-datos) → [Endpoints](architecture.html#4-diseno-de-endpoints-restful) → [Seguridad](architecture.html#5-estrategia-de-seguridad) → [ADR-003](adr/0003-anti-cheat.html) → [Rendimiento](performance.html) → [Testing](coding-standards.html#4-estrategia-de-testing).
- **Quien vaya a implementar:** [Roadmap](roadmap-and-tasks.html) → [Estándares](coding-standards.html) → [Requerimientos](requirements.html) (criterios de aceptación).
