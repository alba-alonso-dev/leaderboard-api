# Análisis de Requerimientos

> **Documento:** `docs/requirements.md` · **Estado:** Aprobado para Fase 1 (Noviembre) · **Versión:** 1.0  
> **Relacionados:** [Arquitectura](architecture.md) · [Roadmap y tareas](roadmap-and-tasks.md) · [Estándares de código](coding-standards.md)

## 1. Contexto y objetivo

**Leaderboard REST API** es un servicio backend que permite a estudios y desarrolladores de videojuegos:

1. Registrar sus juegos y obtener credenciales de servidor (API Keys) para enviar puntuaciones.
2. Recibir puntuaciones de forma segura (autenticadas, firmadas y limitadas en frecuencia).
3. Exponer clasificaciones (Top N, páginas del ranking, posición absoluta y relativa de un jugador).

Además de su utilidad funcional, el proyecto es una **pieza de portafolio** para entrevistas técnicas: cada decisión debe ser defendible, estar documentada (ADR) y cubierta por tests.

### 1.1 Actores

| Actor | Descripción | Mecanismo de autenticación |
|---|---|---|
| **Jugador** (`Player`) | Persona que se registra, inicia sesión y consulta su posición. | JWT (Bearer) |
| **Propietario de juego** (`GameOwner`) | Un jugador que registra juegos y gestiona sus API Keys. No es un rol distinto: es la relación *owner* de un recurso `Game`. | JWT (Bearer) + autorización por recurso |
| **Servidor de juego** (`GameServer`) | Backend autoritativo del juego que envía puntuaciones en nombre de jugadores. | API Key + firma HMAC-SHA256 |
| **Administrador** (`Admin`) | Moderación: revisar/invalidar puntuaciones sospechosas, desactivar juegos. | JWT con rol `admin` |
| **Visitante anónimo** | Consulta rankings públicos. | Ninguno (limitado por IP) |

### 1.2 Glosario

| Término | Definición |
|---|---|
| **Score** | Evento inmutable de puntuación enviado por un servidor de juego. Histórico completo. |
| **Leaderboard entry** | Mejor puntuación vigente de un jugador en un juego (una fila por par juego–jugador). Es la proyección que se ordena. |
| **Posición absoluta** | Rango 1-based del jugador dentro del juego (1 = mejor). |
| **Posición relativa** | Ventana de *k* jugadores por encima y por debajo de un jugador dado. |
| **Percentil** | `100 × (1 − (rank − 1) / total)`; «estás en el top X %». |
| **Orden de puntuación** | Por juego: `HigherIsBetter` (arcade) o `LowerIsBetter` (speedrun/tiempo). |
| **Desempate** | A igual puntuación gana quien la consiguió **antes** (`AchievedAt` ascendente); luego `PlayerId`. Garantiza rangos únicos y deterministas. |

---

## 2. Requerimientos Funcionales

Prioridad según **MoSCoW** (Must / Should / Could / Won't en esta versión). La columna *Fase* indica el mes objetivo de entrega.

### 2.1 Autenticación y cuentas

| ID | Requerimiento | Prioridad | Fase |
|---|---|---|---|
| **RF-01** | El sistema debe permitir registrar un jugador con `username` (único, 3–32 caracteres `[a-zA-Z0-9_-]`), `email` (único, válido) y `password` (≥ 10 caracteres, al menos una letra y un dígito). | Must | Nov |
| **RF-02** | El sistema debe permitir iniciar sesión con email + password y devolver un **access token JWT** (vida corta) y un **refresh token** (opaco, rotatorio). | Must | Nov |
| **RF-03** | El sistema debe permitir renovar el access token mediante el refresh token, **rotándolo** e invalidando el anterior. Reutilizar un refresh token ya rotado revoca toda la familia (detección de robo). | Should | Dic |
| **RF-04** | El sistema debe permitir cerrar sesión revocando el refresh token actual. | Should | Dic |
| **RF-05** | Un jugador autenticado debe poder consultar su perfil (`/players/me`). | Must | Nov |
| **RF-06** | Las credenciales inválidas deben devolver un error genérico que **no revele** si el email existe. | Must | Nov |

### 2.2 Gestión de juegos y credenciales

| ID | Requerimiento | Prioridad | Fase |
|---|---|---|---|
| **RF-10** | Un jugador autenticado debe poder registrar un juego (`name`, `slug` único, `description`, `scoreOrder`, `minScore`, `maxScore` opcionales). Pasa a ser su **propietario**. | Must | Nov |
| **RF-11** | Cualquier usuario debe poder listar juegos activos (paginado) y consultar el detalle de un juego. | Must | Nov |
| **RF-12** | El propietario debe poder actualizar metadatos del juego y **archivarlo** (soft delete: deja de aceptar scores, el ranking sigue siendo consultable). | Should | Nov |
| **RF-13** | El propietario debe poder **emitir** una API Key para su juego. El secreto se muestra **una única vez** en la respuesta de creación. | Must | Nov |
| **RF-14** | El propietario debe poder listar las API Keys de su juego (solo prefijo/identificador, nombre, fechas; nunca el secreto). | Must | Nov |
| **RF-15** | El propietario debe poder **revocar** una API Key; las peticiones posteriores con ella se rechazan con `401`. | Must | Nov |
| **RF-16** | Un juego puede tener varias API Keys activas simultáneamente (máx. 5) para permitir **rotación sin downtime**. | Should | Ene |

### 2.3 Puntuaciones y clasificaciones

| ID | Requerimiento | Prioridad | Fase |
|---|---|---|---|
| **RF-20** | Un servidor de juego autenticado con API Key debe poder **enviar una puntuación** (`playerId`, `value` entero de 64 bits, `metadata` opcional ≤ 2 KB). | Must | Nov |
| **RF-21** | Cada score se almacena como evento inmutable. Si mejora la mejor marca del jugador (según `scoreOrder`), se actualiza su *leaderboard entry* en la **misma transacción**. | Must | Nov |
| **RF-22** | La respuesta del envío indica si fue **nuevo récord personal**, el rango resultante y el mejor score vigente. | Should | Nov |
| **RF-23** | Se rechazan scores fuera del rango `[minScore, maxScore]` configurado para el juego (`422`). | Must | Nov |
| **RF-24** | Cualquier usuario debe poder consultar el **Top N** de un juego (`1 ≤ N ≤ 100`, por defecto 10). | Must | Nov |
| **RF-25** | Cualquier usuario debe poder recorrer el ranking completo **paginado** (`page`, `pageSize ≤ 100`). | Must | Nov |
| **RF-26** | Cualquier usuario debe poder consultar la **posición absoluta** de un jugador en un juego (rango, mejor score, total de jugadores, percentil). | Must | Nov |
| **RF-27** | Cualquier usuario debe poder consultar la **posición relativa** de un jugador: `k` jugadores por encima y por debajo (`1 ≤ k ≤ 25`). | Must | Nov |
| **RF-28** | Un jugador autenticado debe poder consultar su propia posición (`/leaderboard/me`) y su historial de scores por juego (paginado). | Should | Dic |
| **RF-29** | El envío de scores debe ser **idempotente** frente a reintentos: el mismo `nonce` para la misma API Key no crea un segundo score. | Must | Ene |

### 2.4 Anti-cheat y moderación

| ID | Requerimiento | Prioridad | Fase |
|---|---|---|---|
| **RF-30** | Las peticiones de envío de score deben ir **firmadas con HMAC-SHA256** (timestamp + nonce + hash del cuerpo). Firmas inválidas, timestamps fuera de ventana (±300 s) o nonces repetidos se rechazan. | Must | Ene |
| **RF-31** | El sistema debe limitar la frecuencia de envío por API Key y por jugador (ver RNF-02). | Must | Mar |
| **RF-32** | Los scores que superen umbrales de plausibilidad configurables (p. ej. `> maxScore` o salto anómalo respecto al récord previo) se marcan como `PendingReview` y **no** entran en el ranking. | Could | Mar |
| **RF-33** | Un administrador debe poder invalidar un score; el *leaderboard entry* del jugador se recalcula. | Could | Mar |

### 2.5 Calidad de API (transversal)

| ID | Requerimiento | Prioridad | Fase |
|---|---|---|---|
| **RF-40** | Todas las colecciones se devuelven **paginadas** con un sobre común (`items`, `page`, `pageSize`, `totalCount`, `totalPages`). | Must | Nov |
| **RF-41** | Toda entrada se valida con **FluentValidation**; los errores de validación devuelven `400` con `ValidationProblemDetails` (errores por campo). | Must | Nov |
| **RF-42** | Todas las respuestas de error siguen **RFC 7807 / RFC 9457 ProblemDetails**, incluyendo `traceId` y un `code` de error estable. | Must | Nov |
| **RF-43** | La API está **versionada por URL** (`/api/v1`) y documentada con **OpenAPI 3** + Swagger UI (con esquemas de seguridad Bearer y ApiKey). | Must | Nov |
| **RF-44** | El servicio expone **health checks** `/health/live` y `/health/ready` (este último comprueba PostgreSQL). | Must | Mar |

---

## 3. Requerimientos No Funcionales

Cada RNF tiene un **criterio medible** para poder verificarlo (tests, pipeline o benchmark).

| ID | Categoría | Requerimiento | Criterio de verificación |
|---|---|---|---|
| **RNF-01** | **Rendimiento** | Lecturas de ranking (Top N, posición, ventana) con p95 < **100 ms** y envío de score con p95 < **150 ms**, con 100 000 jugadores en un juego, en el entorno `docker compose` local. Consultas de rango en O(log n + k) apoyadas en índice compuesto. | Benchmark con `k6` (Marzo) + `EXPLAIN ANALYZE` documentado sin *Seq Scan* en rutas calientes. |
| **RNF-02** | **Seguridad** | Cumplir **OWASP API Security Top 10 (2023)**: contraseñas con PBKDF2 (`PasswordHasher<T>`), JWT HS256 con secreto ≥ 256 bits (vida 15 min), refresh tokens hasheados en BD, API Keys cifradas en reposo (Data Protection), HMAC con comparación en tiempo constante, rate limiting (auth: 5 req/min/IP; submit: 60 req/min/API Key; lecturas: 120 req/min/IP), HTTPS, cabeceras de seguridad, sin secretos en el repositorio. | Tests de integración de autorización (401/403/429), `dotnet list package --vulnerable` en CI, secret scanning de GitHub. |
| **RNF-03** | **Mantenibilidad** | Clean Architecture con dependencias apuntando hacia el Dominio; ≥ **80 %** de cobertura de líneas en Domain + Application; análisis estático sin warnings (`TreatWarningsAsErrors`). | Tests de arquitectura (NetArchTest), umbral de cobertura en CI, `dotnet format --verify-no-changes`. |
| **RNF-04** | **Testabilidad** | Toda regla de negocio testeable sin infraestructura; tests de integración contra **PostgreSQL real** (Testcontainers), no InMemory. Tiempo del suite completo < 3 min en CI. | Pipeline de GitHub Actions. |
| **RNF-05** | **Observabilidad** | Logs estructurados JSON (Serilog) con `TraceId`, `RequestPath`, `StatusCode`, `Elapsed`, sin PII ni secretos. Correlación entre log y `traceId` del ProblemDetails. | Test que verifica que el `traceId` de un 500 aparece en el log; revisión de plantillas de log. |
| **RNF-06** | **Portabilidad / DX** | `git clone` → `docker compose up` → Swagger operativo en `http://localhost:8080/swagger` en < 2 min sin instalar .NET. Migraciones aplicadas automáticamente en entorno `Development`. | Job de CI *smoke test* que levanta compose y hace `curl` a `/health/ready`. |
| **RNF-07** | **Fiabilidad / Integridad** | Envío de score + actualización del ranking atómicos (transacción); concurrencia sobre la misma entrada resuelta con `INSERT … ON CONFLICT DO UPDATE` condicional. Ningún score aceptado se pierde. | Test de integración concurrente (N envíos paralelos → estado consistente). |
| **RNF-08** | **Usabilidad de la API** | Contrato OpenAPI completo (ejemplos, códigos de respuesta, esquemas de error). Nombres de recursos en plural, `kebab-case` en rutas, `camelCase` en JSON, fechas ISO-8601 UTC. | Revisión de Swagger; test que valida que el documento OpenAPI se genera sin errores. |
| **RNF-09** | **Escalabilidad (horizonte)** | Diseño *stateless* (JWT, sin sesión en memoria) que permita varias réplicas. Límites conocidos documentados (rate limiting en memoria por instancia; ranking en PostgreSQL). | ADR «Mejoras futuras» (Redis sorted sets, rate limiting distribuido). |
| **RNF-10** | **Privacidad** | Rankings públicos exponen solo `playerId` y `username`; nunca email. Logs sin email ni tokens. | Tests de contrato sobre DTOs públicos. |

---

## 4. Historias de Usuario

Formato: *Como … quiero … para …*, con criterios de aceptación en **Gherkin**. Cada historia referencia los RF que cubre.

### US-01 · Registro de jugador (RF-01, RF-41, RF-42)

*Como* jugador *quiero* crear una cuenta *para* aparecer en las clasificaciones con mi nombre.

```gherkin
Feature: Registro de jugadores

  Scenario: Registro correcto
    Given no existe ningún jugador con email "ana@example.com"
    When envío POST /api/v1/auth/register con username "ana_dev", email "ana@example.com" y password "Sup3rSecret!"
    Then la respuesta es 201 Created
    And la cabecera Location apunta a /api/v1/players/{id}
    And el cuerpo no contiene el campo "password" ni "passwordHash"

  Scenario: Email duplicado
    Given existe un jugador con email "ana@example.com"
    When envío POST /api/v1/auth/register con email "ana@example.com"
    Then la respuesta es 409 Conflict
    And el cuerpo es un ProblemDetails con code "player.email_taken"

  Scenario Outline: Validación de entrada
    When envío POST /api/v1/auth/register con <campo> igual a "<valor>"
    Then la respuesta es 400 Bad Request
    And el ProblemDetails contiene un error para el campo "<campo>"

    Examples:
      | campo    | valor        |
      | username | ab           |
      | email    | no-es-email  |
      | password | corta        |
```

### US-02 · Inicio de sesión (RF-02, RF-06)

*Como* jugador registrado *quiero* iniciar sesión *para* obtener un token con el que usar la API.

```gherkin
Feature: Login

  Scenario: Credenciales válidas
    Given existe el jugador "ana@example.com" con password "Sup3rSecret!"
    When envío POST /api/v1/auth/login con esas credenciales
    Then la respuesta es 200 OK
    And el cuerpo contiene "accessToken", "refreshToken" y "expiresIn" = 900
    And el accessToken contiene el claim "sub" con el id del jugador

  Scenario: Credenciales inválidas no revelan si el email existe
    When envío POST /api/v1/auth/login con email "nadie@example.com"
    And envío POST /api/v1/auth/login con email "ana@example.com" y password incorrecta
    Then ambas respuestas son 401 Unauthorized
    And ambas tienen el mismo code "auth.invalid_credentials"

  Scenario: Fuerza bruta limitada
    Given he enviado 5 logins fallidos desde la misma IP en el último minuto
    When envío otro POST /api/v1/auth/login
    Then la respuesta es 429 Too Many Requests
    And incluye la cabecera Retry-After
```

### US-03 · Registrar un juego y obtener API Key (RF-10, RF-13, RF-14)

*Como* desarrollador de un juego *quiero* registrar mi juego y generar una API Key *para* que mi servidor pueda enviar puntuaciones.

```gherkin
Feature: Registro de juegos y credenciales

  Scenario: Crear juego
    Given estoy autenticado como "ana_dev"
    When envío POST /api/v1/games con name "Space Blaster", slug "space-blaster" y scoreOrder "HigherIsBetter"
    Then la respuesta es 201 Created
    And "ana_dev" es propietaria del juego

  Scenario: Emitir API Key (secreto visible una sola vez)
    Given soy propietaria del juego "space-blaster"
    When envío POST /api/v1/games/{gameId}/api-keys con name "prod-server"
    Then la respuesta es 201 Created
    And el cuerpo contiene "keyId" y "secret"
    When envío GET /api/v1/games/{gameId}/api-keys
    Then la lista contiene "keyId" y "name" pero ningún elemento contiene "secret"

  Scenario: Un no-propietario no puede gestionar claves
    Given estoy autenticado como "bob"
    And "bob" no es propietario de "space-blaster"
    When envío POST /api/v1/games/{gameId}/api-keys
    Then la respuesta es 404 Not Found
```

> Se devuelve `404` (no `403`) para no filtrar la existencia de recursos ajenos (OWASP API1 — BOLA).

### US-04 · Enviar puntuación desde el servidor del juego (RF-20…RF-23, RF-29, RF-30)

*Como* servidor de juego *quiero* enviar la puntuación de una partida *para* que el ranking se actualice.

```gherkin
Feature: Envío de puntuaciones

  Background:
    Given el juego "space-blaster" con scoreOrder "HigherIsBetter" y maxScore 1000000
    And una API Key activa "lbk_7f3a…" con su secreto
    And el jugador "ana_dev" con mejor puntuación 5000

  Scenario: Nuevo récord personal
    When el servidor envía POST /api/v1/games/{gameId}/scores con playerId de "ana_dev" y value 7200 correctamente firmado
    Then la respuesta es 201 Created
    And el cuerpo contiene "isPersonalBest" = true, "bestScore" = 7200 y "rank"

  Scenario: Puntuación inferior al récord
    When el servidor envía un score de 3000 para "ana_dev"
    Then la respuesta es 201 Created
    And "isPersonalBest" = false y "bestScore" = 5000
    And el score queda registrado en el historial

  Scenario: Firma inválida
    When el servidor envía un score con la cabecera X-Signature alterada
    Then la respuesta es 401 Unauthorized con code "apikey.invalid_signature"
    And no se registra ningún score

  Scenario: Replay de la misma petición
    Given el servidor ya envió un score con nonce "b1946ac9"
    When reenvía exactamente la misma petición firmada
    Then la respuesta es 200 OK con el mismo resultado (idempotente)
    And existe un único score con ese nonce

  Scenario: Timestamp fuera de ventana
    When el servidor envía un score con X-Timestamp de hace 10 minutos
    Then la respuesta es 401 Unauthorized con code "apikey.stale_request"

  Scenario: Puntuación fuera de rango
    When el servidor envía un score de 2000000
    Then la respuesta es 422 Unprocessable Entity con code "score.out_of_range"

  Scenario: API Key revocada
    Given la API Key ha sido revocada
    When el servidor envía cualquier score
    Then la respuesta es 401 Unauthorized
```

### US-05 · Consultar el Top N (RF-24, RF-40)

*Como* visitante *quiero* ver los mejores jugadores de un juego *para* conocer la clasificación.

```gherkin
Feature: Top N

  Scenario: Top 3 con desempate por antigüedad
    Given el juego "space-blaster" con las entradas:
      | jugador | mejor | conseguido          |
      | ana     | 900   | 2026-11-01T10:00:00Z |
      | bob     | 900   | 2026-11-01T09:00:00Z |
      | carla   | 1200  | 2026-11-02T08:00:00Z |
      | dani    | 100   | 2026-11-03T08:00:00Z |
    When envío GET /api/v1/games/{gameId}/leaderboard/top?n=3
    Then la respuesta es 200 OK
    And el orden es "carla" (1), "bob" (2), "ana" (3)

  Scenario: Juego con orden LowerIsBetter
    Given un juego "speedrun" con scoreOrder "LowerIsBetter"
    And "ana" con 61000 ms y "bob" con 59000 ms
    When consulto el top 2
    Then "bob" es el rango 1

  Scenario: N fuera de límites
    When envío GET /api/v1/games/{gameId}/leaderboard/top?n=500
    Then la respuesta es 400 Bad Request con error en el campo "n"
```

### US-06 · Consultar mi posición absoluta y relativa (RF-26, RF-27, RF-28)

*Como* jugador *quiero* saber en qué posición estoy y quién está justo por encima y por debajo *para* tener un objetivo alcanzable.

```gherkin
Feature: Posición de un jugador

  Scenario: Posición absoluta
    Given 1000 jugadores en "space-blaster" y "ana" es la 42ª mejor
    When envío GET /api/v1/games/{gameId}/leaderboard/players/{anaId}
    Then la respuesta es 200 OK
    And "rank" = 42, "totalPlayers" = 1000 y "percentile" = 95.9

  Scenario: Ventana relativa
    When envío GET /api/v1/games/{gameId}/leaderboard/players/{anaId}/around?range=2
    Then recibo 5 entradas con rangos 40, 41, 42, 43, 44
    And la entrada de rango 42 tiene "isTarget" = true

  Scenario: Ventana en el borde superior
    Given "carla" es la número 1
    When consulto su ventana con range=2
    Then recibo las entradas con rangos 1, 2, 3

  Scenario: Jugador sin puntuaciones en el juego
    When consulto la posición de un jugador sin entradas
    Then la respuesta es 404 Not Found con code "leaderboard.entry_not_found"

  Scenario: Mi posición
    Given estoy autenticado como "ana"
    When envío GET /api/v1/games/{gameId}/leaderboard/me
    Then obtengo el mismo resultado que para /players/{anaId}
```

### US-07 · Revocar y rotar API Keys (RF-15, RF-16)

*Como* propietaria de un juego *quiero* rotar las credenciales de mi servidor *para* responder a una filtración sin cortar el servicio.

```gherkin
Feature: Rotación de API Keys

  Scenario: Rotación sin downtime
    Given el juego tiene la API Key "A" activa
    When emito la API Key "B"
    And despliego mi servidor con "B"
    And revoco "A"
    Then las peticiones firmadas con "B" devuelven 201
    And las peticiones firmadas con "A" devuelven 401

  Scenario: Límite de claves activas
    Given el juego tiene 5 API Keys activas
    When intento emitir otra
    Then la respuesta es 409 Conflict con code "apikey.limit_reached"
```

### US-08 · Operar y monitorizar el servicio (RF-44, RNF-05, RNF-06)

*Como* ingeniero de operaciones *quiero* health checks y logs estructurados *para* detectar y diagnosticar fallos.

```gherkin
Feature: Operabilidad

  Scenario: Arranque con Docker Compose
    Given un clon limpio del repositorio
    When ejecuto "docker compose up -d"
    Then en menos de 2 minutos GET /health/ready devuelve 200 "Healthy"
    And GET /swagger devuelve la UI de Swagger

  Scenario: Base de datos caída
    Given el contenedor de PostgreSQL está detenido
    When consulto GET /health/ready
    Then la respuesta es 503 "Unhealthy"
    But GET /health/live devuelve 200

  Scenario: Trazabilidad de errores
    When una petición provoca un error no controlado
    Then la respuesta es 500 con ProblemDetails que incluye "traceId"
    And existe una entrada de log de nivel Error con la misma TraceId
    And el ProblemDetails no incluye el stack trace fuera de Development
```

---

## 5. Supuestos, restricciones y fuera de alcance

**Supuestos**

- Los servidores de juego son **autoritativos** (no se aceptan scores directamente desde clientes de jugador); el anti-cheat del servidor protege el canal, no la lógica de juego.
- Volumen objetivo del portafolio: decenas de juegos, ≤ 1 M de entradas por juego.
- Un único despliegue (una instancia) es suficiente; el diseño no impide escalar horizontalmente.

**Restricciones**

- Stack cerrado: ASP.NET Core (.NET 10 LTS), EF Core + PostgreSQL, JWT, OpenAPI/Swagger, xUnit, Docker, GitHub Actions, Serilog.
- Calendario: Noviembre → Marzo, con un entregable demostrable al final de cada mes.

**Fuera de alcance** (se documentarán como ADR de *Mejoras futuras*)

| Tema | Motivo | Alternativa futura |
|---|---|---|
| Frontend | El foco es el diseño de API. | Swagger UI cubre la demo. |
| WebSockets / tiempo real | Complejidad de infraestructura no justificada. | SignalR para «nuevo líder» en vivo. |
| Microservicios | Un monolito modular es la arquitectura adecuada a esta escala. | Extraer *Scoring* si el throughput lo exige. |
| Redis cache | PostgreSQL con índices adecuados cumple RNF-01. | Redis Sorted Sets (`ZADD`/`ZREVRANK`) para rankings de > 10 M entradas; rate limiting distribuido. |
| Temporadas / rankings por periodo | Aumenta el modelo sin aportar señal nueva. | Entidad `Season` + particionado. |
| OAuth externo / verificación de email | No aporta al objetivo de demostración. | OpenID Connect (Entra ID / Auth0). |
