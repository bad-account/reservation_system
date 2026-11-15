$ErrorActionPreference = "Stop"
Write-Host "=== SWI C09 Automated Build & Verification ===" -ForegroundColor Cyan

dotnet restore reservation_system_for_padel/reservation_system_for_padel.sln
dotnet build reservation_system_for_padel/reservation_system_for_padel.sln -c Release --no-restore
dotnet test reservation_system_for_padel/reservation_system_for_padel.sln -c Release --no-build --verbosity normal

Write-Host "=== Verification Succeeded (Exit Code: $LASTEXITCODE) ===" -ForegroundColor Green
