# Documentación de diseño · Leaderboard REST API

Punto de entrada a la fase de **análisis, diseño y planificación** del proyecto. Cada documento existe en Markdown (fuente, versionado junto al código) y en HTML navegable (generado con `scripts/build_docs.py`).

| Documento | Qué responde | Markdown |
|---|---|---|
| [Requerimientos](requirements.html) | ¿Qué debe hacer el sistema y con qué calidad? Actores, RF, RNF medibles, historias de usuario en Gherkin, fuera de alcance. | [requirements.md](./requirements.md) |
| [Arquitectura y diseño](architecture.html) | ¿Cómo se construye? Capas Clean Architecture, modelo E/R, contrato REST, errores, seguridad JWT vs API Key + HMAC. | [architecture.md](./architecture.md) |
| [Roadmap y tareas](roadmap-and-tasks.html) | ¿Cuándo y en qué orden? Fases Noviembre → Marzo, checklists por tarea, trazabilidad y riesgos. | [roadmap-and-tasks.md](./roadmap-and-tasks.md) |
| [Estándares de código](coding-standards.html) | ¿Cómo se escribe y se prueba? Convenciones C#, CQRS liviano, Repository, Result + ProblemDetails, pirámide de tests. | [coding-standards.md](./coding-standards.md) |

## Lectura recomendada

- **Reclutador / tech lead con 5 minutos:** [README](../README.md) → [Arquitectura §1–2](architecture.html#1-vision-general) → [Roadmap §1](roadmap-and-tasks.html#1-vision-temporal).
- **Revisor técnico:** [Modelo de datos](architecture.html#3-modelo-de-datos) → [Endpoints](architecture.html#4-diseno-de-endpoints-restful) → [Seguridad](architecture.html#5-estrategia-de-seguridad) → [Testing](coding-standards.html#4-estrategia-de-testing).
- **Quien vaya a implementar:** [Roadmap](roadmap-and-tasks.html) → [Estándares](coding-standards.html) → [Requerimientos](requirements.html) (criterios de aceptación).

## Próximos documentos

| Documento | Fase |
|---|---|
| `docs/adr/0001-postgresql.md` | Enero |
| `docs/adr/0002-clean-architecture.md` | Enero |
| `docs/adr/0003-anti-cheat.md` | Enero |
| `docs/adr/0004-future-improvements.md` | Marzo |
| `docs/performance.md` (benchmark k6) | Marzo |
