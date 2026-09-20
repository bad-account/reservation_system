# Padel Ostrava – Rezervační systém

Webová aplikace v ASP.NET Core MVC (C#) s Entity Framework Core a SQLite pro rezervaci kurtů na padel.

## Požadavky
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) nebo novější
- Visual Studio 2022 / VS Code / Rider

## Instalace a první spuštění

1. **Klonování repozitáře:**
   ```bash
   git clone <URL_REPOZITARE>
   cd reservation_system_for_padel
   ```

2. **Obnova závislostí:**
   ```bash
   dotnet restore
   ```

3. **Příprava databáze:**
   Databáze SQLite (`padel.db`) se vytvoří a naplní výchozími kurty a časovými sloty automaticky při prvním spuštění díky `db.Database.Migrate()` / `EnsureCreated()`. Není nutný žádný externí databázový server.

4. **Konfigurace (volitelné – e-mailové notifikace):**
   Pro plnou funkčnost odesílání e-mailů nastavte lokální User Secrets:
  ```bash
   dotnet user-secrets init
   dotnet user-secrets set "SmtpSettings:Server" "smtp.seznam.cz"
   dotnet user-secrets set "SmtpSettings:Port" "465"
   dotnet user-secrets set "SmtpSettings:SenderEmail" "vas-email@seznam.cz"
   dotnet user-secrets set "SmtpSettings:Username" "vas-email@seznam.cz"
   dotnet user-secrets set "SmtpSettings:Password" "vase-heslo"
   ```
   *(Pokud hodnoty nenastavíte, aplikace poběží dál, chyby odeslání se zalogují do konzole bez pádu systému).*

5. **Sestavení a spuštění:**
   ```bash
   dotnet build
   dotnet run --project reservation_system_for_padel
   ```
   Aplikace je dostupná na adrese zobrazené v konzoli (obvykle `https://localhost:7xxx` nebo `http://localhost:5xxx`).


## CP1 Walking Skeleton

End-to-end cesta pro vytvoření a ověření rezervace kurtu:

1. **Request:**
   - **Metoda & Endpoint:** `POST /Reservation/BookSlot` (případně API endpoint `POST /api/reservations`)
   - **Payload / Form data:**
     - `courtId`: 1 (Kurt č. 1)
     - `timeSlotId`: 2 (např. 09:00–10:00)
     - `date`: "2026-10-01"
     - Autentizační cookie / identifikátor přihlášeného uživatele (`UserId`: 1)

2. **Validate (Doménová validace a business pravidla):**
   - Ověření, že vybraný termín není v minulosti.
   - Ověření, že uživatel nepřekročil limit 2 aktivních budoucích rezervací.
   - Ověření dostupnosti slotu (žádná existující aktivní/potvrzená rezervace pro shodný `CourtId`, `Date` a `TimeSlotId`).

3. **Persist (Uložení stavu):**
   - Vytvoření entity `Reservation` ve stavu `Confirmed` s časovým razítkem `CreatedAt`.
   - Zápis do SQLite databáze přes Entity Framework Core (`SaveChangesAsync()`).

4. **Return reservation ID (Odpověď):**
   - Návrat vygenerovaného unikátního `ReservationId` (v HTTP redirectu / JSON response s kódem 200/201).
   - Odeslání potvrzovací notifikace na pozadí.

5. **Automated check (Automatizované ověření):**
   - Integrační test / automatizovaný skript:
     - Pošle HTTP POST požadavek s definovaným payloadem.
     - Ověří HTTP status kód (úspěch) a přítomnost `ReservationId`.
     - Provede kontrolní dotaz do databáze (případně `GET /Reservation/MyReservations`), kde ověří, že rezervace se shodným ID v databázi skutečně existuje a je ve stavu `Confirmed`.
