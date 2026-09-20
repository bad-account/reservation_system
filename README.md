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

1. **Request:**
   - **Method & Endpoint:** `POST /Reservation/BookSlot` (or API endpoint `POST /api/reservations`)
   - **Payload / Form data:**
     - `courtId`: 1 (Court No. 1)
     - `timeSlotId`: 2 (e.g., 09:00–10:00)
     - `date`: "2026-10-01"
     - Authentication cookie / logged-in user identifier (`UserId`: 1)

2. **Validate (Domain validation and business rules):**
   - Check that the selected date/time is not in the past.
   - Check that the user has not exceeded the limit of 2 active future reservations.
   - Check slot availability (no existing active/confirmed reservation for the same `CourtId`, `Date`, and `TimeSlotId`).

3. **Persist (State persistence):**
   - Create a `Reservation` entity in the `Confirmed` state with a `CreatedAt` timestamp.
   - Save to the SQLite database via Entity Framework Core (`SaveChangesAsync()`).

4. **Return reservation ID (Response):**
   - Return the generated unique `ReservationId` (via HTTP redirect / JSON response with HTTP 200/201).
   - Send a confirmation notification in the background.

5. **Automated check:**
   - Integration test / automated script:
     - Sends an HTTP POST request with the defined payload.
     - Verifies the HTTP status code (success) and presence of the `ReservationId`.
     - Executes a verification query against the database (or calls `GET /Reservation/MyReservations`) to confirm the reservation with the matching ID actually exists and is in the `Confirmed` state.
