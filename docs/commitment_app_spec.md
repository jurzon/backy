# Commitment App – Product & Technical Specification (Consolidated v5)

Incorporates user-approved clarifications and decisions.

## Chosen Decisions Summary
1. Terminology: "Reminder" (replaces prior misspelling)
2. Statuses: Active, DecisionNeeded (grace window), Completed, Failed, Cancelled, Deleted
3. Cancellation vs Deletion: Both kept. Cancelled = user aborts before deadline (no charge). Deleted = soft-hidden (logical delete) after any terminal state; retains audit.
4. Editing Lock: Within 24h of deadline all fields locked except goal description minor text edits (notes). No schedule/stake/deadline edits inside lock window.
5. Progress Bar: Time elapsed / total time (creation ? deadline) capped at 100%.
6. Risk Badge Logic: "At Risk" if (a) <25% of scheduled check-ins completed after 50% of time elapsed OR (b) time remaining <48h while still Active. During grace -> badge becomes "Decision Needed".
7. Monthly Day Clamp: Day-of-month clamped to last valid day (e.g. 31 -> Feb 29/28).
8. Monthly Nth Weekday Rule: Forward-only; first occurrence is first matching date on/after start date.
9. Timezone Change Effect: Occurrences keep absolute UTC timestamp; local wall-clock time may shift across DST/timezone changes.
10. Deadline Outcome Snooze: Multiple 15-minute snoozes allowed until grace expires.
11. Max Stake: No hard max (backend can optionally enforce business risk limits later). Minimum > 0.
12. Payment Retry: Daily retry up to 5 days for failed payment intents (excluding hard declines) with escalating notifications.
13. Late Check-ins: Allowed anytime until commitment deadline (no concept of "missed" for scoring, but risk algorithm uses completion ratio vs elapsed time).
14. Currencies Supported: EUR, USD, CHF, PLN, CZK, HUF (stored as ISO 4217 + minor units integer). Default currency from user profile.
15. Internationalization: All user-facing strings replaced by i18n keys (examples shown). EN + SK initially; extensible.

---
## 1. Domain Model & State Machine

### Entities
- Commitment
- Schedule (recurrence definition)
- CheckIn
- ReminderEvent (scheduled notification/check-in prompts)
- PaymentIntentLog
- AuditLog
- UserPaymentMethod (tokenized via Stripe)

### Commitment Fields (proposed)
```
Commitment {
  id (UUID)
  user_id (UUID)
  goal (string <=200)
  stake_amount_minor (int)  // minor units (e.g. cents)
  currency (ISO 4217)
  deadline_utc (timestamp)
  timezone (IANA TZ string) // reference for display; scheduling uses stored UTC instants
  status (enum: Active|DecisionNeeded|Completed|Failed|Cancelled|Deleted)
  grace_expires_utc (timestamp nullable)
  created_at_utc, updated_at_utc, cancelled_at_utc, completed_at_utc,
  failed_at_utc, deleted_at_utc
  editing_locked_at_utc (timestamp) // = deadline_utc - 24h
  progress_note_editable_until_utc (same as deadline)
}
```

### Schedule Fields
```
Schedule {
  commitment_id
  pattern_type (Daily|Weekly|Monthly)
  interval (int >=1)
  weekdays_mask (bitmask Mon=1 ...) // Weekly
  month_day (1-31 nullable)         // Monthly day-of-month variant
  nth_week (1-5 nullable)           // Monthly nth-weekday variant (5 = last)
  nth_weekday (0-6 nullable)        // 0=Mon
  start_date (date in commitment timezone)
  time_of_day (HH:MM) // local time component originally selected
  timezone (IANA) // original selection
  next_occurrence_utc (timestamp)
  created_at_utc, updated_at_utc
}
```
Monthly: Only one of (month_day) or (nth_week + nth_weekday) populated.

### CheckIn Fields
```
CheckIn {
  id
  commitment_id
  occurred_at_utc
  note (nullable)
  photo_url (nullable)
  created_at_utc
}
```

### PaymentIntentLog
```
PaymentIntentLog {
  id
  commitment_id
  stripe_payment_intent_id
  amount_minor
  currency
  status (created|requires_action|succeeded|failed|cancelled)
  last_error_code (nullable)
  created_at_utc, updated_at_utc
  attempt_number (int)
}
```

### AuditLog
```
AuditLog {
  id
  commitment_id (nullable for global events)
  user_id
  event_type (enum)
  data_json
  created_at_utc
  correlation_id (UUID) // tie multi-step processes
}
```

### Status Transitions
```
Active -> DecisionNeeded (at deadline)
DecisionNeeded -> Completed (user action)
DecisionNeeded -> Failed (user action or grace expiry auto-fail)
Active -> Cancelled (user action if NOT within 24h lock window)
Any non-Deleted terminal (Completed|Failed|Cancelled) -> Deleted (user soft hide)
Active -> Deleted (allowed? NO; must Cancel first)
Cancelled/Completed/Failed -> (remain) // no reactivation
```
Invalid transitions are rejected (400).

### Grace Logic
- When deadline_utc reached: if status=Active set status=DecisionNeeded, set grace_expires_utc=deadline_utc + grace_window (default 60 min). Create notification and optionally schedule 15-min-before-grace-end notification.
- At grace_expires_utc: if still DecisionNeeded -> auto fail (status=Failed) and create PaymentIntent.

---
## 2. Recurrence & Reminder Expansion

### Recurrence Rules
Represent pattern similar to simplified RFC 5545:
```
RRULE: FREQ=DAILY;INTERVAL=1
RRULE: FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR
RRULE: FREQ=MONTHLY;INTERVAL=1;BYMONTHDAY=31 (clamped)
RRULE: FREQ=MONTHLY;INTERVAL=1;BYSETPOS=1;BYDAY=MO (1st Monday)
```
Internal canonicalization stored in Schedule plus derived next_occurrence_utc.

### Generation Algorithm (Pseudo)
```
function nextOccurrence(afterExclusiveUtc):
  t = last_occurrence_local (or start_date + time_of_day) in schedule.timezone
  do:
     t = advance(t)
     if pattern_type=Monthly & month_day set:
        desired_day = min(month_day, daysInMonth(t.year, t.month))
        t = date(t.year, t.month, desired_day) at time_of_day
     if pattern_type=Monthly & nth_week set:
        t = nthWeekdayOfMonth(t.year, t.month, nth_week, nth_weekday)
  while toUTC(t) <= afterExclusiveUtc or toUTC(t) > commitment.deadline_utc
  return toUTC(t) or null if past deadline
```
Advance uses interval units; for weekly iterate days until a valid weekday bit set.

### Timezone Changes
- If user changes commitment timezone later: future UTC instants remain (absolute). We store original timezone for display; editing schedule triggers recalculation with new timezone baseline only if schedule edited.

### Editing Schedule
- Allowed only outside 24h lock window.
- On edit: drop all future ReminderEvents after now and regenerate from (now) boundary.

### ReminderEvents
- Stored for next N (e.g. 30) days or up to deadline; rolling background job ensures upcoming horizon (idempotent). Alternatively compute just-in-time; we choose materialized for push scheduling reliability.

---
## 3. Progress & Risk

Progress Bar = (now - created_at) / (deadline - created_at). Clamp [0,1]. If status terminal use deadline for denominator (no post-deadline growth).

Check-in Completion Ratio = completed_checkins / expected_checkins_elapsed.
Expected_checkins_elapsed counts scheduled occurrences whose UTC time <= now.

Risk Badge Determination:
- If status=DecisionNeeded -> badge: i18n.key:badge.decision_needed
- Else if (time_remaining < 48h) OR (time_elapsed_ratio > 0.5 AND checkin_completion_ratio < 0.25) -> i18n.key:badge.at_risk
- Else -> i18n.key:badge.on_track

---
## 4. UI Screens (Updated)

All previous screen descriptions retained; updated terminology and badges.

### Dashboard
Add: Badge states: On Track / At Risk / Decision Needed.

### Creation Screen
"Reminder & Check-in Schedule" section (label i18n.key:commitment.schedule.section_title) showing preview next 3 occurrences (UTC converted to user timezone for display).

Validation Additions:
- deadline must be at least 1 hour > now (configurable) else error i18n.key:error.deadline.too_soon
- schedule must produce >=1 occurrence strictly before deadline.
- stake > 0; decimal entry converted to minor units; currency allowed set {EUR, USD, CHF, PLN, CZK, HUF}.

### Details Screen
Show: status pill; if within 24h lock window show lock icon with tooltip i18n.key:tooltip.edit_locked.
Actions availability matrix:
- Edit (fields) hidden/disabled inside lock window.
- Cancel allowed only if status=Active and now < editing_locked_at_utc.
- Delete allowed if status in (Cancelled, Completed, Failed) or status=Active but only after cancellation (two-step).

### Check-in Modal
Title i18n.key:checkin.title (param {goal}). Accept any time pre-deadline. If status not Active (DecisionNeeded or terminal) modal blocked (toast i18n.key:error.checkin.not_allowed).

### Deadline Outcome Modal
Appears when status=DecisionNeeded. Snooze button re-schedules a reminder 15 mins later if now + 15m < grace_expires_utc; unlimited repeats until boundary.

---
## 5. Cancellation & Deletion Semantics
- Cancelled: User explicitly ends commitment early (no charge). Requires confirmation and reason (optional text logged). Status becomes Cancelled; cannot be resumed.
- Deleted: Soft-hidden from standard lists; can still appear under filters (e.g., "Show Deleted"). Only allowed after (Cancelled|Completed|Failed). Delete sets status=Deleted and deleted_at_utc. Audit retains prior status.

---
## 6. Payments

### Flow
1. On creation ensure a valid default payment method (Stripe SetupIntent if none exists).
2. On user-triggered failure (check-in fail button or decision modal) OR auto-fail after grace:
   - Create PaymentIntent (amount=stake) with idempotency key pattern: fail-{commitment_id}-{status_transition_seq}.
3. If requires_action (3DS): surface Payment Status Modal. On completion, webhook updates status -> succeeded.
4. If failed (retryable) schedule daily retry job up to 5 attempts. Hard declines flagged (no retries) -> notify user to update method.

### Webhooks
Handle events: payment_intent.succeeded, payment_intent.payment_failed, payment_intent.requires_action (if using asynchronous). Update PaymentIntentLog + AuditLog.

### Idempotency
Auto-fail scanner uses consistent idempotency key; if PaymentIntent already exists in final status skip recreation.

---
## 7. Notifications & Reminders

Event Types:
- reminder.checkin_due
- commitment.deadline_reached (DecisionNeeded)
- commitment.grace_final_warning (15m before grace end)
- payment.requires_action
- payment.failed_retry
- payment.final_failure

Quiet Hours: Suppress non-critical reminders (checkin_due) during user-config quiet window; send at end of window. Critical (deadline_reached, grace_final_warning, payment.*) bypass quiet hours.

Channels: push, email (user prefs per channel + per commitment override). Fallback: In-app badge count.

Localization: Each notification references template key + variable map.

---
## 8. Internationalization
All UI strings replaced by keys, example:
- "Your Commitments" => dashboard.header.title
- "Add Commitment" => dashboard.add_button.label
- Badges: badge.on_track / badge.at_risk / badge.decision_needed
- Errors: error.deadline.too_soon, error.schedule.no_occurrence, error.checkin.not_allowed
Maintain JSON resource bundles per locale. Pluralization handled by ICU message format.

---
## 9. Validation Rules Summary
- goal: length 1–200.
- stake_amount: >0. Precision up to 2 decimals on input -> convert to integer minor units.
- deadline: now + min_lead (default 1h) to now + max_duration (configurable, e.g. 180 days).
- schedule: at least one occurrence strictly before deadline_utc.
- editing: disallowed (except goal text) after editing_locked_at_utc.
- cancellation: only if status=Active & now < editing_locked_at_utc.
- deletion: only allowed after terminal or cancelled state.

---
## 10. Audit Logging
Event Types (examples):
- commitment.created
- commitment.updated
- commitment.cancelled
- commitment.deleted
- commitment.status_transition (generic with from/to)
- schedule.updated
- checkin.created
- payment.intent.created / payment.intent.succeeded / payment.intent.failed / payment.retry.scheduled
- notification.sent / notification.failed
- auto.fail.executed

All events store: user_id (actor), commitment_id (when relevant), event_type, data_json (before/after diffs or payload), correlation_id.

---
## 11. Security & Privacy
- Photos: Stored in object storage (private). Access via time-limited signed URLs. Virus scan pipeline.
- PII minimization: Only goal text stored; no payment PAN data (Stripe tokens only).
- Rate limiting: Fail/Complete actions (5/min per user) to mitigate abuse.
- Authorization: Only owner may view/modify commitment (except admin tools not in scope).

---
## 12. Accessibility
- Keyboard reachable modals, ARIA roles (role=dialog) and focus trap.
- Progress bar uses aria-valuenow / min / max.
- Badge color paired with text label (no color-only conveyance).

---
## 13. Background Jobs
1. Reminder Horizon Builder: Generate ReminderEvents up to (now + HORIZON_DAYS) or until deadline; run every hour; idempotent.
2. Grace Expiry Scanner: Every 5 minutes find commitments status=DecisionNeeded and grace_expires_utc < now; transition to Failed & initiate payment.
3. Payment Retry Worker: Daily (or schedule per PaymentIntent next_retry_at) attempt retry; stop after 5 attempts or success/hard failure.
4. Notification Dispatcher: Processes pending ReminderEvents & event notifications.

---
## 14. Testing Strategy
- Unit: recurrence generation (daily/weekly/monthly edge months; DST).
- Unit: risk badge logic thresholds.
- Unit: status transition guard rails.
- Integration: payment fail -> retries; decision modal flows.
- E2E: create -> check-ins -> grace -> auto-fail.
- Localization snapshot tests (keys resolved).

---
## 15. Open Future Extensions (Out of Scope Now)
- Community engagement features (feed, reactions).
- AI personalized feedback (requires analytics ingestion + model invocation).
- Advanced progress computation blending subjective scoring.
- Schedule timezone-shift option (keeping local wall-clock) if user demand.

---
## 16. Glossary
- DecisionNeeded: State during grace window awaiting final user decision.
- Grace Window: Configurable post-deadline interval (default 60m) before auto-fail.
- ReminderEvent: Concrete scheduled check-in or decision notification instance.

---
## 17. Sample i18n Entries (EN)
```
{
  "dashboard.header.title": "Your Commitments",
  "dashboard.add_button.label": "Add Commitment",
  "badge.on_track": "On Track",
  "badge.at_risk": "At Risk",
  "badge.decision_needed": "Decision Needed",
  "tooltip.edit_locked": "Editing locked within 24h of deadline",
  "error.deadline.too_soon": "Deadline must be at least 1 hour from now",
  "error.schedule.no_occurrence": "Schedule must occur at least once before deadline",
  "error.checkin.not_allowed": "Cannot add check-in in current state",
  "checkin.title": "Progress update for {goal}",
  "deadline.modal.title": "It's decision time",
  "deadline.modal.snooze": "Remind me in 15 min"
}
```

---
## 18. Example Timeline Scenario
1. Create (now=2025-08-18 10:00Z) deadline=2025-09-18 10:00Z; schedule weekly Mon 09:00 Europe/Bratislava.
2. Check-ins logged Aug 18, Aug 25; Missed Sept 1 & 8 -> completion ratio 0.5 (2 of 4 elapsed) after ~75% time -> At Risk badge.
3. At deadline status -> DecisionNeeded; user snoozes twice; grace ends -> auto fail; payment intent created and succeeds on first attempt.

---
## 19. Implementation Notes
- Use Stripe Setup Intents for payment method collection at creation if no default method.
- Use optimistic UI updates; server confirmation via websockets or polling for payment outcomes.
- All timestamps stored UTC; convert for display only.

---
End of Spec.
