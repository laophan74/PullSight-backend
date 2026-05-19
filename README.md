# PullSight Backend

ASP.NET Core Web API for PullSight.

Production URL:

```text
https://pullsight-backend.onrender.com
```

## Local Development

```bash
dotnet restore
dotnet run --urls http://localhost:5200
```

Health check:

```text
GET http://localhost:5200/api/health
GET http://localhost:5200/api/health/db
```

Demo review endpoint:

```text
POST http://localhost:5200/api/reviews/demo
```

GitHub auth endpoints:

```text
GET  http://localhost:5200/api/auth/github/login
GET  http://localhost:5200/api/auth/github/callback
GET  http://localhost:5200/api/auth/me
POST http://localhost:5200/api/auth/logout
```

Production health check:

```text
GET https://pullsight-backend.onrender.com/api/health
```

## GitHub OAuth

GitHub OAuth login is implemented with an HTTP-only cookie session. The backend redirects users to GitHub, handles the callback, stores the GitHub access token in protected authentication properties, and exposes the current user through `GET /api/auth/me`.

Create a GitHub OAuth App with these URLs:

```text
Homepage URL: https://pull-sight.vercel.app
Local callback: http://localhost:5200/api/auth/github/callback
Production callback: https://pullsight-backend.onrender.com/api/auth/github/callback
```

For local development, use user secrets or environment variables:

```bash
dotnet user-secrets set "GitHub:ClientId" "your-client-id"
dotnet user-secrets set "GitHub:ClientSecret" "your-client-secret"
dotnet user-secrets set "App:FrontendUrl" "http://127.0.0.1:5173"
```

## Supabase Postgres

The backend uses EF Core + Npgsql with Supabase Postgres. Set the database connection string outside source control:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "your-supabase-postgres-connection-string"
```

Render environment variable:

```text
ConnectionStrings__DefaultConnection=your-supabase-postgres-connection-string
```

Supabase URI-style strings like `postgresql://...` are supported; the backend normalizes them for Npgsql in `Program.cs`.

Database schema is managed through EF Core migrations:

```bash
dotnet ef migrations add MigrationName
dotnet ef database update
```

The app also applies pending migrations on startup, so Render deploys pick up committed migrations automatically.

## Production Environment Variables

Set these on the hosting platform:

```text
ASPNETCORE_ENVIRONMENT=Production
App__FrontendUrl=https://pull-sight.vercel.app
GitHub__ClientId=your-client-id
GitHub__ClientSecret=your-client-secret
ConnectionStrings__DefaultConnection=your-supabase-postgres-connection-string
```

`App__FrontendUrl` is also included in the backend CORS allow-list, so one frontend domain only needs that one variable. Add extra allowed origins only when you intentionally support additional frontend domains:

```text
Cors__AllowedOrigins__1=https://your-custom-domain.com
```

## Docker

```bash
docker build -t pullsight-api .
docker run --rm -p 8080:8080 -e App__FrontendUrl=http://localhost:5173 pullsight-api
```

## Deploy Notes

Use Docker deployment on Koyeb or Render. The app reads the platform-provided `PORT` environment variable. The health check path is `/api/health`; the database health check path is `/api/health/db`.

Current Render deployment:

```text
https://pullsight-backend.onrender.com
```
