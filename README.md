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

Production health check:

```text
GET https://pullsight-backend.onrender.com/api/health
```

## Production Environment Variables

Set these on the hosting platform:

```text
ASPNETCORE_ENVIRONMENT=Production
Cors__AllowedOrigins__0=https://your-frontend-domain.vercel.app
```

Add more origins by incrementing the index:

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
