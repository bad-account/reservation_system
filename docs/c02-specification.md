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
Správce sportoviště může posoudit žádost o rezervaci prémiového kurtu a rozhodnout o jejím závazném potvrzení nebo zamítnutí.

**Trigger:**  
Oprávněný administrátor odešle požadavek na schválení či zamítnutí rezervace ve stavu `PENDING_APPROVAL`.

**Observable requirement:**  
- **REQ-06:** Systém umožní přepnout rezervaci ze stavu `PENDING_APPROVAL` do stavu `CONFIRMED` (při schválení) nebo `REJECTED` (při zamítnutí) pouze přihlášenému uživateli s rolí `Admin`.
- **REQ-06b:** Při přechodu do `CONFIRMED` systém vygeneruje vstupní QR kód a odešle schvalovací e-mail. Při přechodu do `REJECTED` odešle storno notifikaci a termín okamžitě uvolní.

**Preconditions:**
- Uživatel má roli `Admin`.
- Rezervace existuje a je ve stavu `PENDING_APPROVAL`.

**Success postcondition:**
- `Reservation.State = CONFIRMED` NEBO `Reservation.State = REJECTED`.
- V případě `CONFIRMED` vzniká přístupový QR kód a blokace slotu trvá.
- V případě `REJECTED` slot přestává blokovat dostupnost pro ostatní.
- Odeslán e-mail žadateli.

**State change:**  
`PENDING_APPROVAL → CONFIRMED`  
`PENDING_APPROVAL → REJECTED`

**Verification examples:**
- Admin schválí `PENDING_APPROVAL` rezervaci na Kurt 1 → stav je `CONFIRMED`, odeslán e-mail s QR kódem.
- Běžný hráč pošle požadavek na schválení → `403 Forbidden`.
- Admin zamítne rezervaci → stav `REJECTED`, kurt se v matici okamžitě zobrazí jako volný.

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





## Dopad změny C02

**Změněná podmínka:**  
Rezervace na vybrané kurtové kapacity (Kurt č. 4 – turnajový kurt) podléhají manuálnímu schválení správcem areálu předtím, než se stanou závazně potvrzenými.

**Dotčené požadavky / části specifikace:**
- `OP-02 Check Availability`: Rozšířeno pravidlo blokování – slot je blokován i stavem `PENDING_APPROVAL`.
- `OP-03 Confirm Reservation`: Pokud rezervace náleží ke Kurtu č. 4, po potvrzení z draftu nepřechází do `CONFIRMED`, nýbrž do `PENDING_APPROVAL`.
- `OP-04 Cancel Reservation`: Umožněno zrušení i pro stav `PENDING_APPROVAL`.
- Vzniká nová operace `OP-05 Approve / Reject Reservation`.

Nedotčené požadavky / části + proč:
- `OP-01 Create Reservation`: Zůstává beze změny, vytváří dočasný 5minutový `DRAFT` bez ohledu na typ kurtu.
- Běžné kurty (Kurt č. 1, 2 a 3) si zachovávají přímé potvrzení do `CONFIRMED`.

Nový aktér / operace, pokud vznikne:
- `Správce areálu / Administrátor (Admin)` – osoba s oprávněním posuzovat a rozhodovat o žádostech ve stavu `PENDING_APPROVAL`.
- `OP-05 Approve / Reject Reservation` – operace umožňující správci převést rezervaci ze stavu `PENDING_APPROVAL` do `CONFIRMED` (schválení) nebo `REJECTED` (zamítnutí).

Změněná pravidla / význam stavů:
- `PENDING_APPROVAL`: Nový přechodový stav. Vyjadřuje, že hráč potvrdil svůj draft na Kurt č. 4 a čeká na rozhodnutí správce. Podle pravidla BR-02 termín exkluzivně blokuje a započítává se do limitu 2 aktivních rezervací hráče (BR-04).
- `REJECTED`: Nový koncový stav. Nastává při zamítnutí správcem. Kurt se okamžitě uvolní pro ostatní a hráči se vrátí kapacita do limitu 2 rezervací.
- Úprava BR-02 (Exclusive Resource invariant): Pro daný kurt, datum a slot nesmí v platném stavu systému existovat více než jedna rezervace ve stavu `CONFIRMED` nebo `PENDING_APPROVAL`.

Změna diagramu případů užití: 
- Do diagramu přibývá primární aktér `Správce / Administrátor`.
- Přibývá nový use case `OP-05: Schválit / zamítnout rezervaci`, na který je napojen výhradně Správce.
- Případ užití `OP-04: Zrušit rezervaci` je nyní dostupný jak pro `Hráče`, tak pro `Správce`.

Změna stavového diagramu:
- Z uzlu `DRAFT` se větví přechod při potvrzení hráčem: Běžné kurty (1, 2, 3) $\rightarrow$ CONFIRMED, turnajový kurt (4) $\rightarrow$ PENDING_APPROVAL.
- Ze stavu `PENDING_APPROVAL` nově vedou 3 možné přechody: $\rightarrow$ `CONFIRMED` (přes `OP-05 Approve`), $\rightarrow$ `REJECTED` (přes `OP-05 Reject`), $\rightarrow$ `CANCELED` (přes `OP-04 Cancel` hráčem nebo správcem)

Nové příklady ověření:
- Uživatel potvrdí draft na Kurt č. 4, rezervace přejde do stavu `PENDING_APPROVAL`. Admin provede schválení, stav se změní na `CONFIRMED`, vygeneruje se QR kód a odejde potvrzovací e-mail.
- Kurt č. 4 je ve stavu `PENDING_APPROVAL` na termín 16:00–17:00, pro všechny ostatní uživatele se slot zobrazuje jako obsazený a nelze na něj kliknout.
- Admin zamítne rezervaci na Kurt č. 4, stav přejde do `REJECTED`, slot se v kalendáři okamžitě uvolní a zezelená pro ostatní hráče.

Architektonické drivery pro C03:
- Rezervace ve stavu `PENDING_APPROVAL` nesmí blokovat kurt donekonečna. Pokud správce nerozhodne včas (např. do 24 h před začátkem hry), systém potřebuje naplánovanou úlohu, která rezervaci expurkuje do stavu `EXPIRED` a uvolní kurt.
