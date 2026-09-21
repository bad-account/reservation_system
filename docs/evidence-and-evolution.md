# C01 Engineering Spike
- Question / unknown: Can a new team member perform a clean repository checkout and successfully build and run the project relying exclusively on the instructions in `README.md`, without the system crashing due to a missing local SQLite database or unconfigured SMTP credentials?  
- What we did: 
1. The second team member performed a clean clone of the repository into a fresh directory on his computer.
2. Attempted to compile and run the application using standard CLI commands (`dotnet build` and `dotnet run`).

- Observed result: During the initial run on the clean checkout, attempting to complete a reservation caused an email to not be sent due to missing configuration keys. The SQLite database schema was successfully created and seeded on application launch via `Database.Migrate()` / `EnsureCreated()`. After publishing the step-by-step setup guide in `README.md`, the clean environment build and run succeeded on the first attempt without errors.

- Decision / what changes because of the result:
For our development environment, we have updated the README with mandatory documentation policies, added information about secrets and configuration, and retained embedded SQLite with automatic schema bootstrapping alongside console fallbacks for external services.
