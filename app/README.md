# Expovice

Home-services marketplace. One account can **book** nearby pros (electrician, plumber, carpenter, AC, fridge, washing machine, dog walking, painter) and **list itself** as a pro.
Built from the Claude Design handoff in `../project/Expovice App.dc.html`.

| Layer | Tech | Folder |
|---|---|---|
| Mobile app (iOS + Android) | .NET MAUI 10, MVVM (CommunityToolkit.Mvvm) | `src/Expovice.App` (XAML, platform code) + `src/Expovice.App.Core` (view models, API client — plain `net10.0`, unit-tested) |
| Shared contract | DTOs, routes, service catalog, limits | `src/Expovice.Contracts` |
| API | ASP.NET Core 10 Minimal APIs, EF Core 10 | `src/Expovice.Api` |
| Cloud | App Service, Azure SQL (free offer), Blob Storage, Key Vault, Communication Services (SMS), App Insights | `infra/main.bicep` |
| Tests | xUnit v3, WebApplicationFactory, NSubstitute | `tests/` |

## Run locally

Prereqs: .NET 10 SDK, Docker, and for the app `dotnet workload install maui` (+ Android SDK / Xcode).

```bash
cd app
export SA_PASSWORD='<strong password>'
docker compose up -d                       # SQL Server + Azurite

cd src/Expovice.Api
dotnet user-secrets set ConnectionStrings:Sql "Server=localhost,1433;Database=expovice;User Id=sa;Password=$SA_PASSWORD;TrustServerCertificate=True"
dotnet user-secrets set Storage:ConnectionString "UseDevelopmentStorage=true"
dotnet user-secrets set Jwt:SigningKey "$(openssl rand -base64 48)"
dotnet run                                 # http://localhost:5080 — migrates DB, seeds 56 sample pros around Indiranagar

# In another terminal: Android emulator (talks to http://10.0.2.2:5080)
cd app && dotnet build -t:Run -f net10.0-android src/Expovice.App
```

In Development the OTP is **logged** by the API (look for the `DEV-ONLY` warning) instead of being texted.

Tests: `dotnet test --project tests/Expovice.Api.Tests` and `dotnet test --project tests/Expovice.App.Core.Tests`.

## Deploy to Azure

```bash
az group create -n expovice-rg -l centralindia
export EXPOVICE_JWT_KEY=$(openssl rand -base64 48)
az deployment group create -g expovice-rg -f infra/main.bicep -p infra/main.bicepparam
dotnet publish src/Expovice.Api -c Release -o publish && (cd publish && zip -qr ../api.zip .)
az webapp deploy -g expovice-rg -n <webAppName output> --src-path api.zip --type zip
```

Then set `AppConfig.ApiBaseUrl` (Release) in `src/Expovice.App/AppConfig.cs` to the `apiBaseUrl` output.
The `deploy-api` GitHub workflow does the same with OIDC (see the comment at its top for one-time setup).

**Cost at launch is ~₹0 + SMS**: App Service F1, Azure SQL free offer (auto-pauses instead of billing), LRS storage, Log Analytics capped at 1 GB/day.
Set `budgetContactEmails` in `main.bicepparam` to get an alert at 80 % / 100 % of `monthlyBudget`.

### Turning on sign-in providers

| Provider | What to do |
|---|---|
| Phone OTP | Buy a number in the Communication Services resource and set `smsFromNumber`. Sending to Indian numbers needs DLT-registered templates; if ACS can't send to your users, implement `ISmsSender` for an Indian gateway (MSG91, 2Factor, …) — one class, registered in `AddExpoviceMessaging`. |
| Google | Create an OAuth *Web* client, redirect URI `https://<api>/signin-google`; set `googleClientId` + `EXPOVICE_GOOGLE_SECRET`. |
| Apple | Create a Services ID + key, return URL `https://<api>/signin-apple`; set `appleClientId/TeamId/KeyId` + `EXPOVICE_APPLE_P8`. |

## Design decisions

- **Own JWTs instead of Entra External ID.** One token flow for phone OTP, Google and Apple, any SMS vendor, no per-MAU cost. Access tokens 1 h; refresh tokens 60 days, hashed in SQL, rotated on every use (reuse ⇒ all sessions revoked).
- **Google/Apple via the API (browser flow).** The app opens `/auth/external/{provider}`; the API finishes OAuth and redirects to `expovice://auth#refresh_token=…`, which the app immediately rotates — so the token in the URL is single-use.
- **Geo search without spatial types.** Bounding-box prefilter on an indexed `(Latitude, Longitude)`, then Haversine in memory. Works on Azure SQL and SQLite (tests) alike.
- **Race-safe status changes.** `ExecuteUpdateAsync … WHERE Status = expected`; 0 rows ⇒ 409. Counters (requests, rating) are updated atomically in the same transaction.
- **Passwordless Azure.** The API's managed identity is the SQL Entra admin, reads Key Vault secrets via references, and writes blobs via RBAC. Storage has shared keys disabled; photos are served by short-lived SAS URLs; ID documents are never exposed.

## Differences from the prototype (deliberate)

- OTP is **6 digits** (prototype: 4) — 4-digit codes are too guessable.
- **"Mark as done"** on accepted jobs, otherwise nothing could reach *Completed* and be rated.
- Location comes from the device (fallback: Indiranagar); "Available today" is derived from the pro's working days.
- Prototype-only chrome (jump list, fake status bar, iOS/Android toggle) is gone.
- `.washed` photo filter is approximated with a 14 % cream overlay (no CSS filters in native UI).

## Known gaps

The bell and "Change address" buttons are placeholders; "Saved addresses" and "Help" show a toast; past time slots for today aren't disabled; first-time "Save & exit" discards the draft.
Not yet exercised against real Azure resources, SMS, Google/Apple, or on a device — see the PR description for exactly what was verified.
