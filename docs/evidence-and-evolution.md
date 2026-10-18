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

### C03 — Architecture Evidence

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

**Implementation changes:** These are proposed changes, not verified as implemented. Check the database provider and existing duplicates, add the constraint/migration, refactor lifecycle changes into the service, add supporting components and run tests.

**Behaviour verification:** Behaviour was tested

**Architecture conformance rule + result:** Rule — only `ReservationManagementService` may change an existing reservation's lifecycle state. Result — not yet verified; the current controller directly changes states, so refactoring and an automated conformance check are needed.

**Remaining uncertainty / risk:** Database-provider support, existing duplicate reservations, concurrency behaviour, background expiry and email/QR failure handling still need verification.

**Commit/tag:** tag C03
