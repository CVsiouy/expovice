---
tags: [dotnet, maui, aspnetcore, azure, efcore, expovice]
---

# Expovice — how it's built (study notes)

## 1. Solution shape
- **Contracts** (`net10.0`): records shared by API and app → one definition of every DTO, route and limit (DRY). Change a field once, both sides fail to compile until fixed.
- **API** (ASP.NET Core Minimal APIs): `app.MapGet(route, handler)`; handlers grouped per feature in static `MapXxx(this IEndpointRouteBuilder)` extension methods.
- **App.Core** (`net10.0`, no MAUI): view models + HTTP client. Because it has no UI dependency, it's unit-testable on any machine.
- **App** (MAUI, `net10.0-android;net10.0-ios`): XAML pages that bind to App.Core view models.
- `Directory.Build.props` = MSBuild settings applied to every project; `Directory.Packages.props` = Central Package Management (versions in one file).

## 2. C# concepts used
- `record` — immutable class with value equality; perfect for DTOs. `sealed record X(string A)` = positional record (constructor + properties generated).
- `FrozenDictionary` / `FrozenSet` (`System.Collections.Frozen`) — built once, optimised for reads. Used for the category catalog.
- Primary constructors: `class Foo(IBar bar)` — `bar` is available in the whole class body.
- `async/await` + `CancellationToken` — free the thread while waiting on I/O; the token lets callers abort.
- `TimeProvider` — inject time instead of `DateTime.UtcNow` so tests can freeze/advance the clock (`FakeTimeProvider`).
- Pattern matching: `x switch { { } s when cond => ..., _ => ... }`.

## 3. Security
- **OTP**: `RandomNumberGenerator` (crypto-random), store only an **HMAC hash**, 5-minute TTL, 5 attempts, 30 s resend cooldown, compared with `CryptographicOperations.FixedTimeEquals` (constant time → no timing attack). Plus per-IP rate limiting (`AddRateLimiter`).
- **JWT access token** (1 h) + **refresh token** (60 days, random 32 bytes, SHA-256 hashed in DB). *Rotation*: each refresh revokes the old token; using a revoked one ⇒ revoke all (stolen-token detection).
- **Secrets**: never in `appsettings.json`. Local → `dotnet user-secrets`. Azure → Key Vault, pulled in by App Service *Key Vault references* `@Microsoft.KeyVault(SecretUri=...)`.
- **Managed identity**: the web app has an Azure identity; RBAC roles let it read secrets / write blobs / log into SQL — no passwords anywhere.
- Photos: file size + content-type + *magic bytes* check; served via short-lived **SAS** URLs; storage account disallows public access and shared keys.

## 4. EF Core
- `DbContext` is **scoped and NOT thread-safe** — one per request; never share across threads.
- Primitive collections (`List<string>` columns) are stored as JSON automatically (EF 8+).
- `ExecuteUpdateAsync(...)` — single SQL `UPDATE ... WHERE` without loading entities. With `WHERE Status = 'Pending'` it becomes an atomic compare-and-set ⇒ two providers clicking Accept can't both win (0 rows ⇒ 409 Conflict).
- Migrations: `dotnet ef migrations add Name` (tool pinned in `dotnet-tools.json`).

## 5. Geo search
- Haversine = great-circle distance between two lat/lon points.
- Bounding box: 1° latitude ≈ 111 km; longitude degrees shrink by `cos(lat)`. Filter the box in SQL (uses the index), then exact Haversine in memory.

## 6. MAUI + MVVM
- `[ObservableProperty] private string name;` → generates `Name` property raising `PropertyChanged`.
- `[RelayCommand] async Task Save()` → generates `SaveCommand` (an `IAsyncRelayCommand`) for XAML `Command="{Binding SaveCommand}"`.
- `x:DataType` on XAML = **compiled bindings**: typos become build errors and bindings are faster.
- Shell navigation: `Shell.Current.GoToAsync("route", parameters)`; wrapped in `INavigationService` so view models stay testable.
- `SecureStorage` (Keychain/Keystore) for the refresh token; `Preferences` for non-secret flags.
- `DelegatingHandler` (`AuthTokenHandler`) = HttpClient middleware: adds `Authorization: Bearer`, on 401 refreshes once. A `SemaphoreSlim(1,1)` makes concurrent 401s share one refresh (thread-safe).
- `AddStandardResilienceHandler()` (Polly under the hood) retries transient failures — but **not POSTs** (not idempotent: retrying could create two bookings).

## 7. Azure cost cheatsheet
| Service | Free part |
|---|---|
| App Service F1 | free (sleeps, 60 CPU-min/day) |
| Azure SQL free offer | 100k vCore-s + 32 GB / month, auto-pause instead of bill |
| Blob Storage LRS | pennies |
| Key Vault | ~cents per 10k ops |
| App Insights / Log Analytics | 5 GB/month free; capped at 1 GB/day here |
| SMS | **always paid** (+ India DLT registration) |

## 8. Commands
```bash
dotnet test --project tests/Expovice.Api.Tests
dotnet ef migrations add AddSomething --project src/Expovice.Api
az deployment group create -g rg -f infra/main.bicep -p infra/main.bicepparam
az bicep build --file infra/main.bicep
```
