# C01
## Selected future pressure
- Category: C  
- Concrete pressure: Adding support for partial/open reservations (new status: WaitingForPlayers) allowing other users to join an existing reservation, alongside an add-on service for equipment rental.  
- Why it is relevant to our reservation system: Requires extending the core domain logic, state machine, and pricing calculations.

# C03/A — AS-IS Trace

## A1. Scenario reference

- The selected scenario traces how an administrator approves a reservation that is currently in the PENDING_APPROVAL state. The expected result is a transition to CONFIRMED, generation of an access QR code, and an approval email to the user.

| Item | Value |
| -------- | ------- |
| Scenario / operation | OP-05 — Approve / Reject Reservation |
| Requirements | REQ-06, REQ-06b |
| Rules / invariants | BR-02 — Exclusive Resource invariant |
| Baseline | v0.2 |
| Main implementation entry point | ReservationController.Approve(int reservationId)

## A2. Main success path mapped to code

- Scenario: An administrator approves a reservation in the PENDING_APPROVAL state.

| Scenario step | Implementation location | Evidence
| -------- | ------- | ------- |
| 1. The administrator opens the list of pending requests. | `ReservationController.PendingApprovals()` loads reservations in PendingApproval state and returns the view. | ReservationController.cs, lines 316–330. |
| 2. The administrator submits the approval form. | `PendingApprovals.cshtml` contains a POST form targeting the Approve action and sends the reservation ID. | PendingApprovals.cshtml, lines 57–62. | 
| 3. The system checks administrator authorization. | The `[Authorize(Roles = "Admin")]` attribute protects the Approve action. The role is included in the authentication claims by `AccountController.SignInUser()`. | ReservationController.cs, lines 332–336; AccountController.cs, lines 91–110. |
| 4. The system loads the reservation and its related data. | `Approve() queries _context.Reservations`, includes User, Court, and TimeSlot, and searches by reservation ID. | ReservationController.cs, lines 338–342. |
| 5. The system verifies that the reservation exists and is awaiting approval. | The action checks whether the reservation is null or its state is not PendingApproval. Invalid requests receive an error message and are redirected to the pending-approval list. | ReservationController.cs, lines 344–348. |
| 6. The reservation state changes to CONFIRMED and is persisted. | The action assigns `ReservationState.Confirmed` and calls `SaveChangesAsync()`. | ReservationController.cs, lines 350–351. |
| 7. The system generates an access QR code. | Approve() builds the QR payload and uses `QRCodeGenerator` and `PngByteQRCode` to create PNG bytes. | ReservationController.cs, lines 353–357. |
| 8. The system sends the approval email, if the user has an email address. | `Approve()` calls `_emailSender.SendEmailAsync(...)`. The EmailSender implementation creates the email and sends it through SMTP, embedding the QR code when supplied. | ReservationController.cs, lines 359–376; IEmailSender.cs, lines 3–6; EmailSender.cs, lines 19–60. |
| 9. The system reports the result. | The action sets `TempData["SuccessMessage"]` and redirects to PendingApprovals. | ReservationController.cs, lines 378–379. |


## A3. Alternative / failure path

- Scenario: A regular user attempts to approve a reservation.

- **v0.2 behaviour**: A non-admin user must not be allowed to approve a reservation (REQ-06).
- **Where condition is detected**: ASP.NET Core authorization evaluates the `[Authorize(Roles = "Admin")]` attribute on the Approve action.
- **Where outcome is decided**: The authorization middleware/filter prevents the action from executing when the caller does not satisfy the role requirement.
- **What caller receives**: The request is denied by the configured authentication/authorization handling. The exact HTTP response or redirect depends on the application authentication configuration, which was not included in the supplied files.

## A4. Main implementation elements

| Implementation element | Type / contents | Role in this scenario | Evidence |
| -------- | ------- | -------- | ------- |
| PendingApprovals.cshtml | Razor view | Displays pending requests and provides the administrator with Approve and Reject forms. | PendingApprovals.cshtml, lines 28–75. |
| `ReservationController.Approve()` | MVC controller action | Authorizes the operation through an attribute, validates the current state, changes the state, generates the QR code, requests email delivery, and redirects. | ReservationController.cs, lines 332–379. |
| `Reservation` and `ReservationState` | Entity and enum | Represent the reservation data and its lifecycle states, including PendingApproval and Confirmed. | Reservation.cs, lines 3–28. |
| `AppDbContext` | Entity Framework Core DbContext | Provides access to reservation entities and persists state changes through SaveChangesAsync(). | AppDbContext.cs, lines 6–22; ReservationController.cs, lines 338–351. |
| `IEmailSender` and `EmailSender` | Email abstraction and SMTP implementation | Separate the controller`s notification request from the technical details of composing and sending email. | IEmailSender.cs, lines 3–6; EmailSender.cs, lines 8–67. |
| `QRCodeGenerator` and `PngByteQRCode` | QR-code generation library | Generate the PNG representation of the access QR code after approval. | ReservationController.cs, lines 353–357. |
| `AccountController.SignInUser()` | Authentication setup | Adds the user`s role to the claims used by role-based authorization. | AccountController.cs, lines 91–110. |

## A5. State, state change and business rule

### State
| Question | Answer | Evidence |
| -------- | ------- | -------- | 
| Where is reservation state represented? | In `Reservation.State`, which uses the `ReservationState` enum. The enum includes Draft, PendingApproval, Confirmed, Rejected, Canceled, and Expired. | Reservation.cs, lines 3–10 and 27–28.
| Where is reservation state persisted? | The reservation is tracked through `AppDbContext.Reservations`. The approval action calls `SaveChangesAsync()` after changing the state, persisting the update through Entity Framework Core. The database provider and connection configuration were not included in the supplied files. | AppDbContext.cs, lines 6–13; ReservationController.cs, lines 350–351. |
| Which code decides and executes the state transition? | `ReservationController.Approve()` verifies that the current state is PendingApproval, then assigns `ReservationState.Confirmed` and saves the change. | ReservationController.cs, lines 344–351. |

### Business rule / invariant — BR-02
| Question | Answer | Evidence |
| -------- | ------- | -------- | 
|¨Where is the condition for the rule checked? | `CreateDraft()` checks whether the same court, date, and time slot already has a Confirmed or PendingApproval reservation. It rejects a new draft if either state already occupies the slot. | ReservationController.cs, lines 137–147.
| Where is the outcome decided? | `CreateDraft()` returns a JSON failure when it finds an existing blocking reservation. The calendar also loads reservations other than Canceled and Rejected, so pending requests remain visible as occupied. | ReservationController.cs, lines 76–82 and 137–147; Index.cshtml, lines 153–205. |
| Where is the resulting state change performed? | `Approve()` changes the reservation from PendingApproval to Confirmed and persists it. |ReservationController.cs, lines 344–351. |
| Is the invariant fully guaranteed under concurrent requests? | The supplied code does not prove that it is. The conflict check occurs in application code, and `AppDbContext` defines a non-unique index on (CourtId, Date, TimeSlotId). The approval action does not repeat a conflict check, and no transaction/concurrency protection or unique database constraint is shown in the supplied files. | ReservationController.cs, lines 137–147 and 338–351; AppDbContext.cs, lines 19–22. |

## A6. Relevant dependencies

| Dependency | Where it connects to the application | Which component knows its technical API | Evidence |
| -------- | ------- | -------- | -------- | 
| Database / persistence | `ReservationController` uses `AppDbContext` to query and update reservations. | AppDbContext uses Entity Framework Core; the actual provider and database configuration are not included in the supplied files. | AppDbContext.cs, lines 1–22; ReservationController.cs, lines 338–351. |
| QR-code generation | The approval action creates QR-code PNG bytes after the state change. | `ReservationController.Approve()` directly uses the QRCoder API. | ReservationController.cs, lines 353–357. |
| Email / notification service | The controller sends the recipient, subject, HTML body, and optional QR-code bytes to _emailSender. | IEmailSender defines the abstraction; EmailSender uses MailKit and MimeKit to compose and send the email through the configured SMTP server.| IEmailSender.cs, lines 3–6; EmailSender.cs, lines 19–60. |
| Authentication and role authorization | The controller action requires the Admin role. | `AccountController.SignInUser()` creates claims, including `ClaimTypes.Role`; ASP.NET Core authorization evaluates the role requirement. | AccountController.cs, lines 91–110; ReservationController.cs, lines 332–336. | 

- The supplied files do not include the application`s dependency-injection and authentication configuration, so the registration of these services and the exact database provider could not be independently verified.

## A7. AS-IS structural diagram

- The diagram below describes the implementation that exists in the supplied source files. It is an AS-IS view, not a proposed redesign.
```mermaid
flowchart TD
    Admin[Administrator / Browser]

    subgraph App["Padel Reservation Application"]
        View["PendingApprovals.cshtml"]
        Controller["ReservationController<br/>PendingApprovals() / Approve()"]
        Context["AppDbContext"]
        Entity["Reservation / ReservationState"]
        QR["QRCoder<br/>QRCodeGenerator / PngByteQRCode"]
        Interface["IEmailSender"]
        Sender["EmailSender"]
        Auth["AccountController<br/>role claim creation"]
    end

    DB[(Database<br/>provider not verified)]
    SMTP[External SMTP server]

    Admin --> View
    View -->|"POST Approve + reservationId"| Controller
    Auth -.->|"Admin role claim"| Controller
    Controller --> Context
    Context --> Entity
    Context <--> DB
    Controller -->|"Generate QR PNG"| QR
    Controller -->|"SendEmailAsync()"| Interface
    Interface --> Sender
    Sender -->|"SMTP"| SMTP
 ```

  - The controller coordinates the workflow and directly knows about the persistence context and QR-code library. Email delivery is accessed through IEmailSender, which keeps the SMTP implementation behind an interface. Persistence is accessed through Entity Framework Core. The database provider configuration was not part of the supplied files.


## A8. Architecture question

| Item | Content | 
| -------- | ------- |
| Question | How should the application enforce BR-02 atomically so that concurrent reservation requests cannot create two confirmed reservations for the same court, date, and time slot? |
| Evidence | `CreateDraft()` performs an application-level conflict check (ReservationController.cs, lines 137–147), while `AppDbContext` declares a non-unique index on (CourtId, Date, TimeSlotId) (AppDbContext.cs, lines 19–22). `Approve()` changes the state without repeating the conflict check (ReservationController.cs, lines 338–351). | 
| Why it matters | Two concurrent requests may pass application-level checks before either change becomes visible to the other. Without appropriate database constraints or transaction/concurrency handling, the implementation does not demonstrate that the exclusive-resource invariant is guaranteed in every execution. This can lead to double-booking, which violates a core business rule. |




