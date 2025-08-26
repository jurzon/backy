# Commitment App Backend (Learning Project)

Stack: .NET 8 (Minimal APIs), EF Core, PostgreSQL, Docker, Swagger, (Blazor Server UI later).

## Prerequisites
- .NET 8 SDK
- Docker Desktop (running)
- (Optional) dotnet-ef tool: `dotnet tool install --global dotnet-ef` (already installed if you followed earlier steps)

## 1. Clone
```
git clone https://github.com/jurzon/backy.git
cd backy
```

## 2. Start Infrastructure (Postgres etc.)
```
docker compose up -d db
```
(Current compose also defines redis/minio/mailhog but only `db` needed now.)

## 3. Apply Database Migration
First build solution (ensures packages restored):
```
dotnet build Backy.sln
```
Apply migrations (creates schema in Postgres):
```
dotnet ef database update -p Commitments.Infrastructure/Commitments.Infrastructure.csproj -s Commitments.Api/Commitments.Api.csproj
```
(Uses connection string from `Commitments.Api/appsettings.json` -> Host=localhost; Database=commitments.)

## 4. Run the API
```
dotnet run --project Commitments.Api
```
Console output shows listening URLs (e.g. http://localhost:5000 or dynamic port). Default Kestrel dev ports if unspecified.

## 5. Health Check
```
curl http://localhost:5000/health
```
Response:
```
{"status":"ok","time":"2025-.."}
```

## 6. Swagger UI
Navigate in browser: `http://localhost:5000/swagger`
You should see endpoints:
- GET /health
- POST /commitments
- GET /commitments/{id}
- GET /commitments

## 7. Create a Commitment (Sample Request)
```
curl -X POST http://localhost:5000/commitments \
  -H "Content-Type: application/json" \
  -d '{
    "userId":"11111111-1111-1111-1111-111111111111",
    "goal":"Read 10 pages",
    "stakeAmount":25.5,
    "currency":"EUR",
    "deadlineUtc":"2025-12-31T18:00:00Z",
    "timezone":"Europe/Bratislava",
    "schedule":{
      "patternType":"daily",
      "interval":1,
      "weekdaysMask":null,
      "monthDay":null,
      "nthWeek":null,
      "nthWeekday":null,
      "startDate":"2025-08-26",
      "timeOfDay":"09:00:00"
    }
  }'
```
Response 201 body contains JSON summary with new `id`.

## 8. Fetch a Commitment
```
curl http://localhost:5000/commitments/{id}
```

## 9. List Commitments (User)
```
curl "http://localhost:5000/commitments?userId=11111111-1111-1111-1111-111111111111&page=1&pageSize=20"
```
Optional filter by status: `&status=Active`.

## 10. Run via Docker Image (API Only)
Build image:
```
docker build -t commitments-api -f Commitments.Api/Dockerfile .
```
Run (requires db container running):
```
docker run -p 8080:8080 --env ConnectionStrings__Postgres="Host=host.docker.internal;Username=commitments;Password=commitments;Database=commitments" commitments-api
```
Swagger at: http://localhost:8080/swagger

## 11. Environment Variables (Override Connection String)
`ConnectionStrings__Postgres` environment variable overrides appsettings. Example:
```
ConnectionStrings__Postgres=Host=localhost;Username=commitments;Password=commitments;Database=commitments
```

## 12. Troubleshooting
- ERROR: connection refused -> ensure `docker compose up -d db` and container healthy.
- 404 on /swagger -> ensure `ASPNETCORE_ENVIRONMENT=Development` (default when running `dotnet run`).
- Currency validation: must be 3-letter uppercase.
- Deadline must be > now + 1 hour.

## 13. Next Steps (Roadmap)
See `docs/backend_roadmap.md` for planned phases (recurrence jobs, check-ins, payments, notifications, Blazor UI).

## 14. License / Purpose
Educational / learning project for .NET 8 backend + domain design. Not production ready yet (no auth, limited validation, schedule algorithm placeholder).

---
Happy building!
