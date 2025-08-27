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
You should see endpoints (subset):
- GET /health
- POST /commitments
- GET /commitments/{id}
- GET /commitments
- POST /commitments/{id}/checkins
- POST /commitments/{id}/actions/{cancel|complete|fail|delete}
- POST /webhooks/stripe (test only)
- GET /payments/setup/{userId}?ensure={true|false}
- GET /notifications/quiet-hours/{userId}
- POST /notifications/quiet-hours/{userId}

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

## 12. Manual Blazor Admin UI Testing (Beginner Friendly)
This gives you a simple local dashboard to create & inspect commitments.

### 12.1 Start the backend
1. Ensure Postgres is running (`docker compose up -d db`).
2. Apply migrations (step 3 above) once.
3. Run API:
```
dotnet run --project Commitments.Api --urls http://localhost:5000
```
4. Keep this terminal open (logs & Hangfire dashboard info).

### 12.2 Start the Blazor Server UI
Open a new terminal:
```
dotnet run --project Commitments.Blazor --urls http://localhost:5100
```
Browse to http://localhost:5100

### 12.3 Understand the Dev User
Authentication is a basic dev-only Basic Auth in the API; the Blazor UI currently assumes a fixed user id:
```
11111111-1111-1111-1111-111111111111
```
All sample curl commands use this same id. In the future an auth phase will replace this.

### 12.4 Create Your First Commitment (via UI)
1. Click "Create" in left nav.
2. Enter a short Goal (e.g. "Daily Reading").
3. Leave defaults (daily schedule, 9:00, stake 5 EUR, deadline +7 days) and Submit.
4. Success banner shows a link – click to open detail page.

### 12.5 Explore Detail Page
You will see:
- Status (initial Active)
- Progress (simple time elapsed % placeholder)
- Risk badge (based on check-in adherence)
- Action buttons (Cancel / Complete / Fail / Delete)
- Recent Check-ins list (empty initially)

### 12.6 Add a Check-In
1. On detail page click "New Check-In".
2. Optionally add a note.
3. Save – list updates and progress / risk may change.

### 12.7 Trigger Risk Changes Quickly (Optional)
Create a commitment with a short deadline (e.g. now + 2 hours) and daily schedule; missing check-ins near deadline will show risk badges (AtRisk / Critical) automatically when you refresh.

### 12.8 Use Quiet Hours (Notifications Phase)
Quiet hours defer reminder sending. Set them (example: 22:00–07:00 UTC):
```
curl -X POST http://localhost:5000/notifications/quiet-hours/11111111-1111-1111-1111-111111111111 \
  -H "Authorization: Basic ZGV2OmRldg==" \
  -H "Content-Type: application/json" \
  -d '{"startHour":22,"endHour":7,"timezone":"UTC"}'
```
Fetch current quiet hours:
```
curl -H "Authorization: Basic ZGV2OmRldg==" \
  http://localhost:5000/notifications/quiet-hours/11111111-1111-1111-1111-111111111111
```
(Remember: Basic Auth header `dev:dev` is used by the API. The Blazor UI currently calls the API without auth for development.)

### 12.9 Payment Setup (Placeholder)
Invoke ensure setup intent (no real Stripe call unless you configure an API key):
```
curl -H "Authorization: Basic ZGV2OmRldg==" \
  http://localhost:5000/payments/setup/11111111-1111-1111-1111-111111111111?ensure=true
```

### 12.10 Hangfire Dashboard
When running in Development, visit:
```
http://localhost:5000/hangfire
```
You can see scheduled recurring jobs (reminder horizon, grace expiry, payment retry, reminder dispatch).

### 12.11 Common Issues
| Symptom | Fix |
|---------|-----|
| Empty list page | Ensure API is running & correct port (5000) |
| 404 /swagger | Not in Development environment |
| Validation error | Adjust goal length / deadline (>1h) |
| Quiet hours not deferring | Scheduled reminder may already be rescheduled; create a new reminder nearer current time |

## 13. Notification Endpoints (Summary)
| Endpoint | Method | Purpose |
|----------|--------|---------|
| /notifications/quiet-hours/{userId} | GET | Retrieve quiet hours config |
| /notifications/quiet-hours/{userId} | POST | Create/update quiet hours |

Body for POST:
```
{
  "startHour": 22,
  "endHour": 7,
  "timezone": "UTC"
}
```
Hours are integers 0–23; overnight windows (start > end) are supported.

## 14. Next Steps (Roadmap)
See `docs/backend_roadmap.md` for planned phases (recurrence jobs, check-ins, payments, notifications, Blazor UI).

## 15. License / Purpose
Educational / learning project for .NET 8 backend + domain design. Not production ready yet (no auth, limited validation, schedule algorithm placeholder).

---
Happy building!
