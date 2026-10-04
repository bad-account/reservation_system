## OP-01 — Create Reservation

**Goal / user value:**
A signed-in user temporarily reserves a selected court for a specific hourly time slot. This creates a time-limited DRAFT that blocks the slot from other users until it is confirmed or cancelled.

**Trigger:**
A signed-in user submits a request to create a reservation for a specific court (CourtId), date (Date), and time slot (TimeSlotId).

**Observable requirement:**
REQ-01: The system shall create a reservation in the DRAFT state for an existing and active court and a valid future time slot only if the slot is not occupied by an existing reservation in the CONFIRMED state or by another user's active DRAFT, and the requester has not exceeded the active reservation limit.
REQ-01b (Draft TTL): A DRAFT shall remain valid for a maximum of 5 minutes from its creation. After expiration, it shall be automatically invalidated/removed by the system.

**Preconditions:**

* User is signed in.
* The Court exists and has `IsActive = true`.
* The selected date and slot are not in the past (`Date >= today`, for today `StartTime > currentTime`).
* The user has fewer than 2 active future reservations (rule BR-04).
* The slot is not blocked by another CONFIRMED reservation or another user's active DRAFT.

**Success postcondition:**

* Exactly one new Reservation record is created.
* `Reservation.State = DRAFT`.
* Creation time is set as `CreatedAt = UtcNow`.
* The reservation identifier and remaining expiration time (300 seconds) are returned.

**State change:**
`[none] → DRAFT`

**Referenced rules:**
BR-01 Interval semantics, BR-02 Exclusive Resource invariant, BR-04 Active reservation limit (Max 2), BR-05 Draft TTL policy.

**Main success scenario:**

1. The user selects an active court, date, and time slot.
2. The system checks authentication, date validity, and the user's limit (max. 2).
3. The system verifies that the slot is not occupied.
4. The system saves the reservation in the DRAFT state.
5. The system returns JSON containing `reservationId` and a 300-second countdown.

**Alternative / failure outcomes:**

* Unsigned user → redirected to Login (401 Unauthorized).
* Date/time slot is in the past → rejected with an error message.
* User already has 2 active reservations → rejected with a limit error.
* Slot is already confirmed (CONFIRMED) → rejected because the slot is occupied.
* Slot is currently held in a DRAFT by another user → rejected with a temporary blocking message.
* User already has their own DRAFT for this slot → the existing `draftId` and remaining seconds are returned.

**Verification examples:**
Court 1 + tomorrow 10:00–11:00 + user with 0 reservations → DRAFT created (TTL 300 s).
Court 1 + today 08:00–09:00 (at 15:00) → rejected (past).
User with 2 existing active games → rejected (limit reached).

**Rationale:**
The two-phase reservation process using DRAFT prevents conflicts caused by simultaneous user clicks and gives the player time to review the reservation before finally confirming the slot.

## OP-02 — Check Availability

**Goal / user value:**
A visitor or signed-in user can see a clear daily matrix of courts and time slots (08:00–22:00) showing available, occupied by another user, and reserved by the current user slots.

**Observable requirement:**
REQ-02: For the specified date, the system shall display all active courts and valid future time slots. A slot shall be marked as unavailable (Occupied / My reservation) if there is a reservation in the CONFIRMED state or a valid DRAFT for it; otherwise, it shall be offered as available for reservation.

**Preconditions:**

* The requested date is valid.
* Defined courts and time slots exist in the system.

**Success postcondition:**

* A list of courts, time slots, and existing reservations for the selected day is returned.
* The expiration service (`CleanupExpiredDraftsAsync`) removes expired DRAFTs older than 5 minutes.
* No reservation state is changed.

**Referenced rules:**
BR-01 Interval semantics, BR-02 Exclusive Resource invariant, BR-05 Draft TTL policy.

**Verification examples:**
Court 2 has a CONFIRMED reservation tomorrow from 14:00–15:00 → the slot is displayed as occupied.
Court 2 tomorrow from 15:00–16:00 has no reservation → the slot is green (Reserve button).
Court 1 has an active DRAFT that is 6 minutes old → when the page is loaded, the DRAFT is removed and the slot is displayed as available.

**Accepted semantics:**
Intervals are half-open: `[start,end)`.

## OP-03 — Confirm Reservation

**Goal / user value:**
The user definitively confirms their DRAFT. This gives them a guaranteed court, generates a unique access QR code for them, and sends a confirmation email.

**Trigger:**
The reservation owner submits a confirmation request (`POST /Reservation/ConfirmDraft`).

**Observable requirements:**
REQ-03: The system shall confirm only a reservation that exists, belongs to the signed-in user, is in the DRAFT state, and whose 5-minute limit has not expired.
REQ-03b (Notifications and QR): Upon successful confirmation, the system shall generate an access QR code and send it to the user's contact email together with a summary.
REQ-04: In case of concurrent requests, at most one reservation for a given court and time slot may reach the CONFIRMED state.

**Preconditions:**

* The reservation exists in the database.
* `Reservation.state = DRAFT`.
* `Reservation.UserId` matches the signed-in user.
* The DRAFT has not exceeded its 5-minute limit.

**Success postcondition:**

* `Reservation.State = CONFIRMED`.
* The slot is definitively blocked for other users.
* A notification email containing the QR code is sent.
* The system sets `TempData["SuccessMessage"]` and redirects back to the calendar.

**Failure outcomes:**

* Reservation does not exist or its TTL has expired → rejected (Draft reservation expired).
* Reservation belongs to another user → rejected.
* Reservation is in a state other than DRAFT → rejected.

**Verification examples:**
User's active DRAFT within 5 minutes → transitions to CONFIRMED, email sent.
DRAFT confirmed 6 minutes after creation → deleted, expiration message displayed.
Attempt to confirm another user's DRAFT → rejected.

## OP-04 — Cancel Reservation

**Goal / user value:**
A user can cancel a previously created valid reservation. The court is immediately released for other players, the user's capacity under the 2-reservation limit is freed, and a cancellation email is sent to them.

**Observable requirement — EXAMPLE POLICY:**
REQ-05: The system shall allow a reservation in the CONFIRMED state to be cancelled if the requester is its owner or a user with the Admin role.
REQ-05b: After cancellation, the reservation shall transition to the CANCELED state, the slot shall no longer block court availability, and the user shall receive an informational cancellation email.

**Preconditions:**

* The reservation exists.
* The signed-in user is the reservation owner (`UserId == Reservation.UserId`) OR has the Admin role.
* `Reservation.State == CONFIRMED` (with the possible addition of cancellation of DRAFT reservations via CancelDraft).

**Success postcondition:**

* `Reservation.State = CANCELED` (or the DRAFT is physically deleted).
* The slot immediately becomes available for new reservations (the index and query filter out CANCELED reservations).
* The user once again has capacity for a new game.
* A cancellation email is sent.

**Failure outcomes:**

* Reservation not found → error displayed.
* Regular user attempts to cancel another user's reservation → rejected (You do not have permission to cancel another user's reservation).

**Verification examples:**
A player cancels their active reservation for tomorrow → the state changes to CANCELED, the slot turns green on the website, and it can immediately be reserved again.
Player A sends a request to cancel Player B's reservation → rejected with a permission error.
An Admin cancels Player B's reservation → successfully cancelled.

## OP-05 — Approve / Reject Reservation

**Goal / user value:**  
The facility administrator can review a reservation request for a premium court and decide whether to definitively confirm or reject it.

**Trigger:**  
An authorized administrator submits a request to approve or reject a reservation in the `PENDING_APPROVAL` state.

**Observable requirement:**  
- **REQ-06:** The system shall allow changing a reservation state from `PENDING_APPROVAL` to CONFIRMED (upon approval) or `REJECTED` (upon rejection) only for a signed-in user with the Admin role.
- **REQ-06b:** Upon transition to `CONFIRMED`, the system shall generate an access QR code and send an approval email. Upon transition to `REJECTED`, it shall send a cancellation notification and immediately release the timeslot.

**Preconditions:**
- The user has the `Admin` role.
- The reservation exists and is in the `PENDING_APPROVAL` state.

**Success postcondition:**
- `Reservation.State = CONFIRMED` or `Reservation.State = REJECTED`.
- In case of `CONFIRMED` state, an access QR code is generated and the slot blocking remains active.
- In case of `REJECTED` state, the slot stops blocking availability for other users.
- An email is sent to the requester.

**State change:**  
`PENDING_APPROVAL → CONFIRMED`  
`PENDING_APPROVAL → REJECTED`

**Verification examples:**
- Admin approves a `PENDING_APPROVAL` reservation for Court 1 → state changes to `CONFIRMED`, an email with a QR code is sent.
- Regular player sends an approval request → `403 Forbidden`.
- Admin rejects a reservation → state changes to `REJECTED`, the court immediately shows as available in the matrix.

## BR-01 — Interval semantics

Time slots are fixed blocks with a duration of 60 minutes between 08:00 and 22:00. They use half-open intervals `[StartTime, EndTime)`. The end of the previous slot corresponds to the beginning of the next slot (e.g. `[08:00, 09:00)` and `[09:00, 10:00)` do not overlap).

## BR-02 — Exclusive Resource invariant

A single court (`CourtId`) may have at most one reservation in the CONFIRMED state for a given day (`Date`) and time slot (`TimeSlotId`). Cancelled (`CANCELED`) and rejected (`REJECTED`) reservations do not block availability.

## BR-03 — Cancellation policy

A CONFIRMED reservation may only be cancelled by the user who created it or by an administrator. Once cancelled, the slot immediately becomes available again for the club, and the original access QR code becomes invalid.

## BR-04 — Active reservation limit (Domain-specific rule from C01)

A single user may have at most 2 active future reservations in the system at any given time (both DRAFT and CONFIRMED states are counted for today's and future dates). A third reservation cannot be started until one of the previous reservations has taken place (expired) or has been cancelled.

## BR-05 — Draft TTL policy

A preliminary reservation in the DRAFT state protects the selected slot for a maximum of 5 minutes (300 seconds) from its creation (`CreatedAt`). If the user does not submit a confirmation within this period, the DRAFT is automatically removed and the slot is released back to the available pool.





## Impact of Change C02

**Changed condition:**  
Reservations for selected court capacities (Court No. 4 – tournament court) are subject to manual approval by the facility administrator before becoming definitively confirmed.

**Affected requirements / specification parts:**
- `OP-02 Check Availability`: Blocking rule extended – slot is also blocked by the `PENDING_APPROVAL` state.
- `OP-03 Confirm Reservation`: If the reservation belongs to Court No. 4, upon confirmation from draft it does not transition to `CONFIRMED` state, but rather to `PENDING_APPROVAL` state.
- `OP-04 Cancel Reservation`: Cancellation enabled for the `PENDING_APPROVAL` state.
- A new operation `OP-05 Approve / Reject Reservation` is introduced.

**Unaffected requirements / parts + rationale:**
- `OP-01 Create Reservation`: Remains unchanged, creates a temporary 5-minute `DRAFT` regardless of the court type.
- Regular courts (Courts No. 1, 2, and 3) retain direct confirmation into `CONFIRMED`.

**New actor / operation, if created:**
- `Facility Administrator / Administrator (Admin)` – a person authorized to review and decide on requests in the `PENDING_APPROVAL` state.
- `OP-05 Approve / Reject Reservation` – an operation allowing the administrator to transition a reservation from `PENDING_APPROVAL` to `CONFIRMED` (approval) or `REJECTED` (rejection).

**Changed rules / state meanings:**
- `PENDING_APPROVAL`: New transitional state. Indicates that the player confirmed their draft for Court No. 4 and is awaiting the administrator's decision. According to rule BR-02, the time slot exclusively blocks court capacity and counts towards the player's limit of 2 active reservations (BR-04).
- `REJECTED`: New terminal state. Occurs upon rejection by the administrator. The court is immediately released for others, and capacity is returned to the player's 2-reservation limit.
- Modification of BR-02 (Exclusive Resource invariant): For a given court, date, and slot, there must not exist more than one reservation in the `CONFIRMED` or `PENDING_APPROVAL` state within a valid system state.

**Use case diagram changes:**
- Primary actor `Facility Administrator / Administrator` is added to the diagram.
- New use case `OP-05: Approve / reject reservation` is added. This use case is associated exclusively with the `Administrator`.
- Use case `OP-04: Cancel reservation` is now available to both `Player` and `Administrator`.

**State diagram changes:**
- From the `DRAFT` node, the transition branches upon player confirmation: Regular courts (1, 2, 3) $\rightarrow$ `CONFIRMED`, tournament court (4) $\rightarrow$ `PENDING_APPROVAL`.
- From the `PENDING_APPROVAL` state, there are now 3 possible outgoing transitions: $\rightarrow$ `CONFIRMED` (via `OP-05 Approve`), $\rightarrow$ `REJECTED` (via `OP-05 Reject`), $\rightarrow$ `CANCELED` (via `OP-04 Cancel` by player or admin).

**New verification examples:**
- User confirms draft for Court No. 4, reservation transitions to `PENDING_APPROVAL`. Admin approves, state changes to `CONFIRMED`, QR code is generated, and confirmation email is sent.
- Court No. 4 is in the `PENDING_APPROVAL` state for the 16:00–17:00 slot; for all other users, the slot is displayed as occupied and cannot be clicked.
- Admin rejects a reservation for Court No. 4, state transitions to `REJECTED`, the slot in the calendar is immediately released and turns green for other players.

**Architectural drivers for C03:**
- Reservations in the `PENDING_APPROVAL` state must not block the court indefinitely. If the administrator does not decide in time (e.g., up to 24 hours before game start), the system requires a scheduled job to expire the reservation into the `EXPIRED` state and release the court.
