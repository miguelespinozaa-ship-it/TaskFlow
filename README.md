# TaskFlow

> SaaS multi-tenant de gestión de proyectos. ASP.NET Core 10 + React + PostgreSQL.

**Estado:** Fase 1 — esqueleto Clean Architecture + vertical slice (crear tarea y verla en la UI).

## 🏗 Arquitectura

```
Api → Infrastructure → Application → Domain
```

- `TaskFlow.Domain` — entidades con invariantes (`TaskItem`, `Project`, `Workspace`). Cero dependencias.
- `TaskFlow.Application` — casos de uso, DTOs, validación (FluentValidation), interfaces de repositorio.
- `TaskFlow.Infrastructure` — EF Core + PostgreSQL, global query filter por tenant, repositorios.
- `TaskFlow.Api` — controllers, `ProblemDetails` (RFC 9457), CORS explícito.

Los tests de arquitectura (NetArchTest) fallan si `Domain` o `Application` dependen de EF Core o de capas externas.

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

## 🔌 Endpoints (Fase 1)

| Método | Ruta | Descripción |
|---|---|---|
| GET | `/api/v1/projects` | Lista proyectos |
| GET | `/api/v1/projects/{id}/tasks` | Lista tareas del proyecto |
| POST | `/api/v1/projects/{id}/tasks` | Crea una tarea `{ title, description?, priority }` |
| GET | `/health` | Health check |

## 🧪 Tests

```bash
dotnet test   # unitarios + arquitectura + integración (Testcontainers: necesita Docker)
```

## 📊 Decisiones técnicas

1. **UUID v7 como PK** — no enumerables (no exponen volumen de negocio) y ordenables por tiempo (no fragmentan el índice).
2. **Shared schema + `workspace_id`** con global query filter de EF. El filtro lee `CurrentWorkspaceId` del
   `DbContext` en cada query: si capturara el valor al construir el modelo, quedaría fijo para siempre porque EF cachea el modelo.
3. **`workspace_id` denormalizado en `tasks`** y heredado del proyecto en el dominio: una tarea no puede quedar en otro tenant.
4. **Migraciones reales**, nunca `EnsureCreated`.
5. **Tests de integración contra Postgres real** (Testcontainers), no EF InMemory.
6. **xUnit + Shouldly** en vez de FluentAssertions (licencia comercial desde v8).
