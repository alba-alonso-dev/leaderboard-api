# ADR-003 · Estrategia anti-cheat para el envío de puntuaciones

> **Estado:** Aceptado · **Fecha:** 2027-01 · **Decisores:** autora del proyecto  
> **Relacionados:** [Arquitectura §5](../architecture.md#5-estrategia-de-seguridad) · [Requerimientos RF-29…RF-33](../requirements.md#24-anti-cheat-y-moderacion)

## Contexto

Un leaderboard solo tiene valor si sus puntuaciones son creíbles. Amenazas principales:

| Amenaza | Ejemplo |
|---|---|
| **Falsificación** | Un jugador descubre el endpoint y envía `value: 999999`. |
| **Robo de credencial** | La API Key aparece en logs de un proxy o en un binario del cliente. |
| **Replay** | Se captura una petición válida y se reenvía para inflar estadísticas. |
| **Manipulación en tránsito** | Se altera `value` en una petición legítima. |
| **Inundación** | Un servidor comprometido envía miles de puntuaciones por minuto. |
| **Valores imposibles** | Puntuaciones fuera de lo que el juego permite. |

Supuesto clave: **los servidores de juego son autoritativos**. El cliente del jugador nunca envía puntuaciones; si el propio servidor está comprometido, ninguna medida de canal lo evita (para eso está la moderación).

## Decisión

Defensa en profundidad con cinco capas:

1. **API Key por juego + firma HMAC-SHA256** de cada petición.
   - Cabeceras: `X-Api-Key` (id público `lbk_…`), `X-Timestamp`, `X-Nonce`, `X-Signature`.
   - `signature = Base64(HMAC-SHA256(secret, METHOD\nPATH\nTIMESTAMP\nNONCE\nhex(SHA256(body))))`.
   - El **secreto nunca viaja**; se guarda cifrado con ASP.NET Core Data Protection (debe ser recuperable para recalcular la firma) y se compara con `CryptographicOperations.FixedTimeEquals`.
2. **Frescura + unicidad**: ventana de ±300 s para el timestamp y `UNIQUE (api_key_id, nonce)` en base de datos. Un reenvío idéntico devuelve el resultado original (`200`, idempotente); el mismo nonce con otro cuerpo devuelve `409 score.nonce_reused`.
3. **Rate limiting**: *token bucket* por API Key (60/min, ráfaga 20) en el middleware nativo, más un límite por jugador y juego (10/min) en el caso de uso.
4. **Plausibilidad**: `minScore`/`maxScore` por juego → `422 score.out_of_range`; y un nuevo récord personal más de 10× mejor que el anterior (configurable en `Scores:MaxImprovementFactor`) se guarda como `PendingReview` (`202`) sin entrar en el ranking hasta que un administrador lo aprueba (`POST /api/v1/admin/scores/{id}/approve`) o lo invalida.
5. **Moderación y auditoría**: histórico inmutable de puntuaciones; un administrador puede invalidar una puntuación y la mejor marca se recalcula desde el histórico. Los rechazos se registran (`Rejected game-server request with key {KeyId}: {ErrorCode}`) sin datos sensibles.

## Alternativas consideradas

| Alternativa | Por qué no (o no solo) |
|---|---|
| **API Key en claro en cabecera** | Una sola filtración permite enviar puntuaciones arbitrarias indefinidamente y no protege la integridad del cuerpo. |
| **JWT de servidor (client credentials)** | Protege la identidad pero no la integridad del cuerpo ni el replay dentro de la vida del token; requiere un endpoint de emisión de tokens adicional. Válido como evolución si hay muchos consumidores. |
| **mTLS** | Máxima seguridad de canal, pero gestión de certificados desproporcionada para un proyecto de portafolio y difícil de demostrar en Swagger. |
| **Firma asimétrica (Ed25519)** | El servidor no necesitaría guardar secretos recuperables. Más compleja para los integradores; se deja como mejora si se exige no almacenar secretos. |
| **Detección estadística (z-score, p99)** | Útil, pero requiere datos históricos y ajuste. En 1.0 se implementa una heurística simple y explicable (salto relativo sobre el récord personal) que alimenta la misma cola `PendingReview`; la versión estadística queda como mejora futura (ADR-004). |

## Consecuencias

**Positivas**

- Capturar una petición solo permite reenviarla durante 5 minutos, y ese reenvío es inocuo (idempotente).
- Alterar el cuerpo invalida la firma (`401 apikey.invalid_signature`).
- Rotación sin downtime: hasta 5 claves activas por juego.
- Swagger sigue siendo usable: en Development la UI firma las peticiones en el navegador con WebCrypto (`wwwroot/swagger-hmac.js`); el secreto no se envía al servidor.

**Negativas / riesgos aceptados**

- El secreto se almacena **cifrado, no hasheado**: si se filtran a la vez la base de datos y el *key ring* de Data Protection, las claves quedan expuestas. Mitigación: key ring en volumen separado (en producción, Key Vault) y revocación inmediata.
- Los límites de frecuencia son **por instancia** (en memoria). Con varias réplicas harían falta límites distribuidos (Redis) — ADR-004.
- Un servidor de juego comprometido puede enviar puntuaciones plausibles; solo la moderación lo corrige.

## Validación

Tests de integración: firma alterada, cuerpo alterado, timestamp caducado, cabeceras ausentes, clave revocada, clave de otro juego (`403`), JWT de jugador rechazado, replay idempotente, nonce reutilizado (`409`), límite por jugador (`429`) y rate limiting con `Retry-After`. Tests unitarios del autenticador con `FakeTimeProvider` para los bordes de la ventana.
