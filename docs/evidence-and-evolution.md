## C01 Engineering Spike
- Question / unknown: Can a new team member perform a clean repository checkout and successfully build and run the project relying exclusively on the instructions in `README.md`, without the system crashing due to a missing local SQLite database or unconfigured SMTP credentials?  
- What we did: 
1. The second team member performed a clean clone of the repository into a fresh directory on his computer.
2. Attempted to compile and run the application using standard CLI commands (`dotnet build` and `dotnet run`).

- Observed result: During the first run, attempting to complete a reservation caused an email to not be sent due to missing configuration keys. The SQLite database schema was successfully created and seeded on application launch via `Database.Migrate()` / `EnsureCreated()`. After adding the step-by-step setup guide in `README.md`, the run succeeded on the first attempt without errors.

- Decision / what changes because of the result:
For our development environment, we have updated the README with mandatory documentation policies, added information about secrets and configuration, and retained embedded SQLite with automatic schema bootstrapping alongside console fallbacks for external services.


## Evidence C02: Specification → Running Application

Accepted baseline:
- Baseline v0.2 (C02): Four core operations (Create Reservation, Check Availability, Confirm Reservation, Cancel Reservation), 5-minute DRAFT TTL, and a maximum active reservation limit of 2 per user. The application was extended with manual approval for Tournament Court No. 4 by the facility manager (Admin).

Demonstrated core operations:
- OP-01 Create Reservation: Creation of a temporary 5-minute DRAFT for an available time slot (applies identically to Courts 1–4).
- OP-02 Check Availability: Viewing the court and time slot matrix (08:00–22:00); slots are blocked by CONFIRMED, active DRAFT, and the new PENDING_APPROVAL states.
- OP-03 Confirm Reservation: The player confirms the DRAFT. Standard courts (1, 2, 3) transition directly to CONFIRMED (sending a QR code with confirmation). Court No. 4 transitions to PENDING_APPROVAL
- OP-04 Cancel Reservation: The player or Admin cancels a reservation (in DRAFT, PENDING_APPROVAL, or CONFIRMED state); the time slot is immediately released.
- OP-05 Approve / Reject Reservation (New operation for C03): The Admin approves (PENDING_APPROVAL → CONFIRMED + sending QR code) or rejects (PENDING_APPROVAL → REJECTED + releasing time slot) requests for Court No. 4.

Actually performed verification examples:
- Tournament Court (Court No. 4): A user confirms a draft on Court No. 4 → the reservation transitions to PENDING_APPROVAL. The time slot is displayed as "Occupied" to all other users. 
- Admin Approval: The Admin clicks Approve in the interface → the state changes to CONFIRMED, and the user receives an email with a QR code.
- Admin Rejection: The Admin clicks Reject on a reservation in PENDING_APPROVAL state → the state changes to REJECTED, the time slot immediately turns green (available) in the calendar, and the player's capacity is restored within their 2-reservation limit.
- 5-Minute DRAFT Expiration: A user leaves an unconfirmed modal open for Court No. 4 → after 300 seconds, both the client timer and server cleanup cancel the DRAFT, and the cell in the calendar switches back to "Reserve".
- 24-Hour PENDING_APPROVAL Expiration: If the Admin does not approve the reservation within 24 hours, it transitions to EXPIRED and becomes available for other players.

Identified inconsistency and solution:

Summary of change impact:
- Domain entities: Added PendingApproval and Rejected values to the ReservationState enum.
- Business rules: The exclusive resource invariant (BR-02) and active reservation limit of 2 (BR-04) were extended to account for the PENDING_APPROVAL state.
- User interface: For standard users on Court No. 4, confirming displays "Pending manager approval". The time slot is shown as "Occupied" to all other users.
  
Remaining assumption / unknown:
- Assumption: The facility manager (Admin) regularly accesses the system and processes tournament court requests in a timely manner.
- Unknown: How to handle edge-case race conditions in the database if a user cancels a PENDING_APPROVAL request right before the Admin makes a decision.

Architectural drivers carried over to C03:
- A reservation in PENDING_APPROVAL state must not block a court indefinitely. A scheduled task (background worker) transitions unhandled reservations to EXPIRED and releases the court 24 hours before the game starts.
- Strict role separation (User vs. Admin) for accessing operation OP-05 Approve / Reject.
  
Commit / application tag:
- tag C02

## C03 — Architecture Evidence

**Baseline:** v0.3

**Part A:** OP-05 — Approve / Reject Reservation. The current controller checks authorization and reservation state, saves changes, generates a QR code and requests email delivery.

**Drivers:**
- AD-01: Prevent double-booking.
- AD-02: Expire pending approvals automatically.
- AD-03: Handle email and QR failures.
- AD-04: Protect valid reservation state transitions.

**Decision question:** How can the system guarantee that two active reservations never occupy the same court and time slot?

**Alternatives:**
- Alternative A: Database constraint for blocking reservations.
- Alternative B: Application-level transaction through a central service.

**Scenario walkthrough:** An administrator approves a reservation while another user tries to reserve the same slot. The database constraint in Alternative A should allow only one blocking reservation to succeed.

**ADR:** ADR-01 recommends a database constraint, subject to verification that the configured database provider supports the required constraint.

**Views:**
- **Domain class:** Court, TimeSlot, User, UserRole, Reservation and ReservationState.
- **Context:** Player and Administrator interact with the Reservation System, which uses a database and SMTP email server.
- **Static architecture:** Reservation Controller, Reservation Management Service, Reservation Repository, Database, QR Code Service and Notification Service.
- **State ownership:** Reservation Management Service owns lifecycle decisions; the player, administrator or background worker can request transitions.
- **Runtime/deployment:** ASP.NET Core application with database and SMTP dependencies; a background worker is proposed for pending-reservation expiry.
- **Design sequence:** Administrator approval flow, including persistence, QR generation and notification.
- **Focused design class:** ReservationController, ReservationManagementService, IReservationRepository, Reservation, QRCodeService and NotificationService.

**Cross-view issues found/resolved:** The views consistently assign lifecycle decisions to the proposed Reservation Management Service and BR-02 enforcement to the database. Database-provider support and the exact constraint/migration remain to be verified.

**AS-IS → TO-BE delta:** Replace application-only double-booking checks with database enforcement; centralize lifecycle transitions; separate repository, QR and notification responsibilities; add a background worker for expiry.

**Implementation changes:** Implemented `ReservationManagementService`, `ReservationRepository`, `QRCodeService`, `ReservationExpirationWorker`, and the SQLite filtered unique index `IX_Reservations_ExclusiveActiveSlot` on `(CourtId, Date, TimeSlotId) WHERE State IN (1, 2)`. Refactored `ReservationController` to delegate all state transitions to the service.

**Behaviour verification:** Verified both via running application scenarios (C02 verification examples) and automated xUnit tests (`dotnet test` — 6/6 passing).

**Architecture conformance rule + result:** Rule — only `ReservationManagementService` may change a reservation's lifecycle state (`Reservation.State`). Check — static search `Select-String -Path "reservation_system_for_padel/Controllers/*.cs" -Pattern "\.State\s*="`. Result — **PASS** (zero state mutations in controllers).

**Remaining uncertainty / risk:** Synchronous SMTP email dispatch inside HTTP request scope can add latency if the external mail server is slow; local server timezone (`DateTime.Now`) is assumed.

**Commit/tag:** tag C03


## C05 DD Verification Evidence

### 1. Tests Derived from Detailed Design

| DD Element / Decision | What Must Hold? | Test Level | Test Case |
| :--- | :--- | :--- | :--- |
| **Algorithm branch & State transition (DR-03)** | Confirming a `Draft` on Tournament Court No. 4 must transition state to `PendingApproval`, send a notification email, and **not** generate a QR code. | Unit / Component | `ConfirmDraftAsync_Court4_TransitionsToPendingApprovalWithoutQrCode` |
| **Collaboration & Post-transition side effects (DR-06)** | Confirming a `Draft` on Standard Courts (1–3) must transition state to `Confirmed`, invoke `IQRCodeService`, and pass the PNG bytes to `IEmailSender`. | Unit / Component | `ConfirmDraftAsync_StandardCourt1_TransitionsToConfirmedAndGeneratesQrCode` |
| **Interface contract & BR-04 Quota Invariant (DR-02)** | `CreateDraftAsync` must reject a request when the user already holds 2 active future reservations (`Draft`, `PendingApproval`, or `Confirmed`). | Unit | `CreateDraftAsync_WhenUserAlreadyHasTwoActiveReservations_ReturnsQuotaFailure` |
| **Temporal advance-booking rule (DR-01)** | `CreateDraftAsync` for Court No. 4 must reject slots starting in less than 24 hours with a validation message and without persisting a draft. | Unit | `CreateDraftAsync_Court4LessThan24HoursBeforeStart_ReturnsAdvanceBookingFailure` |
| **Failure behavior & Concurrency contract (ADR-01 / BR-02)** | When `IReservationRepository.SaveChangesAsync()` throws a `DbUpdateException` due to a concurrent unique index violation, the service catches it, aborts email/QR side effects, and returns `(false, conflictMessage)`. | Unit | `ConfirmDraftAsync_WhenConcurrentDbUpdateExceptionOccurs_ReturnsGracefulFailure` |
| **State transition OP-05 (DR-03 / DR-06)** | `ApprovePendingReservationAsync` transitions a `PendingApproval` reservation to `Confirmed`, generates the access QR code, and emails it to the user. | Unit / Component | `ApprovePendingReservationAsync_ValidPendingRequest_TransitionsToConfirmedAndSendsQr` |

---

### 2. Test Level Justification

#### Test 1: `ConfirmDraftAsync_Court4_TransitionsToPendingApprovalWithoutQrCode`
- **Selected level:** Unit / Component level (testing `ReservationManagementService` with in-memory test doubles for `IReservationRepository`, `IQRCodeService`, and `IEmailSender`).
- **Why this level:** The branching decision between standard courts (`Confirmed` + QR) and Court No. 4 (`PendingApproval` without QR) is pure domain orchestration logic inside `ReservationManagementService`. Isolating it from SQLite and a live SMTP server makes the test fast, deterministic, and directly focused on state transition and side-effect rules.
- **What design decision does this test verify?** Verifies **DR-03** and **DR-06**: Court No. 4 confirmation must never issue an entry QR code prematurely before administrator approval.

#### Test 2: `ConfirmDraftAsync_WhenConcurrentDbUpdateExceptionOccurs_ReturnsGracefulFailure`
- **Selected level:** Unit level with simulated persistence failure (`ThrowConflictOnSave = true` on `IReservationRepository`).
- **Why this level:** Reproducing exact millisecond race conditions against a real SQLite file is non-deterministic in standard test runners. Simulating `DbUpdateException` at the `IReservationRepository` contract boundary deterministically verifies how `ReservationManagementService` handles atomic constraint violations defined in **ADR-01**.
- **What design decision does this test verify?** Verifies the **IReservationManagementService failure contract** and **DR-06**: database concurrency exceptions must not leak as unhandled 500 HTTP crashes and must prevent false-positive confirmation emails from being sent.

---

### 3. Unit / Component Tests Executed

- **Test project / suite:** `reservation_system_for_padel.Tests` (`ReservationManagementServiceTests.cs`)
- **Command executed:** `dotnet test`
- **Execution result:** 6 passed, 0 failed.
  - `[PASS] ConfirmDraftAsync_Court4_TransitionsToPendingApprovalWithoutQrCode`
  - `[PASS] ConfirmDraftAsync_StandardCourt1_TransitionsToConfirmedAndGeneratesQrCode`
  - `[PASS] CreateDraftAsync_WhenUserAlreadyHasTwoActiveReservations_ReturnsQuotaFailure`
  - `[PASS] CreateDraftAsync_Court4LessThan24HoursBeforeStart_ReturnsAdvanceBookingFailure`
  - `[PASS] ConfirmDraftAsync_WhenConcurrentDbUpdateExceptionOccurs_ReturnsGracefulFailure`
  - `[PASS] ApprovePendingReservationAsync_ValidPendingRequest_TransitionsToConfirmedAndSendsQr`

---

### 4. Design ↔ Implementation Consistency Check

| DD Decision | Implemented? | Evidence | Action |
| :--- | :--- | :--- | :--- |
| **`IReservationManagementService` contract & tuple return types** | **YES** | `Services/IReservationManagementService.cs`, `Services/ReservationManagementService.cs` | **Keep** — matches DD contract and preconditions/postconditions. |
| **Repository abstraction (`IReservationRepository`)** | **YES** | `Services/IReservationRepository.cs`, `Services/ReservationRepository.cs` | **Keep** — encapsulates EF Core queries and `SaveChangesAsync`. |
| **24-hour advance booking check for Court No. 4 (DR-01)** | **YES** | `ReservationManagementService.CreateDraftAsync` + `CleanupExpiredReservationsAsync` | **Keep** — prevents immediate expiration of newly created Court 4 requests. |
| **QR generation isolated behind `IQRCodeService` (DR-06)** | **YES** | `Services/IQRCodeService.cs`, `Services/QRCodeService.cs` | **Keep** — controller and service no longer depend on `QRCoder` directly. |
| **Autonomous expiration via `ReservationExpirationWorker` (DR-05)** | **YES** | `Services/ReservationExpirationWorker.cs` calling `CleanupExpiredReservationsAsync` | **Keep** — runs every 60s and expires both 5m drafts and `< 24h` pending requests. |
| **Read queries in `ReservationController` (`Index`, `PendingApprovals`, `MyReservations`)** | **PARTLY** | `Controllers/ReservationController.cs` delegates all state mutations to `IReservationManagementService`, but still reads calendar views via `AppDbContext`. | **Keep (Conscious Pragmatic Choice)** — DD scopes state-mutating lifecycle rules inside `ReservationManagementService`; simple read-only view projections in the controller do not violate state transition invariants. |

---

### 5. Mismatch Resolution & Remaining Design Risk

- **Design ↔ Implementation Mismatch:** During initial testing of C03/C05, we discovered that allowing players to create `PendingApproval` reservations for Court No. 4 less than 24 hours before the match caused the background expiration cleanup (`CleanupExpiredReservationsAsync`) to immediately transition those requests to `Expired`.
- **Resolution:** Updated both the Detailed Design (**DR-01**) and `ReservationManagementService.CreateDraftAsync` (via `GetCourtByIdAsync` and `GetTimeSlotByIdAsync` on `IReservationRepository`) to enforce a 24-hour advance-booking precondition for Court No. 4.
- **Remaining Design Risk:**
  - `IEmailSender.SendEmailAsync` is still invoked synchronously after `SaveChangesAsync()` inside the HTTP request scope. While `EmailSender` catches and logs SMTP exceptions so the reservation state remains valid, a slow SMTP server can still increase HTTP response latency. Moving email dispatch to an asynchronous background queue remains a future improvement if traffic grows.
  - Time comparisons use server local time (`DateTime.Now` / `DateOnly.FromDateTime`), assuming the facility and server operate in the same timezone (Czechia / CET).

**Commit/tag:** `tag C05`

---

## C06 Internal Evidence Assessment

- **Assessed baseline:** `tag C05` (commit `79fbe08` / `b492019`), assessed by reviewing team *Daniel Kodrla a spol.*
- **Verified findings:**
  - **F1 (C02 — OP-01 Preconditions in `CreateDraftAsync`):** `ReservationManagementService.CreateDraftAsync` checked `date < today`, but did not verify `court.IsActive == true` or reject time slots on the current day whose `StartTime` had already passed when called via direct HTTP POST.
  - **F2 (C02 — OP-04 State Precondition in `CancelReservationAsync`):** `ReservationManagementService.CancelReservationAsync` verified ownership and admin role, but did not enforce that the reservation is in an active cancellable state (`Confirmed` or `PendingApproval`), allowing a `Draft` reservation to transition to `Canceled` instead of using `CancelDraftAsync`.
- **Owner disposition:**
  - **F1:** `ACCEPTED — change required`
  - **F2:** `ACCEPTED — change required` (with clarification that under the C02/v0.2 baseline change, both `Confirmed` and `PendingApproval` are valid states for `OP-04 Cancel`, whereas `Draft` must be rejected by `CancelReservationAsync`).
- **Actions / justified no-change decisions:**
  - Updated `ReservationManagementService.CreateDraftAsync` to explicitly reject inactive courts (`!court.IsActive`) and today's time slots where `timeSlot.StartTime <= currentTime`.
  - Updated `ReservationManagementService.CancelReservationAsync` to guard state transitions so only `Confirmed` and `PendingApproval` reservations can be cancelled; kept the reviewing team's verification test `CancelReservationAsync_DraftReservation_IsRejected` in `ReservationManagementServiceTests.cs` (all 7/7 unit tests now pass).
- **Remaining evidence gaps:**
  - Align `<TargetFramework>` in `reservation_system_for_padel.Tests.csproj` with the main project (`net8.0`) so `dotnet test` runs on .NET 8 SDK without requiring .NET 9 runtime.
  - Update the status label of `ADR-01` in `docs/architecture-and-decisions.md` from `Proposed` to `Accepted & Verified` and explicitly mention `PENDING_APPROVAL` directly inside the `BR-02` paragraph of `docs/c02-specification.md`.

---
 
## C07–C08 — Project Assessment, Risk Management & Reviews

### 1. Disposition of C06 Internal Evidence Assessment Findings

During the independent assessment (C06) conducted by the assessment team, three candidate findings were identified. Their technical verification, accepted disposition, and implemented actions are presented below.

| Finding ID | Claim / Description | Status & Verdict | Owner & Action / Justification |
| :--- | :--- | :--- | :--- |
| **F1** | The `ConfirmDraftAsync` method did not verify whether the court was active (`!court.IsActive`) or whether the slot’s start time had already passed (`timeSlot.StartTime <= currentTime`). | **VERIFIED FINDING**<br>(ACCEPTED) | **Owner:** Dev Team<br>**Action:** Additional validation of court activity and the time window was added directly to the reservation creation/confirmation logic in `ReservationManagementService`. |
| **F2** | The `CancelReservationAsync` method also allowed reservations in the `Draft` state to be cancelled, even though a separate operation, `CancelDraftAsync`, exists for drafts. | **VERIFIED FINDING**<br>(ACCEPTED) | **Owner:** Dev Team<br>**Action:** A state restriction was added — `CancelReservationAsync` now explicitly allows cancellation only for reservations in the `Confirmed` and `PendingApproval` states. This was verified by the `CancelReservationAsync_DraftReservation_IsRejected` test. |

### 2. Risk Register (C07 / C08)

#### R-01: Concurrent Confirmation of the Same Slot (Race Condition / Double Booking)
- **Cause → Uncertain Event → Consequence:** If two users submit a request to confirm/create a reservation for the same court and time at the same millisecond, the application layer may check availability before the first record is written, violating the BR-02 exclusivity invariant (double booking).
- **Initial L / I / Priority:** Likelihood = 2 (Medium), Impact = 3 (High) → **HIGH (6)**.
- **Response Strategy:** **MITIGATE**
- **Treatment, Owner, Due:** Configure a partial unique index in SQLite/EF Core (`IX_Reservations_ExclusiveActiveSlot` for `State IN (1, 2)`) and catch `DbUpdateException` in `ReservationManagementService`. Owner: Backend Lead. Due: Completed (C03).
- **Treatment Evidence:** The index is defined in `AppDbContext.cs`; the `ConfirmDraftAsync_WhenConcurrentDbUpdateExceptionOccurs_ReturnsGracefulFailure` test verifies that the exception is handled correctly.
- **Expected Residual Risk:** L = 1 (Low), I = 2 (Medium) → **LOW (2)**.
- **Actual Residual Risk:** L = 1, I = 2 → **LOW (2)** (verified via unit test and database constraint definition).
- **Acceptance & Contingency:** Accepted by the team. If the database driver fails, the system returns a clear error message.
- **Monitoring / Trigger:** Trigger: Failure of the concurrency integration test. Review when the persistence layer changes.

#### R-02: Tournament Court No. 4 Being Blocked by an Unprocessed Request

- **Cause → Uncertain Event → Consequence:** A player creates a `PendingApproval` reservation for Court No. 4, and the facility administrator (Admin) neither approves nor rejects it before the match starts. As a result, the court remains blocked and the booking opportunity is lost, reducing utilisation of the premium court.
- **Initial L / I / Priority:** Likelihood = 2 (Medium), Impact = 2 (Medium) → **MEDIUM (4)**.
- **Response Strategy:** **MITIGATE**
- **Treatment, Owner, Due:** Implement an autonomous `ReservationExpirationWorker` background service that checks every 60 seconds and automatically expires pending reservations whose start time is less than 24 hours away. Owner: Background Services Dev. Due: Completed (C05).
- **Treatment Evidence:** The `ReservationExpirationWorker.cs` class and the `CleanupExpiredReservationsAsync()` method in `ReservationManagementService.cs`.
- **Expected Residual Risk:** L = 1 (Low), I = 1 (Low) → **LOW (1)**.
- **Actual Residual Risk:** L = 1, I = 1 → **LOW (1)** (verified).
- **Acceptance & Contingency:** Accepted. The administrator is also notified by email.
- **Monitoring / Trigger:** Trigger: A player complaint about a request that has not been answered.

#### R-03: Latency or Outage of the External SMTP Gateway When Sending Emails with QR Codes

- **Cause → Uncertain Event → Consequence:** Emails are sent through an external SMTP server synchronously within the HTTP request scope. If the SMTP server does not respond or times out, the HTTP request may fail or become significantly slower, even though the reservation has already been confirmed in the database.
- **Initial L / I / Priority:** Likelihood = 2 (Medium), Impact = 2 (Medium) → **MEDIUM (4)**.
- **Response Strategy:** **MITIGATE**
- **Treatment, Owner, Due:** Implement `try-catch` blocks in `EmailSender` and `ReservationManagementService` so that an SMTP outage is logged but does not cause the reservation confirmation transaction to fail. The UI displays a success message, and the user can download the QR code later via the `/Reservation/GetQrCode` endpoint. Owner: Integration Dev. Due: Completed.
- **Treatment Evidence:** `EmailSender.cs` safely catches exceptions, and `ReservationManagementService` returns `Success = true` even if email delivery fails.
- **Expected Residual Risk:** L = 1 (Low), I = 2 (Medium) → **LOW (2)**.
- **Actual Residual Risk:** L = 1, I = 2 → **LOW (2)** (verified).
- **Acceptance & Contingency:** Accepted as a pragmatic compromise for a monolithic application. An asynchronous queue (RabbitMQ/Hangfire) has been deferred until future scaling is required.
- **Monitoring / Trigger:** Trigger: An increase in SMTP-related error entries in the application log.

#### R-04: Unauthorized Access to Premium Court Approval (Admin Operations)

- **Cause → Uncertain Event / Consequence:** A regular user attempts to call the POST endpoint for approving/rejecting a reservation for Court No. 4 (`/Reservation/Approve`). If role validation is missing, this could violate the security invariant and allow unauthorized booking of the tournament court.
- **Initial L / I / Priority:** Likelihood = 1 (Low), Impact = 3 (High) → **MEDIUM (3)**.
- **Response Strategy:** **AVOID**
- **Treatment, Owner, Due:** Apply the `[Authorize(Roles = "Admin")]` attribute consistently to all relevant actions in `ReservationController` (`PendingApprovals`, `Approve`, `Reject`). Owner: Security Lead. Due: Completed.
- **Treatment Evidence:** `ReservationController.cs` (lines 220–290) contains explicit role-based authorization.
- **Expected Residual Risk:** L = 1 (Low), I = 1 (Low) → **LOW (1)**.
- **Actual Residual Risk:** L = 1, I = 1 → **LOW (1)** (verified).
- **Acceptance & Contingency:** Accepted. ASP.NET Core Identity reliably enforces role claims.
- **Monitoring / Trigger:** Trigger: Security audit of permissions.

---

## C08 — Controlled Change: Baseline & Pre-Change Verification Evidence

### Step 0 — Frozen Before-Change Baseline

- **Baseline commit/tag:** `tag C08` (commit on `main` prior to `CR-08` 12h cancellation implementation)
- **Specification & Rules:** `OP-01..OP-05`, `BR-01..BR-05` @ `v0.2` (`docs/c02-specification.md`)
- **Architecture & Decisions:** `ADR-01` (SQLite partial unique index `IX_Reservations_ExclusiveActiveSlot`), C03 `G2/G3` (`docs/architecture-and-decisions.md`)
- **Database Schema:** `20260915190609_InitialCreate` + `IX_Reservations_ExclusiveActiveSlot WHERE State IN (1, 2)`
- **Runtime & Stack:** .NET 8.0 SDK (C# 12, ASP.NET Core MVC, EF Core SQLite)
- **Test DB & Setup:** Isolated in-memory SQLite connection (`DataSource=:memory:`) with real DDL schema & unique index
- **Verification Command:** `dotnet test` (9 passed, 0 failed; zero external secrets required)

---

### Step 2 — Executed Baseline Test Cases (Preserved Behavior Verification)

#### C08 / TC-C08-01 — Positive Adjacent Reservations on Exclusive Court (`[10:00, 11:00)` and `[11:00, 12:00)`)

- **Accepted test basis / rule and revision, actual risk ID:** `BR-01` (Discrete 60-minute time slot interval semantics `[start, end)`) & `BR-02` (Exclusive Resource Invariant) @ `v0.2` (`docs/c02-specification.md`); mitigates Risk `R-02`.
- **Baseline commit, DB schema, runtime, data setup and command:** `tag C08`; EF Core SQLite schema with `IX_Reservations_ExclusiveActiveSlot`; .NET 8.0; isolated SQLite `:memory:` database seeded via `EnsureCreated()` (`CourtId = 1`, `TimeSlotId = 3` `[10:00, 11:00)`, `TimeSlotId = 4` `[11:00, 12:00)`, `UserId = 1`); command: `dotnet test --filter TC_C08_01`.
- **Preconditions, roles and exact initial persisted state:**
  - Authenticated Player (`UserId = 1`, `admin@padel.cz` seeded user).
  - Standard `CourtId = 1` (`Number = 1`, `IsActive = true`).
  - Persisted reservation `B1`: `CourtId = 1`, `Date = today + 3 days`, `TimeSlotId = 3` (`10:00–11:00`), `State = CONFIRMED` (`2`).
  - Persisted reservation `B2`: `CourtId = 1`, `Date = today + 3 days`, `TimeSlotId = 4` (`11:00–12:00`), `State = DRAFT` (`0`).
- **Test data including intervals and timezones:** `targetDate = DateOnly.FromDateTime(DateTime.Today.AddDays(3))`; `B1` interval `[10:00, 11:00)`, `B2` adjacent interval `[11:00, 12:00)` (local CET/CEST club date/time).

| # | Action / input | Expected BEFORE execution | Actual AFTER execution | Evidence |
| :---: | :--- | :--- | :--- | :--- |
| 1 | Initialize isolated SQLite DB, apply schema + `IX_Reservations_ExclusiveActiveSlot`, insert `B1` (`CONFIRMED`, slot `10:00–11:00`) and `B2` (`DRAFT`, slot `11:00–12:00`). | Exactly 1 `CONFIRMED` row (`B1`) and 1 `DRAFT` row (`B2`) persisted in `Reservations` table. | `SaveChangesAsync()` succeeded; `B1.Id = 1` (`Confirmed`), `B2.Id = 2` (`Draft`). | `ReservationManagementServiceTests.cs` (`TC_C08_01_AdjacentTimeSlotsOnSameCourt_BothReachConfirmedInSqlite`) |
| 2 | Independently inspect stored slots and states before confirmation. | `B1` occupies `TimeSlotId = 3` (`[10:00, 11:00)`), `B2` occupies `TimeSlotId = 4` (`[11:00, 12:00)`); zero overlap on `(CourtId, Date, TimeSlotId)`. | Verified in SQLite context prior to calling `ConfirmDraftAsync`. | `ReservationManagementServiceTests.cs` |
| 3 | Execute `service.ConfirmDraftAsync(b2.Id, userId: 1)`. | Returns `Success == true` because standard Court 1 does not require admin approval and adjacent slot 4 does not conflict with slot 3. | Returned `(Success: true, Message: "Rezervace byla úspěšně potvrzena a QR kód vám byl odeslán na e-mail!")`. | `dotnet test` execution output (`PASS`) |
| 4 | Independently query committed state from SQLite using `.AsNoTracking()` for both `B1` and `B2`. | Both `B1.State == Confirmed` and `B2.State == Confirmed` exist in SQLite without unique index violation. | `persistedB1.State == Confirmed` and `persistedB2.State == Confirmed` verified via `AsNoTracking()` SQL `SELECT`. | `ReservationManagementServiceTests.cs` |
| 5 | Dispose in-memory SQLite connection. | Test state is completely destroyed and repeatable on next run. | Connection disposed; repeated runs deterministic. | `using var _ = conn;` |

- **Final postcondition / independently read committed-state oracle:** Both adjacent reservations `B1` (`[10:00, 11:00)`) and `B2` (`[11:00, 12:00)`) are persisted in `State = Confirmed` (`2`) in SQLite.
- **Verdict PASS / FAIL / BLOCKED / NOT RUN + supporting observation:** **`PASS`** — Executed via `dotnet test`, confirmed against real SQLite engine with active unique index.
- **If FAIL / If BLOCKED:** N/A (test passed).
- **What remains untested / actual residual exposure:** Multi-process file-system lock contention (`SQLITE_BUSY`) under heavy parallel HTTP load is not covered by single-connection `:memory:` SQLite.

---

#### C08 / TC-C08-02 — Negative Overlapping Confirmation on Same Court and Slot (`[10:00, 11:00)`)

- **Accepted test basis / rule and revision, actual risk ID:** `REQ-04` & `BR-02` (Exclusive Resource Invariant — at most one blocking reservation per court, date, and time slot) @ `v0.2` (`docs/c02-specification.md`), `ADR-01` (`docs/architecture-and-decisions.md`); directly addresses and closes Risk `R-02`.
- **Baseline commit, DB schema, runtime, data setup and command:** `tag C08`; EF Core SQLite schema with partial unique index `IX_Reservations_ExclusiveActiveSlot ON Reservations(CourtId, Date, TimeSlotId) WHERE State IN (1, 2)`; .NET 8.0; command: `dotnet test --filter TC_C08_02`.
- **Preconditions, roles and exact initial persisted state:**
  - Authenticated Player (`UserId = 1`).
  - Standard `CourtId = 1` (`Number = 1`, `IsActive = true`).
  - Persisted reservation `B1`: `CourtId = 1`, `Date = today + 3 days`, `TimeSlotId = 3` (`10:00–11:00`), `State = CONFIRMED` (`2`).
  - Persisted competing reservation `B2`: `CourtId = 1`, `Date = today + 3 days`, `TimeSlotId = 3` (`10:00–11:00`), `State = DRAFT` (`0` — allowed by partial index because `0 NOT IN (1, 2)`).
- **Test data including intervals and timezones:** `targetDate = DateOnly.FromDateTime(DateTime.Today.AddDays(3))`; both `B1` and `B2` target `CourtId = 1, TimeSlotId = 3` (`[10:00, 11:00)`).

| # | Action / input | Expected BEFORE execution | Actual AFTER execution | Evidence |
| :---: | :--- | :--- | :--- | :--- |
| 1 | Initialize isolated SQLite DB with `IX_Reservations_ExclusiveActiveSlot`, insert `B1` (`CONFIRMED`, Court 1, Slot 3) and competing `B2` (`DRAFT`, Court 1, Slot 3). | `B1` is `CONFIRMED` (`2`), `B2` is `DRAFT` (`0`); SQLite allows both because partial unique index filters `WHERE State IN (1, 2)`. | Both rows inserted into SQLite (`B1.Id = 1`, `B2.Id = 2`). | `ReservationManagementServiceTests.cs` (`TC_C08_02_ConflictingConfirmationOnSameCourtAndSlot_IsRejectedBySqliteConstraint`) |
| 2 | Independently verify initial persisted state before confirmation attempt. | Exactly 1 `CONFIRMED` reservation exists for `(CourtId=1, Date, TimeSlotId=3)`. | Confirmed prior to `ConfirmDraftAsync` call. | `ReservationManagementServiceTests.cs` |
| 3 | Execute `service.ConfirmDraftAsync(b2.Id, userId: 1)` to promote `B2` from `DRAFT` to `CONFIRMED`. | SQLite rejects the `UPDATE Reservations SET State = 2` due to `UNIQUE constraint failed: Reservations.CourtId, Reservations.Date, Reservations.TimeSlotId`; service catches `DbUpdateException` and returns `Success == false`. | Returned `(Success: false, Message: "Tento termín byl v mezičase obsazen.")`; zero emails sent. | `dotnet test` execution output (`PASS`) |
| 4 | Independently query SQLite via `.AsNoTracking().CountAsync(...)` after transaction failure. | `B1` remains `CONFIRMED`; `B2` is **not** `CONFIRMED` in SQLite; total `CONFIRMED` count for the slot is strictly `1`. | `confirmedCount == 1` in SQLite database after rejected commit. | `ReservationManagementServiceTests.cs` |
| 5 | Dispose connection and repeat test execution. | Reproducible rejection on every execution. | Verified repeatable via `dotnet test`. | `ReservationManagementServiceTests.cs` |

- **Final postcondition / independently read committed-state oracle:** Exactly 1 `CONFIRMED` reservation (`B1`) exists in SQLite for `(CourtId = 1, Date = today + 3 days, TimeSlotId = 3)`; `B2` did not reach `CONFIRMED` in persistent storage.
- **Verdict PASS / FAIL / BLOCKED / NOT RUN + supporting observation:** **`PASS`** — Real SQLite partial unique index `IX_Reservations_ExclusiveActiveSlot` triggered `DbUpdateException`, which `ReservationManagementService.ConfirmDraftAsync` caught and translated into a clean rejection tuple. Risk `R-02` is now **VERIFIED** and closed.
- **If FAIL / If BLOCKED:** N/A (test passed).
- **What remains untested / actual residual exposure:** New cancellation cutoff behavior (`CR-08` — 12h window) is `PLANNED` and designed in `docs/intent-and-change.md`, to be implemented and verified in **C09**.
