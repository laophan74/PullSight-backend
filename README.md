# PullSight Backend

ASP.NET Core Web API for PullSight.

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

## Production Environment Variables

Set these on the hosting platform:

```text
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://+:8080
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

Use Docker deployment on Koyeb or Render. The container listens on port `8080`, and the health check path is `/api/health`.
