# AGENTS.md

Backend-specific Codex context for PullSight.

## Scope

This repository contains the ASP.NET Core Web API backend for PullSight. It should remain deployable as a standalone backend repo.

Production backend URL:

```text
https://pullsight-backend.onrender.com
```

Production frontend URL:

```text
https://pull-sight.vercel.app
```

GitHub OAuth login is implemented. Required production environment variables:

```text
ASPNETCORE_ENVIRONMENT=Production
App__FrontendUrl=https://pull-sight.vercel.app
GitHub__ClientId=...
GitHub__ClientSecret=...
ConnectionStrings__DefaultConnection=...
```

`App__FrontendUrl` is the source of truth for the primary frontend origin and is included in CORS automatically. `Cors__AllowedOrigins__*` is only for extra frontend origins. Never commit OAuth secrets; use Render env vars or local `dotnet user-secrets`.

Supabase Postgres is connected through EF Core + Npgsql. The backend accepts Supabase URI-style connection strings and normalizes them in `Program.cs`. EF Core migrations are committed under `Migrations/` and are applied at startup.

## Standards

- Use ASP.NET Core Web API.
- Keep controllers thin.
- Put request/response contracts under `Contracts/`.
- Put domain/application services under `Services/`.
- Use analyzer abstractions so AI providers and rule-based fallback can be swapped.
- Keep GitHub OAuth/provider logic under `Services/GitHub`.
- Prefer explicit DTOs/records for API responses.
- Add EF Core/Npgsql/Supabase integration behind services and configuration.
- Keep database entities under `Data/Entities` and the DbContext in `Data/PullSightDbContext.cs`.

## Verification

Run before finishing backend changes:

```bash
dotnet build
```

Local URLs:

```text
http://localhost:5200/api/health
GET http://localhost:5200/api/health/db
POST http://localhost:5200/api/reviews/demo
GET http://localhost:5200/api/auth/me
```

Production URLs:

```text
https://pullsight-backend.onrender.com/api/health
GET https://pullsight-backend.onrender.com/api/health/db
POST https://pullsight-backend.onrender.com/api/reviews/demo
GET https://pullsight-backend.onrender.com/api/auth/me
```

## Related Docs

In the local full workspace, see `../CONTEXT.md` and `../ARCHITECTURE.md`.
