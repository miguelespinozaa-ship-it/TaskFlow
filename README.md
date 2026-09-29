# TaskFlow

> SaaS multi-tenant de gestión de proyectos. ASP.NET Core 10 + React + PostgreSQL.

**Estado:** Fase 2b — modelo completo (comentarios, etiquetas, asignaciones), CRUD, búsqueda full-text con paginación por cursor, soft delete y auditoría.

## ✨ Features

- **Multi-tenancy** (shared schema + `workspace_id`): el tenant sale del claim firmado del JWT y un global query
  filter de EF lo aplica a toda consulta. Un recurso de otro workspace responde **404, no 403**.
- **Auth** con ASP.NET Core Identity (PBKDF2, lockout tras 5 intentos) + JWT de 15 min.
- **Refresh tokens con rotación y detección de reuso**: presentar un token ya usado revoca toda la familia.
- **RBAC por workspace**: el mismo usuario puede ser Owner en uno y Viewer en otro.
- **Board con fractional indexing**: mover una tarea es un UPDATE de una fila (punto medio entre vecinas);
  cuando se agotan los decimales, la columna se renumera con un único UPDATE.
- **Búsqueda full-text en español** (columna `tsvector` generada + índice GIN) y **paginación por cursor**.
- **Soft delete** con filtro global y **auditoría automática** (tabla `activities` con diff en `jsonb`),
  ambos en `SaveChanges`: ningún caso de uso puede olvidarse de auditar.

## 🏗 Arquitectura

```
Api → Infrastructure → Application → Domain
```

- `TaskFlow.Domain` — entidades con invariantes (`TaskItem`, `Project`, `Workspace`, `WorkspaceMember`). Cero dependencias.
- `TaskFlow.Application` — casos de uso, DTOs, validación (FluentValidation), interfaces (`ITenantContext`, repositorios).
- `TaskFlow.Infrastructure` — EF Core + PostgreSQL, Identity, emisión de tokens, filtro global por tenant.
- `TaskFlow.Api` — controllers, policies de RBAC, `TenantResolutionMiddleware`, `ProblemDetails` (RFC 9457).

Los tests de arquitectura (NetArchTest) fallan si `Domain` o `Application` dependen de EF Core, ASP.NET o capas externas.

## 🚀 Cómo correrlo

Requisitos: .NET 10 SDK, Docker, Node 20+.

```bash
docker compose up -d postgres

dotnet tool restore
dotnet ef database update -p src/TaskFlow.Infrastructure -s src/TaskFlow.Api
dotnet run --project src/TaskFlow.Api          # http://localhost:5080 (crea datos de demo)

cd web/taskflow-web
npm install && npm run dev                     # http://localhost:5173
```

Usuarios de demo (workspace "Demo"), contraseña `Demo1234`:

| Email | Rol |
|---|---|
| `demo@taskflow.dev` | Owner |
| `member@taskflow.dev` | Member |
| `viewer@taskflow.dev` | Viewer (solo lectura) |

> El secreto JWT de `appsettings.Development.json` es solo para desarrollo. En producción se inyecta con la
> variable de entorno `Jwt__Secret`.

## 🔌 Endpoints

Todo requiere `Authorization: Bearer <token>` salvo lo marcado como público.

| Método | Ruta | Rol mínimo | Descripción |
|---|---|---|---|
| POST | `/api/v1/auth/register` | público | Crea usuario + workspace personal (transacción) |
| POST | `/api/v1/auth/login` | público | Access token + cookie httpOnly de refresh |
| POST | `/api/v1/auth/refresh` | cookie | Rota el refresh token |
| POST | `/api/v1/auth/logout` | cookie | Revoca la familia de tokens |
| POST | `/api/v1/auth/switch-workspace` | miembro | Tokens para otro workspace |
| GET | `/api/v1/auth/me` | — | Usuario + sus workspaces |
| GET / POST | `/api/v1/workspaces` | — | Mis workspaces / crear uno (quedo Owner) |
| GET | `/api/v1/workspaces/current/members` | Viewer | Miembros del workspace activo |
| POST | `/api/v1/workspaces/current/members` | Admin | Agregar miembro `{ email, role }` |
| GET / POST | `/api/v1/projects` | Viewer / Member | Proyectos (`?includeArchived=true`) |
| GET / PATCH | `/api/v1/projects/{id}` | Viewer / Member | Detalle / editar |
| POST | `/api/v1/projects/{id}/archive` · `/unarchive` | Admin | Archivar / desarchivar |
| DELETE | `/api/v1/projects/{id}` | Admin | Soft delete del proyecto y sus tareas |
| GET / POST | `/api/v1/projects/{id}/tasks` | Viewer / Member | Board del proyecto / crear tarea |
| GET | `/api/v1/tasks` | Viewer | Búsqueda: `projectId`, `status`, `assigneeId`, `labelId`, `search`, `cursor`, `pageSize` |
| GET / PATCH / DELETE | `/api/v1/tasks/{id}` | Viewer / Member / Member | Detalle / editar / soft delete |
| POST | `/api/v1/tasks/{id}/move` | Member | `{ status, afterTaskId? }` |
| POST | `/api/v1/tasks/{id}/assign` | Member | `{ assigneeId }` (debe ser miembro) |
| PUT | `/api/v1/tasks/{id}/labels` | Member | `{ labelIds }` |
| GET / POST | `/api/v1/tasks/{id}/comments` | Viewer / Member | Comentarios |
| PATCH / DELETE | `/api/v1/comments/{id}` | autor / autor o Admin | Editar / borrar |
| GET / POST | `/api/v1/labels` | Viewer / Member | Etiquetas del workspace |
| PATCH / DELETE | `/api/v1/labels/{id}` | Admin | Editar / borrar |
| GET | `/api/v1/activity` · `/api/v1/tasks/{id}/activity` | Viewer | Auditoría paginada por cursor |
| GET | `/health` | público | Health check |

## 🧪 Tests

```bash
dotnet test --solution TaskFlow.slnx   # unitarios + arquitectura + integración (Testcontainers: necesita Docker)
```

Incluye, entre otros: reuso de refresh token revoca la familia, refresh concurrente con el mismo token
(solo uno gana), token de A no ve datos de B (404), matriz de roles × permisos, token con claims válidos
pero firmado con otra clave → 401, paginación que no duplica filas si se inserta durante el recorrido,
50 inserciones en el mismo hueco del board (fuerza el rebalanceo), soft delete que conserva la fila.

## 📊 Decisiones técnicas

1. **UUID v7 como PK** — no enumerables (no exponen volumen de negocio) y ordenables por tiempo (no fragmentan el índice).
2. **Shared schema + `workspace_id`** con global query filter de EF. El filtro lee `CurrentWorkspaceId` del
   `DbContext` en cada query: si capturara el valor al construir el modelo, quedaría fijo para siempre porque EF cachea el modelo.
3. **El tenant sale solo del JWT**, nunca de un header, subdominio o del body: es lo único firmado.
4. **Refresh token en cookie httpOnly + `SameSite=Strict`**, access token solo en memoria del cliente: un XSS no
   puede leer ninguno de los dos desde JavaScript.
5. **Refresh tokens hasheados (SHA-256)** en la DB. Revocación atómica con `UPDATE ... WHERE revoked_at IS NULL`:
   dos refresh simultáneos con el mismo token no pueden ganar los dos.
6. **Índice único de email en la base**, no solo la validación de Identity en la app (que tiene condición de carrera).
7. **Secure by default**: `FallbackPolicy` exige autenticación en todo endpoint salvo `[AllowAnonymous]` explícito.
8. **Paginación por cursor `(created_at, id)`**, no OFFSET: coste constante en cualquier página, y no duplica ni
   salta filas si se inserta algo mientras el cliente pagina. Postgres la resuelve con un *Index Only Scan*.
9. **Filtros globales con nombre (EF Core 10)**: `Tenant` y `SoftDelete` por separado, para poder ver lo
   borrado sin desactivar el aislamiento entre tenants.
10. **Migraciones reales**, nunca `EnsureCreated`. **Tests de integración contra Postgres real** (Testcontainers).
11. **xUnit + Shouldly** en vez de FluentAssertions (licencia comercial desde v8).

## ⚠️ Limitaciones conocidas

- Un cambio de rol o una expulsión se aplica al siguiente refresh (máx. 15 min, lo que dura el access token).
  Fase 4: cache de permisos en Redis con invalidación.
- Dos pestañas que refrescan exactamente a la vez pueden disparar la detección de reuso y cerrar la sesión.
  El cliente evita el caso dentro de una pestaña (un solo refresh en vuelo).
- `PATCH` usa null como "no cambiar": para vaciar la descripción se envía `""` y para quitar el vencimiento `clearDueAt: true`.
- Agregar miembros requiere que el usuario ya esté registrado; las invitaciones por email llegan con los background jobs.
