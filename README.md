# Padel Ostrava – Reservation System

A web application built with ASP.NET Core MVC (C#), Entity Framework Core, and SQLite for booking padel courts.

## Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or newer
- Visual Studio 2022 / VS Code / Rider

## Installation and First Run

1. **Clone the repository:**
   ```bash
   git clone <REPOSITORY_URL>
   cd reservation_system_for_padel
   ```

2. **Restore dependencies:**
   ```bash
   dotnet restore
   ```

3. **Database setup:**
   The SQLite database (`padel.db`) is automatically created and populated with default courts and time slots upon the first launch using `db.Database.Migrate()` / `EnsureCreated()`. No external database server is required.

4. **Configuration (optional – email notifications):**
   To enable full email sending functionality, configure local User Secrets:
   ```bash
   dotnet user-secrets init
   dotnet user-secrets set "SmtpSettings:Server" "smtp.seznam.cz"
   dotnet user-secrets set "SmtpSettings:Port" "465"
   dotnet user-secrets set "SmtpSettings:SenderEmail" "your-email@seznam.cz"
   dotnet user-secrets set "SmtpSettings:Username" "your-email@seznam.cz"
   dotnet user-secrets set "SmtpSettings:Password" "your-password"
   ```
   *(If values are not set, the application will continue to run normally, and email delivery errors will be logged to the console without crashing the system).*

5. **Build and run:**
   ```bash
   dotnet build
   dotnet run --project reservation_system_for_padel
   ```
   The application will be accessible at the URL displayed in the console (typically `https://localhost:7xxx` or `http://localhost:5xxx`).


## CP1 Walking Skeleton

End-to-end flow for creating and verifying a court reservation:

1. **Request (Two-step reservation flow):**
   - **Step 1 – Hold slot:** Request `POST /Reservation/CreateDraft` with `courtId`, `timeSlotId`, and `date`. The system creates a temporary `Draft` reservation with a 5-minute countdown.
   - **Step 2 – Finalize:** Submitting the modal form executes `POST /Reservation/ConfirmDraft`

2. **Validate (Domain validation and business rules):**
   - Prevents selecting dates or time slots in the past.
   - Enforces a business limit of maximum **2 active reservations** (`Draft` or `Confirmed`) per user.
   - Verifies slot availability to prevent double-booking (`Confirmed` state check).
   - Automatically cleans up expired `Draft` records older than 5 minutes (`CleanupExpiredDraftsAsync`).

3. **Persist (State persistence):**
   - Updates the `Reservation` entity state from `Draft` to `Confirmed`.
   - Saves changes to the SQLite database via Entity Framework Core (`SaveChangesAsync()`).

4. **Return & Notification (Response):**
   - Generates a QR code containing reservation payload data.
   - Sends a confirmation email with details and the attached QR code image via `IEmailSender`.
   - Redirects to `/Reservation` with a success message stored in `TempData["SuccessMessage"]`.

5. **Automated check:**
   - Integration test / verification flow:
     - Sends `POST /Reservation/CreateDraft` and receives a JSON response containing `reservationId`.
     - Sends `POST /Reservation/ConfirmDraft` with the returned `reservationId`.
     - Queries the SQLite database or calls `GET /Reservation/MyReservations` to confirm the reservation state is set to `Confirmed`.
