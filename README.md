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

Create a GitHub OAuth App with these callback URLs:

```text
Local:      http://localhost:5200/api/auth/github/callback
Production: https://pullsight-backend.onrender.com/api/auth/github/callback
```

For local development, use user secrets or environment variables:

```bash
dotnet user-secrets set "GitHub:ClientId" "your-client-id"
dotnet user-secrets set "GitHub:ClientSecret" "your-client-secret"
dotnet user-secrets set "App:FrontendUrl" "http://127.0.0.1:5173"
```

## Production Environment Variables

Set these on the hosting platform:

```text
ASPNETCORE_ENVIRONMENT=Production
App__FrontendUrl=https://your-frontend-domain.vercel.app
GitHub__ClientId=your-client-id
GitHub__ClientSecret=your-client-secret
```

`App__FrontendUrl` is also included in the backend CORS allow-list, so one frontend domain only needs that one variable. Add extra allowed origins only when you intentionally support additional frontend domains:

```text
Cors__AllowedOrigins__1=https://your-custom-domain.com
```

## Docker

```bash
docker build -t pullsight-api .
docker run --rm -p 8080:8080 -e Cors__AllowedOrigins__0=http://localhost:5173 pullsight-api
```

## Deploy Notes

Use Docker deployment on Koyeb or Render. The app reads the platform-provided `PORT` environment variable and falls back to `8080` locally. The health check path is `/api/health`.

Current Render deployment:

```text
https://pullsight-backend.onrender.com
```
