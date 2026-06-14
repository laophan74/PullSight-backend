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
GitHub__CallbackUrl=https://pullsight-backend.onrender.com/api/auth/github/callback
ConnectionStrings__DefaultConnection=...
```

`App__FrontendUrl` is the source of truth for the primary frontend origin and is included in CORS automatically. `Cors__AllowedOrigins__*` is only for extra frontend origins. Never commit OAuth secrets; use Render env vars or local `dotnet user-secrets`.

Supabase Postgres is connected through EF Core + Npgsql. The backend accepts Supabase URI-style connection strings and normalizes them in `Program.cs`. EF Core migrations are committed under `Migrations/`. Startup migrations are opt-in with `Database__MigrateOnStartup=true`; keep this false on Render unless explicitly needed. Render should use the transaction pooler host `aws-1-ap-southeast-1.pooler.supabase.com:6543` with username `postgres.syshrmyuimqoitbpijea`.

Review History is implemented through `GET /api/reviews` and `GET /api/reviews/{reviewRunId}`. Keep both endpoints cookie-authenticated and ownership-scoped through `ReviewHistoryService`.

Compare Reviews is implemented through `POST /api/reviews/compare`. Keep ownership, same-PR validation, queries, and finding classification in `ReviewComparisonService`. Matching must use stable finding content identity rather than database IDs.

Review History filters run through `ReviewHistoryService` before pagination. Saved review/comparison exports use `ReviewReportService`. GitHub PR publishing uses `ReviewPublishService` and `Services/GitHub/GitHubCommentService`; repository and PR context must come from persisted owned runs. Stable hidden markers provide idempotent comment updates without a migration.

Review runs use `queued`, `analyzing`, `completed`, `fallback`, and `failed`. Keep lifecycle writes in persistence/orchestration services and sanitize failed errors. Structured summaries use overview, risk overview, key changes, and suggested test plan.

Check Run and inline publishing use `ReviewGitHubPublishService`. Only completed/fallback owned runs are eligible. Check Runs use `external_id = pullsight:{reviewRunId}`. Inline comments require the persisted head to remain current and the finding line to be an added right-side diff line; markers use stable content identity rather than finding database IDs.

Migration `AddReviewLifecycleSummary` adds only nullable summary JSON and error columns. Do not enable Render startup migrations.

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
dotnet build -c Release
dotnet test -c Release
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
