# AGENTS.md

Backend-specific Codex context for PullSight.

## Scope

This repository contains the ASP.NET Core Web API backend for PullSight. It should remain deployable as a standalone backend repo.

## Standards

- Use ASP.NET Core Web API.
- Keep controllers thin.
- Put request/response contracts under `Contracts/`.
- Put domain/application services under `Services/`.
- Use analyzer abstractions so AI providers and rule-based fallback can be swapped.
- Prefer explicit DTOs/records for API responses.
- Add EF Core/Npgsql/Supabase integration behind services and configuration.

## Verification

Run before finishing backend changes:

```bash
dotnet build
```

Local URLs:

```text
http://localhost:5200/api/health
POST http://localhost:5200/api/reviews/demo
```

## Related Docs

In the local full workspace, see `../CONTEXT.md` and `../ARCHITECTURE.md`.
