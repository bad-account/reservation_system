## OP-01 — Create Reservation

Goal / user value:
Signed in user creates a reservation draft that can be later confirmed or cancelled.

Trigger:
Signed in User creates a Reservation for a Court and Time slot.

Observable requirement:
REQ-01:
The system shall create a DRAFT Reservation for a one of three Courts
when the requested time slot is available.

Preconditions:
- User is signed in.
- Court exists.
- Time slot is available.
- start < end.

Success postcondition:
- one new Reservation exists;
- Reservation.state = DRAFT;
- no Resource allocation is committed yet.

State change:
[none] → DRAFT

Referenced rules:
BR-01 Interval semantics.

Main success scenario:
1. User submits Resource and interval.
2. System validates authorization, Resource and interval.
3. System creates Reservation in DRAFT.
4. System returns the Reservation identifier and current state.

Alternative / failure outcomes:
- unauthorized User → reject; no Reservation created;
- unknown Resource → reject; no Reservation created;
- invalid interval → reject; no Reservation created.

Verification examples:
valid Resource + [10:00,11:00) → one DRAFT created
start == end → rejected
unknown Resource → rejected

Rationale:
Creation records user intent without committing Resource allocation.


## OP-02 — Check Availability

Goal / user value:
User can determine whether an exclusive Resource is currently
available for a requested interval.

Observable requirement:
REQ-02:
For a valid interval, the system shall report a Resource as unavailable
if the interval overlaps any CONFIRMED Reservation of that Resource;
otherwise it shall report it as available.

Preconditions:
- Resource exists.
- requested interval is valid.

Success postcondition:
- availability result is returned;
- no Reservation state is changed.

Referenced rules:
BR-01 Interval semantics.
BR-02 Exclusive Resource invariant.

Verification examples:
Existing CONFIRMED: [10:00,11:00)

query [09:00,10:00) → AVAILABLE
query [10:30,11:30) → UNAVAILABLE
query [11:00,12:00) → AVAILABLE

Accepted semantics:
Intervals are half-open: [start,end).


## OP-03 — Confirm Reservation

Goal / user value:
A valid DRAFT Reservation becomes the accepted allocation of Resource.

Trigger:
Authorized User requests confirmation of Reservation X.

Observable requirements:
REQ-03:
The system shall confirm a DRAFT Reservation only when its Resource
is active and its interval does not conflict with an existing
CONFIRMED Reservation of the same exclusive Resource.

REQ-04:
For concurrent confirmation attempts that conflict under BR-02,
at most one Reservation shall reach CONFIRMED.

Preconditions:
- Reservation exists.
- Reservation.state = DRAFT.

Success postcondition:
- Reservation.state = CONFIRMED;
- the Reservation blocks its Resource for its interval;
- BR-02 remains true.

Failure outcomes:
- inactive Resource → reject; Reservation remains DRAFT;
- overlap exists → reject; Reservation remains DRAFT;
- invalid source state → reject; state unchanged.

Verification examples:
DRAFT + active Resource + no overlap → CONFIRMED
DRAFT + overlap → rejected, remains DRAFT
two concurrent conflicting confirmations → at most one CONFIRMED


## OP-04 — Cancel Reservation

Goal / user value:
An eligible Reservation can be withdrawn and stops blocking Resource.

Observable requirement — EXAMPLE POLICY:
REQ-05:
The system shall allow a DRAFT or CONFIRMED Reservation to be
cancelled before its start time.

Preconditions:
- Reservation exists.
- Reservation.state ∈ {DRAFT, CONFIRMED}.
- currentTime < Reservation.start.

Success postcondition:
- Reservation.state = CANCELLED;
- Reservation no longer blocks Resource availability.

Failure outcomes:
- already CANCELLED → [team decides: idempotent success OR explicit rejection];
- start time reached/passed → reject;
- Cancel races with Confirm → team defines accepted observable outcome.

Verification examples:
DRAFT before start → CANCELLED
CONFIRMED before start → CANCELLED and Resource becomes available
CONFIRMED at/after start → rejected according to accepted policy


BR-01 — Interval semantics
Reservation intervals use [start,end) semantics.

BR-02 — Exclusive Resource invariant
At no committed system state may two CONFIRMED Reservations overlap
for the same exclusive Resource.

BR-03 — Cancellation policy
[team-defined, domain-specific accepted policy]

BR-04 — Domain-specific rule from C01
[team-defined]
