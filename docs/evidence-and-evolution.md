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
