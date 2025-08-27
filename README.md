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

## 12. Manual Blazor Admin UI Testing
The server?side Blazor admin scaffold (Phase 9) provides a basic list/detail/create flow.

1. Start API (port assumed 5000):
```
dotnet run --project Commitments.Api --urls http://localhost:5000
```
2. Start Blazor UI (separate terminal):
```
# Option A: use API default (configure ApiBase)
set ApiBase=http://localhost:5000/
# PowerShell: $env:ApiBase="http://localhost:5000/"
# then run
dotnet run --project Commitments.Blazor --urls http://localhost:5100
```
3. Open browser: http://localhost:5100
4. Navigate with left nav:
   - Home
   - Commitments (lists commitments for hardcoded dev UserId `11111111-1111-1111-1111-111111111111`)
   - Create (submit form, then follow success link to detail)
5. Detail page shows summary fields (status / progress placeholders).
6. To test API errors (e.g. validation) try blank Goal or deadline < 1h ahead; UI shows raw error JSON.
7. Hot reload: edit a .razor file; .NET dev server applies changes automatically.

Note: Authentication not implemented yet; a fixed dev userId is used. Update later when auth phase added.

## 13. Troubleshooting
- ERROR: connection refused -> ensure `docker compose up -d db` and container healthy.
- 404 on /swagger -> ensure `ASPNETCORE_ENVIRONMENT=Development` (default when running `dotnet run`).
- Currency validation: must be 3-letter uppercase.
- Deadline must be > now + 1 hour.
- Blazor UI not showing data -> verify ApiBase env var matches API URL and CORS not required (same origin dev HTTP).

## 14. Next Steps (Roadmap)
See `docs/backend_roadmap.md` for planned phases (recurrence jobs, check-ins, payments, notifications, Blazor UI).

## 15. License / Purpose
Educational / learning project for .NET 8 backend + domain design. Not production ready yet (no auth, limited validation, schedule algorithm placeholder).

---
Happy building!
