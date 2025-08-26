# Backend Implementation Roadmap (Harmonogram)
Target Stack: .NET 8, ASP.NET Core (Minimal APIs), EF Core, PostgreSQL, Redis, Hangfire (background jobs), Stripe SDK, Docker / Compose.
Focus: Deliver functional API + Blazor Server admin/testing UI first.
Time Horizon Example: 10 weeks (adjustable). Each week ends with a demoable increment.

## Phase 0 – Environment & Project Bootstrap (Week 1)
Goals:
- Tooling: .NET 8 SDK, Docker Desktop
- Create solution structure
- Docker Compose with Postgres, Redis, Mailhog (dev email), MinIO (mock object storage)
- Baseline API project + Blazor Server project + Shared library
Deliverables:
- /src/Commitments.Api
- /src/Commitments.Blazor
- /src/Commitments.Domain (entities + value objects)
- /src/Commitments.Infrastructure (EF Core DbContext, migrations)
- Health endpoint, swagger, basic DI wiring.

## Phase 1 – Core Domain & Persistence (Week 2)
Goals:
- Implement entities per spec (Commitment, Schedule, CheckIn, PaymentIntentLog, AuditLog, ReminderEvent)
- EF Core mappings, constraints, indexes
- Repository or pure DbContext usage policy
- Migration 001
Deliverables:
- Domain models + validation methods
- DbContext + initial migration applied via Compose startup
Tests:
- Unit tests for entities invariants

## Phase 2 – Creation & Retrieval APIs (Week 3)
Goals:
- Endpoints: Create Commitment, Get Commitment, List Commitments (filters: status, date range)
- Schedule validation & first occurrence preview logic
- Value conversions (money to minor units)
- Central error handling & problem details
Deliverables:
- POST /commitments
- GET /commitments/{id}
- GET /commitments
Tests:
- Request validation, schedule generation edge cases

## Phase 3 – Recurrence & Reminder Generation (Week 4)
Goals:
- Implement recurrence expansion service
- Background job: Reminder horizon builder
- Store ReminderEvents
- Risk badge computation helper
Deliverables:
- Hangfire setup + dashboard (dev only)
- Service: IReminderScheduler
Tests:
- Recurrence unit tests (daily/weekly/monthly, DST)

## Phase 4 – Check-ins & Progress (Week 5)
Goals:
- Endpoint: POST /commitments/{id}/checkins
- Progress & risk fields in GET
- Photo upload pre-signed URL generation (MinIO dev)
Deliverables:
- Object storage abstraction
Tests:
- Check-in creation, risk badge scenario

## Phase 5 – Deadline / Grace Handling (Week 6)
Goals:
- Transition to DecisionNeeded at deadline
- Grace expiry scanner job
- Snooze logic (reschedule ReminderEvent)
Deliverables:
- Background job #2: GraceExpiryScanner
Tests:
- Simulated time advancement integration tests

## Phase 6 – Status Changes & Cancellation / Deletion (Week 7)
Goals:
- Endpoints: Cancel, Complete (decision), Fail (manual), Delete
- State machine guard service
- Audit logging of transitions
Deliverables:
- POST /commitments/{id}/actions/{cancel|complete|fail|delete}
Tests:
- Invalid transitions rejection

## Phase 7 – Payments Integration (Week 8)
Goals:
- Stripe SetupIntent on creation (if missing PM)
- PaymentIntent creation on fail / auto-fail
- Webhook endpoint & signature validation
- Retry scheduler job
Deliverables:
- POST /webhooks/stripe
- PaymentRetryWorker
Tests:
- Mock webhook events, idempotency keys

## Phase 8 – Notifications (Week 9)
Goals:
- Notification dispatch abstraction (in-memory + console for dev)
- Quiet hours logic
- Email/push stubs
Deliverables:
- NotificationDispatcher job wiring ReminderEvents -> console/email
Tests:
- Quiet hours suppression tests

## Phase 9 – Blazor Server Admin/Test UI (Week 10)
Goals:
- Pages: Dashboard list, Commitment detail, Create form, Check-in modal, Decision modal
- Shared localization resources (EN/SK stub)
- Auth (temporary developer login)
Deliverables:
- Functional testing surface for API
Tests:
- Playwright minimal smoke (optional)

## Continuous
- CI pipeline: build, test, lint, docker image
- Static analysis (Nullable enabled, analyzers)
- Observability: Serilog + OpenTelemetry (later)

## Stretch (Post v1)
- Real notification providers
- Photo virus scanning hook
- Multi-tenancy

---
# Weekly Checkpoints Summary Table
Week | Theme | Key Endpoints / Features | Demo
1 | Bootstrap | Health, Swagger, Compose up | Project runs
2 | Domain/Persistence | Entities + Migration | DB schema
3 | Creation/Retrieval | CRUD base endpoints | Create/list commitment
4 | Recurrence | Reminder generation job | Horizon table populates
5 | Check-ins | Check-in endpoint + progress | Risk calc visible
6 | Grace | DecisionNeeded transitions | Auto-fail demo
7 | Status & Audit | Cancel/Fail/Delete actions | Audit log entries
8 | Payments | Stripe fail charge + webhook | Simulated payment
9 | Notifications | Reminder dispatch (console) | Console/email outputs
10| Blazor UI | Web UI flows | Full user flow local

---
# Issue / Task Template (Use per feature)
Title: <Feature>
Description: Rationale + acceptance.
Acceptance Criteria:
- [ ] ...
Out of Scope:
Technical Notes:
Test Cases:
Risks:

---
# Definition of Done
- Code + tests merged
- Swagger updated
- Migration added (if DB change)
- Docs updated
- No critical analyzer warnings

---
# Risk Mitigation
Risk | Mitigation
Recurrence bugs | Exhaustive unit tests with fixed seeds
Payment idempotency | Deterministic keys, unique constraint
Data loss | Daily volume snapshot (Postgres), migration backups
Schedule drift | UTC storage + explicit TZ conversions

---
# Metrics (Later)
- Commitments created / day
- % Completed vs Failed
- Avg time to decision
- Payment success rate first attempt

---
End.
