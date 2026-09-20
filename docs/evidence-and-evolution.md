# C01 Engineering Spike
Question / unknown: Can a new team member perform a clean repository checkout and successfully build and run the project relying exclusively on the instructions in `README.md`, without the system crashing due to a missing local SQLite database or unconfigured SMTP credentials?
What we did: 
1. The second team member performed a clean clone of the repository into a fresh directory on his computer.
2. Attempted to compile and run the application using standard CLI commands (`dotnet build` and `dotnet run`).
3. Identified and evaluated points of failure:
   - Evaluated application runtime behavior when the `SmtpSettings` section is absent from `appsettings.json` (email credentials reside strictly in local User Secrets).
   - Verified whether the local SQLite database (`padel.db`) and seed data are generated properly on startup without requiring manual EF Core CLI steps.
4. Documented the complete environment setup process in `README.md`, covering system prerequisites (.NET 8 SDK) and optional configuration via `dotnet user-secrets`.
Observed result: During the initial run on the clean checkout, attempting to complete a reservation caused an email to not be sent due to missing configuration keys. The SQLite database schema was successfully created and seeded on application launch via `Database.Migrate()` / `EnsureCreated()`. After publishing the step-by-step setup guide in `README.md`, the clean environment build and run succeeded on the first attempt without errors.
Decision / what changes because of the result:
1. **External Services Configuration Standard:** All third-party integration services (SMTP now, payment gateways in future iterations) must provide a safe local development fallback: if credentials are missing, the service logs the payload to the console instead of interrupting core business transactions.
2. **README Documentation Policy:** Any new library or architectural dependency requiring external configuration must be documented in `README.md` as a mandatory checklist item during Pull Request reviews.
3. **Retaining Embedded SQLite for Development:** Confirmed the decision to use embedded SQLite with automatic schema bootstrapping for local development, removing the requirement to run external database servers or Docker containers for onboarding.
