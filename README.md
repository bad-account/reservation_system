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
