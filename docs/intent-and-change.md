# Project Frame

## Reservation domain
Domain for padel courts reservation 

## Purpose
- Reservation system for the new padel courts in Ostrava. 
- It is for people who are searching for padel courts to play on with their family or friends. 

## Users / Stakeholders
Admin
User

## Core concepts
Reservation, Court, Time-slot, User 

## Core operations
- Create reservation
- Confirm / approve reservation
- Cancel reservation
- Check availability

## Persistent state
Reservation - User ID, name, surname, time slot, court number, state
Court - number, availability

## State-changing operation
- DRAFT → CONFIRMED
- DRAFT → REJECTED
- CONFIRMED → CANCELED
- CONFIRMED → EXPIRED


## Common business rule
Confirmed reservations for the same resource must not overlap.

## Domain-specific business rule
User can have at most 2 active reservations in advance.

## External / system boundary
Notification Service (email notification one day before reservation)
Access Control System (QR code generation for easy court access)

## Assumption
Courts are going to have fixed opening hours (8:00-22:00) and fixed time slots (60 minutes).

## Unknown


---

## C08 / CR-08 — Controlled Change Card: 12-Hour Cancellation Cutoff Policy

- **Change source and concrete project need:** Facility management (Padel Ostrava club owner) reported revenue loss caused by players cancelling `CONFIRMED` courts minutes before the match starts, leaving the slot empty and unbookable by other players. Management requests: *"A standard player may cancel a CONFIRMED reservation no later than 12 hours before the slot start time (`StartTime`). Unapproved `PENDING_APPROVAL` requests may still be withdrawn at any time before start, and an Administrator may cancel any reservation at any time before start."*
- **Before commit/tag, accepted spec/BR/ADR, schema and config:**
  - `baseline=tag C07` (post-C06 fix commit on `main`); `spec=OP-04/BR-03@v0.2` (`docs/c02-specification.md`); `arch=ADR-01 & G2/G3` (`docs/architecture-and-decisions.md`); `schema=20260915190609_InitialCreate + IX_Reservations_ExclusiveActiveSlot`; `runtime=.NET 8.0 / SQLite`; `command=dotnet test`.
- **Previous observable behavior / limitation:**
  - In `tag C07`, `ReservationManagementService.CancelReservationAsync(int reservationId, int userId, bool isAdmin)` allows any owner (or Admin) to cancel a `Confirmed` or `PendingApproval` reservation at any time right up until the slot expires, without enforcing any advance cancellation cutoff window (`BR-03` had no minimum lead time before `StartTime`).
- **New intended behavior, scope and explicit out-of-scope:**
  - **In scope:** Enforce a 12-hour cutoff rule (`slotStartDateTime - now >= 12 hours`) when a standard player (`isAdmin == false`) attempts to cancel a `CONFIRMED` reservation via `OP-04`. Preserve unrestricted cancellation before start for `PENDING_APPROVAL` reservations (since no court commitment is finalized yet) and for Administrators (`isAdmin == true`).
  - **Out of scope:** Payment penalties/refund processing, new background workers, database schema changes, or changes to `CancelDraftAsync` (5-minute draft removal).
- **Acceptance conditions (positive / negative / boundary / regression):**
  - **AC-01 (Positive — Standard Player > 12h):** A player cancelling their own `CONFIRMED` reservation strictly more than 12 hours before `Date + TimeSlot.StartTime` succeeds; state becomes `Canceled` and slot is released.
  - **AC-02 (Negative — Standard Player < 12h):** A player (`isAdmin == false`) attempting to cancel a `CONFIRMED` reservation less than 12 hours before `Date + TimeSlot.StartTime` is rejected with an explicit Czech error message; reservation remains `Confirmed` and no cancellation email is sent.
  - **AC-03 (Boundary — Exactly 12:00h):** At `now == slotStartDateTime - 12 hours` (`slotStartDateTime <= now.AddHours(12)`), the 12-hour advance window has elapsed and standard player cancellation of a `CONFIRMED` booking is **rejected** (inclusive cutoff `<=`).
  - **AC-04 (Role & State Exemption):** An Administrator (`isAdmin == true`) can cancel a `CONFIRMED` reservation even `< 12h` before start; a standard player can cancel a `PENDING_APPROVAL` request `< 12h` before start.
  - **AC-05 (Regression):** `BR-02` (no overlapping active reservations) and `BR-04` (max 2 active reservations quota released upon valid cancellation) remain intact.

### Impact Analysis

- **Impact analysis method and exact inspected artifacts:** Traced `AC-01..AC-05` starting from the specification (`docs/c02-specification.md` OP-04 & BR-03) and statechart (`docs/c02-diagrams.md`) through the HTTP boundary (`Controllers/ReservationController.cs` `POST Cancel`), authoritative lifecycle owner (`Services/IReservationManagementService.cs` & `Services/ReservationManagementService.cs` `CancelReservationAsync`), persistence (`Services/IReservationRepository.cs`, `Models/Reservation.cs`), UI templates (`Views/Reservation/Index.cshtml`, `MyReservations.cshtml`), and test suite (`ReservationManagementServiceTests.cs`).

| Dimension / exact location | CHANGE / CHECK / NO IMPACT and why | Action / check | Owner / evidence |
| :--- | :--- | :--- | :--- |
| **Accepted spec / BR / state** (`docs/c02-specification.md` OP-04, BR-03; `docs/c02-diagrams.md`) | **CHANGE** — `OP-04` preconditions and `BR-03` cancellation policy change to require `now < StartTime - 12h` for player-initiated `CONFIRMED` cancellation. State diagram guard on `CONFIRMED -> CANCELED` needs updated condition `[player & >12h before start OR admin]`. | Update `BR-03` and `OP-04` preconditions/failure outcomes in `docs/c02-specification.md` and transition guard in `docs/c02-diagrams.md`. | Jan Bruthans / diff in `docs/c02-specification.md` & `c02-diagrams.md`. |
| **Architecture / owner / interfaces** (`docs/architecture-and-decisions.md`, `IReservationManagementService.cs`) | **CHECK** — `ReservationManagementService` is already the single authoritative lifecycle owner (C03 G2/G3). Method signature `CancelReservationAsync(int reservationId, int userId, bool isAdmin)` already receives `isAdmin` and returns `(bool Success, string Message)`. | Verify no signature change is needed on `IReservationManagementService` and controller delegation remains unchanged. | Jan Bruthans / `Services/IReservationManagementService.cs` inspection. |
| **Detailed design / code** (`Services/ReservationManagementService.cs` — `CancelReservationAsync`) | **CHANGE** — `CancelReservationAsync` currently checks ownership and `State` (`Confirmed` or `PendingApproval`), but lacks the temporal check comparing `reservation.Date.ToDateTime(reservation.TimeSlot.StartTime)` against `DateTime.Now.AddHours(12)` when `!isAdmin && State == Confirmed`. | Add temporal guard inside `CancelReservationAsync` and update C05 DD section in `docs/architecture-and-decisions.md`. | Jan Bruthans / C09 code commit + unit tests. |
| **Data / schema / migration** (`Models/Reservation.cs`, `Services/AppDbContext.cs`, `Migrations/`) | **NO IMPACT** — `Reservation.Date` (`DateOnly`) and `TimeSlot.StartTime` (`TimeOnly`) are already persisted and eagerly loaded via `.Include(r => r.TimeSlot)` in `ReservationRepository.GetByIdAsync`. No new columns or migrations are needed. | Confirm `GetByIdAsync` includes `TimeSlot` (already verified in C05). | Jan Bruthans / `Services/ReservationRepository.cs` line 18. |
| **Config / CI / release** (`appsettings.json`, `Program.cs`) | **NO IMPACT** — The 12-hour cutoff is a fixed domain constant (`12 hours`), analogous to the existing 24-hour Court 4 rule and 5-minute draft TTL. No external service or secret configuration changes. | Keep constant in domain service; document rule in release notes. | Jan Bruthans / `Program.cs` check. |
| **Tests / integration / risk** (`ReservationManagementServiceTests.cs`) | **CHANGE** — Existing cancellation test (`CancelReservationAsync_DraftReservation_IsRejected`) only tests invalid `Draft` state. New unit tests are required for `AC-01` (>12h), `AC-02`/`AC-03` (<=12h rejected for player), and `AC-04` (Admin override <12h & `PendingApproval` <12h). | Add 3 focused unit tests in `ReservationManagementServiceTests.cs` and re-run `TC-C08-01` & `TC-C08-02` regression tests. | Jan Bruthans / `dotnet test` execution log in C09. |
| **Operations / recovery / UI** (`Controllers/ReservationController.cs`, `Views/Reservation/Index.cshtml`, `MyReservations.cshtml`) | **CHECK** — `ReservationController.Cancel` already forwards `result.Message` into `TempData["ErrorMessage"]` when `result.Success == false`. Existing DB records require no migration. | Verify in browser that rejecting a late cancellation displays the red alert banner cleanly without mutating state. | Jan Bruthans / C09 UI smoke test. |

### Feasible Alternatives Comparison

| Option | Meets AC? | Benefits | Cost / failure / recovery | How to verify |
| :--- | :--- | :--- | :--- | :--- |
| **Option A: Authoritative server-side guard in `ReservationManagementService.CancelReservationAsync`** | **YES (AC-01..AC-05)** | Enforces cutoff atomically in the single lifecycle owner regardless of whether request comes from calendar modal, MyReservations page, or direct HTTP POST; zero DB schema changes; easy rollback (single method condition). | Uses server local clock (`DateTime.Now`, same as Court 4 24h rule); UI still shows "Cancel" button `< 12h` until clicked, returning an error toast. | Unit tests for `>12h`, `==12h`, `<12h` (player vs. admin, `Confirmed` vs. `PendingApproval`) + `TC-C08-01/02` regression. |
| **Option B: UI-only disabling of Cancel button in Razor views (`Index.cshtml`, `MyReservations.cshtml`) + controller check** | **PARTLY** (Fails domain encapsulation from C03/C05) | Hides/disables the button visually in the browser `< 12h` before match start. | Violates C03 architecture rule (domain rules leaked into Razor/Controller); bypassable if called outside UI; duplicates time calculation across multiple `.cshtml` files. | Manual browser check; cannot be verified via `ReservationManagementService` unit tests. |
| **Option C: No code change (`NO_CHANGE`) — rely on written club house rules and manual admin monitoring** | **NO** (Fails AC-02 & AC-03) | Zero development effort and zero regression risk. | Players can still click Cancel 5 minutes before match start in the app, releasing the slot without penalty and losing club revenue. | N/A (does not prevent late self-service cancellation). |

- **Consequence of inaction / feasible alternative:** Without `CR-08`, players can cancel confirmed bookings minutes before play, causing unrecoverable empty court slots. Option A is the smallest safe change that respects our C03/C05 architecture (`ReservationManagementService` as sole lifecycle owner).
- **Peer reviewer and challenged assumption:** Reviewed by team member (Padel Team peer review). Challenged assumption: *"Should the 12-hour cutoff also block a player from withdrawing an unapproved `PendingApproval` request on Court 4?"* — Resolved: **No**, because a `PendingApproval` request is not yet a confirmed contract (and expires automatically at 24h before start anyway); blocking its withdrawal would unfairly trap the player's 2-slot quota (`BR-04`).
- **Decision, owner, date, rationale:** **`ACCEPT` (Option A)** — Owner: Jan Bruthans, Date: 2026-11-08. Option A satisfies all acceptance conditions (`AC-01..AC-05`) with a minimal, localized guard inside `ReservationManagementService.CancelReservationAsync`, preserving architectural boundaries and requiring zero database migrations.
- **If ACCEPT — bounded C09 delta and new/regression test oracles:**
  1. **Bounded Delta for C09:** In `ReservationManagementService.CancelReservationAsync`, when `reservation.State == ReservationState.Confirmed && !isAdmin`, compute `slotDateTime = reservation.Date.ToDateTime(reservation.TimeSlot.StartTime)` and if `slotDateTime <= DateTime.Now.AddHours(12)`, return `(false, "Potvrzenou rezervaci lze zrušit nejpozději 12 hodin před začátkem hry.")` without mutating state or sending email.
  2. **New Test Oracles for C09:**
     - `CancelReservationAsync_ConfirmedPlayerMoreThan12h_Succeeds` (`now + 24h` → `Success == true`, `State == Canceled`).
     - `CancelReservationAsync_ConfirmedPlayerLessThanOrEqual12h_IsRejected` (`now + 6h` → `Success == false`, `State == Confirmed`, `email.SentCount == 0`).
     - `CancelReservationAsync_ConfirmedAdminLessThan12h_Succeeds` (`now + 2h`, `isAdmin: true` → `Success == true`, `State == Canceled`).
  3. **Regression Oracles:** All 7 existing C05/C07 unit tests + `TC-C08-01` and `TC-C08-02` SQLite persistence tests must pass (`dotnet test`).
- **Open uncertainty / monitoring or next decision trigger:** `ReservationManagementService` uses `DateTime.Now` directly rather than an injected `TimeProvider`/`IClock`, so boundary unit tests use relative offsets from `DateTime.Today`/`DateTime.Now`. **Trigger:** If sub-minute boundary flake occurs in CI, inject `TimeProvider` into `ReservationManagementService` during C09 implementation.
