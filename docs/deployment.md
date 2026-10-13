# Despliegue gratuito (Render + Neon)

> **Documento:** `docs/deployment.md` · **Estado:** Vigente · **Versión:** 1.0  
> **Relacionados:** [Arquitectura §5.6](architecture.md#56-gestion-de-secretos) · [README](../README.md)

Guía para publicar una **demo pública** de la API, con HTTPS y Swagger, sin coste y sin tarjeta:

| Pieza | Servicio | Plan gratuito |
|---|---|---|
| API (contenedor Docker) | [Render](https://render.com) · *Web Service* | Gratis; se duerme tras ~15 min sin tráfico y tarda 30–60 s en despertar |
| PostgreSQL | [Neon](https://neon.tech) | Gratis sin caducidad; se suspende sin uso y despierta en ~1 s |

> Los planes gratuitos cambian con frecuencia: confirma las condiciones actuales en la web de cada proveedor.

```mermaid
flowchart LR
    U["🌐 Navegador / cliente"] -- HTTPS --> R["Render<br/>proxy TLS"]
    R -- "HTTP :8080<br/>X-Forwarded-For" --> A["leaderboard-api<br/>(Dockerfile del repo)"]
    A -- "TLS · sslmode=require" --> N[("Neon<br/>PostgreSQL")]
    GH["GitHub main"] -- "auto-deploy" --> R
```

## 1. Base de datos en Neon (≈ 3 min)

1. Entra en [neon.tech](https://neon.tech) con tu cuenta de GitHub y crea un proyecto:
   - **Postgres version:** la más reciente disponible (17 o superior).
   - **Region:** *AWS Europe Central (Frankfurt)*, la misma región que usa `render.yaml`, para que la latencia sea mínima.
2. En **Connect**, desactiva *Connection pooling* (las migraciones de EF Core van mejor con la conexión directa) y copia la cadena de conexión. Tiene esta forma:

   ```text
   postgresql://neondb_owner:********@ep-xxxx-xxxx.eu-central-1.aws.neon.tech/neondb?sslmode=require
   ```

   Puedes pegarla tal cual: la API acepta tanto URIs `postgresql://…` como el formato de Npgsql (`Host=…;Database=…`).

No hace falta crear tablas: la API aplica las migraciones al arrancar (`Database__MigrateOnStartup=true`) e instala la extensión `citext`.

## 2. API en Render (≈ 2 min + build)

[![Deploy to Render](https://render.com/images/deploy-to-render-button.svg)](https://render.com/deploy?repo=https://github.com/alba-alonso-dev/leaderboard-api)

1. Pulsa el botón (o en Render: **New → Blueprint** y elige el repositorio). Render lee [`render.yaml`](../render.yaml).
2. Rellena las tres variables marcadas como secretas:

   | Variable | Valor |
   |---|---|
   | `ConnectionStrings__Postgres` | La cadena de Neon del paso 1 |
   | `Seed__Admin__Email` | Email del administrador de la demo |
   | `Seed__Admin__Password` | Contraseña robusta para ese administrador |

   Render **genera automáticamente** `Jwt__SigningKey` y `DataProtection__KeyEncryptionKey` (256 bits aleatorios).
3. **Apply**. El primer build tarda unos minutos. Cuando el servicio esté *Live*:
   - `https://<tu-servicio>.onrender.com/health/ready` → `{"status":"Healthy", …}`
   - `https://<tu-servicio>.onrender.com/swagger` → Swagger UI

Cada push a `main` vuelve a desplegar automáticamente (`autoDeploy: true`).

### Comprobación de extremo a extremo

```bash
BASE_URL=https://<tu-servicio>.onrender.com ./scripts/smoke-test.sh
```

## 3. Qué hace `render.yaml`

| Variable | Valor | Por qué |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Sin detalles de errores en las respuestas, HSTS activado |
| `PORT` | `8080` | Puerto al que Render envía el tráfico (el de la imagen) |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | `true` | Detrás del proxy de Render, la IP del cliente llega en `X-Forwarded-For`. Sin esto, **todos** los clientes compartirían el mismo límite de peticiones |
| `Jwt__SigningKey` | generada | Clave HS256 de los tokens |
| `DataProtection__KeyEncryptionKey` | generada | Cifra con AES-256-GCM el *key ring* de Data Protection (ver abajo) |
| `Database__MigrateOnStartup` | `true` | Crea y actualiza el esquema al arrancar |
| `Swagger__Enabled` | `true` | Swagger visible en Production para la demo |
| `Serilog__Format` | `json` | Logs estructurados en el panel de Render |

### Claves de Data Protection sin disco persistente

Los secretos de las API Keys se guardan cifrados con ASP.NET Core Data Protection. Sus claves (*key ring*) deben sobrevivir a los reinicios, o las API Keys emitidas dejarían de funcionar:

| Entorno | Dónde vive el *key ring* | Protección |
|---|---|---|
| Docker Compose | Volumen `dp-keys` (`DataProtection__KeysPath=/app/keys`) | Fuera de la base de datos |
| Render (sin disco) | Tabla `data_protection_keys` en PostgreSQL | Cifrado con `DataProtection__KeyEncryptionKey` (AES-256-GCM): una filtración de la base de datos sola no expone los secretos |

En `Production`, si el *key ring* va a la base de datos y no hay `KeyEncryptionKey`, **la API se niega a arrancar** en lugar de guardarlo en claro.

## 4. Límites de la demo gratuita

- **Arranque en frío:** la primera petición tras 15 min sin tráfico tarda 30–60 s. Abre `/health/ready` un minuto antes de enseñar la demo.
- **Una sola instancia:** el rate limiting es en memoria (suficiente para una demo; ver [ADR-004](adr/0004-future-improvements.md) para varias réplicas).
- **Migraciones al arrancar:** cómodo para una demo; en un entorno real se ejecutarían como paso previo del pipeline (*migration bundle*).
- **Registro abierto:** cualquiera puede registrarse y crear juegos. Los límites de peticiones por IP protegen la demo de abusos básicos.

## 5. Problemas frecuentes

| Síntoma en los logs de Render | Causa | Solución |
|---|---|---|
| `Connection string 'Postgres' is not configured` | Falta `ConnectionStrings__Postgres` | Añádela en *Environment* y vuelve a desplegar |
| `DataProtection:KeyEncryptionKey must be a base64-encoded 256-bit key` | Valor manual incorrecto | Usa `openssl rand -base64 32` o deja que Render lo genere |
| `The key ring is encrypted but DataProtection:KeyEncryptionKey is not configured` | Se borró o cambió la clave | Restaura el valor anterior. Si se perdió, revoca y vuelve a emitir las API Keys |
| Timeout o `SSL` al conectar | Cadena sin `sslmode=require` o región lejana | Copia de nuevo la cadena desde Neon |
| `429 Too Many Requests` para todos | Falta `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` | Revisa las variables del servicio |
