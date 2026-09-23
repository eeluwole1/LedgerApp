# Ledger.API

The backend for **Ledger**, a personal finance tracker. It's an ASP.NET Core Web API that stores each user's income/expense transactions, issues JWTs for authentication, and enforces that a user can only ever see or modify their own data.

**Live demo:** https://ledgerapp-demo-bgaqbpadbzfjhegh.canadacentral-01.azurewebsites.net (Swagger UI at `/swagger`) — consumed by the deployed frontend, see `Ledger.Client/README.md`.

## User Story

> **As a Ledger user, I want to record and review my own income and expenses through a secure API — with running totals I can actually trust — so that I always know where my money stands without a third party ever seeing or touching my data.**

**The 30-second interview version:** This is the ASP.NET Core backend for a personal finance tracker. A user registers, logs in, and gets a JWT; from there every transaction they create, read, update, or delete is scoped to their account alone, enforced server-side on every query — not just hidden in the UI. The API exposes paginated transaction listings plus a separately-aggregated summary endpoint, so dashboard totals stay accurate regardless of which page the client happens to have loaded.

The part worth highlighting: I found and fixed a floating-point precision bug in `Amount`, which was `double` — binary floating-point that can't exactly represent most decimal fractions (`0.1 + 0.2` evaluates to `0.30000000000000004`, not `0.3`), and those per-transaction rounding errors compound as the summary endpoint sums a user's transactions into a balance. That's exactly the class of bug that matters most in a ledger: not a crash, just numbers that are silently, slightly wrong. I switched `Amount` to `decimal(18,2)`, wrote a unit test proving the exact sum now holds, and — since the fix changes an existing column's actual database type — applied the EF Core migration to production *before* deploying the updated code, so the running API and its database never briefly disagreed about the column's shape. See [A bug found and fixed](#a-bug-found-and-fixed-amount-was-double) below for the full writeup.

## Tech stack

- **.NET 10** / ASP.NET Core Web API
- **Entity Framework Core** (SQL Server provider) for data access and migrations
- **JWT Bearer authentication** (`Microsoft.AspNetCore.Authentication.JwtBearer`)
- **`PasswordHasher<T>`** (`Microsoft.Extensions.Identity.Core`) for password hashing — not the full ASP.NET Core Identity framework, just its hashing utility
- **Swagger / OpenAPI** for interactive API docs in development
- **xUnit** + **EF Core's InMemory provider** for service-layer unit tests (`Ledger.API.Tests`)

## Project structure

```
Controllers/
  AuthController.cs          Register, Login — issues JWTs
  TransactionsController.cs  CRUD + pagination + summary, all scoped to the logged-in user
Data/
  AppDbContext.cs             EF Core DbContext (Users, Transactions)
  Services/
    TransactionsService.cs    Business logic + queries, called by the controller
Models/
  User.cs, Transaction.cs     EF entities — Transaction.Amount is decimal(18,2), not double/float
  Base/BaseEntity.cs          Shared Id/CreatedAt/UpdatedAt
Dtos/
  PostUserDto, LoginUserDto           Auth request shapes
  PostTransactionDto, PutTransactionDto  Transaction request shapes
  PagedResult<T>, TransactionSummaryDto  Response shapes
Migrations/                  EF Core migration history
```

```
Ledger.API.Tests/
  TransactionAmountPrecisionTests.cs  Unit tests against TransactionsService via EF Core's
                                       InMemory provider — no web host or real DB required
```

## Running locally

1. Update `appsettings.json` → `ConnectionStrings:Default` to point at your local SQL Server instance.
2. Apply migrations:
   ```
   dotnet ef database update
   ```
3. Run the API:
   ```
   dotnet run
   ```
   By default this serves on `https://localhost:7177` (see `Properties/launchSettings.json`). Swagger UI is available at `/swagger` in development.

## Authentication & authorization

**How a request becomes "authenticated":**

1. `POST /api/Auth/Register` or `POST /api/Auth/Login` validates credentials (password checked via `PasswordHasher<User>.VerifyHashedPassword`, hashed — never stored or compared as plain text) and, on success, returns a signed JWT containing two claims: `NameIdentifier` (the user's numeric ID) and `Email`.
2. The client sends that token back on every subsequent request as `Authorization: Bearer <token>`.
3. `Program.cs` configures JWT Bearer validation (issuer, audience, lifetime, and signing key, all read from the `Jwt` section of configuration — see below) as the default authentication scheme.
4. `TransactionsController` is decorated with `[Authorize]` at the class level — any request without a valid, non-expired token is rejected with `401` before it reaches an action.

**How ownership is enforced (not just authentication):**

Being logged in only proves *who* you are — it doesn't by itself stop you from reading or editing *someone else's* data. Every action in `TransactionsController` pulls the caller's user ID out of the token's `NameIdentifier` claim (via the private `TryGetUserId` helper) and passes it down to `TransactionsService`, which filters every query by it:

- `GetAll` / `GetSummary` only ever query rows `Where(t => t.UserId == userId)` — another user's transactions are invisible, not just hidden in the UI.
- `GetById` / `Update` / `Delete` match on **both** `Id` and `UserId` together. Requesting someone else's transaction ID returns a plain `404 Not Found` — identical to requesting an ID that doesn't exist at all, so an attacker can't even distinguish "not yours" from "doesn't exist."

This is what stops the classic mistake of relying on `[Authorize]` alone: `[Authorize]` answers "is there a valid user?", the per-query `UserId` filter answers "does this row belong to that user?" — you need both.

**Login is defensive about legacy/malformed password hashes**: `AuthController.Login` wraps `PasswordHasher<User>.VerifyHashedPassword` in a try/catch for `FormatException` — if a stored password value isn't a hash the current hasher recognizes (e.g. a row seeded before hashing was added), it's treated as a failed match (`401`) instead of bubbling up as an unhandled `500`.

### JWT configuration

Read from the `Jwt` section of configuration (`appsettings.json` locally; override via environment variables or your host's configuration/secrets store in any other environment):

```json
"Jwt": {
  "Issuer": "...",
  "Audience": "...",
  "Key": "..."
}
```

`Key` signs and verifies tokens (`HmacSha256`) and must be at least 32 characters. **The value committed in `appsettings.json` is a development-only placeholder — generate a new random secret for any real deployment and never reuse it.**

### CORS

The API only accepts cross-origin requests from origins explicitly listed under `AllowedOrigins` in configuration (`http://localhost:4200` for local development) — not from any origin. Add your deployed frontend's URL to this list wherever you deploy.

## API reference

All `Transactions` endpoints require `Authorization: Bearer <token>` and operate only on the caller's own data.

### Auth

| Method | Route | Body | Response |
|---|---|---|---|
| POST | `/api/Auth/Register` | `{ email, password }` | `200 { token, email }` · `400` if email is taken |
| POST | `/api/Auth/Login` | `{ email, password }` | `200 { token, email }` · `401` on bad credentials |

### Transactions

| Method | Route | Body / Query | Response |
|---|---|---|---|
| GET | `/api/Transactions/All?page=1&pageSize=10` | — | `200 { items, totalCount, page, pageSize }` |
| GET | `/api/Transactions/Summary` | — | `200 { totalIncome, totalExpenses, netBalance }` — aggregated across **all** of the user's transactions, independent of pagination |
| GET | `/api/Transactions/Details/{id}` | — | `200 <transaction>` · `404` if missing or not owned by caller |
| POST | `/api/Transactions/Create` | `{ type, amount, category, createdAt }` | `200 <created transaction>` |
| PUT | `/api/Transactions/Update/{id}` | `{ type, amount, category }` | `200 <updated transaction>` · `404` if missing or not owned |
| DELETE | `/api/Transactions/Delete/{id}` | — | `200` · `404` if missing or not owned |

`page`/`pageSize` are clamped server-side (`page` ≥ 1, `1 ≤ pageSize ≤ 100`) regardless of what's passed.

## Testing

```
dotnet test
```

Runs `Ledger.API.Tests` — currently two unit tests against `TransactionsService`, using EF Core's InMemory provider so neither a real database nor a running web host is needed:

- `GetSummary` sums fractional amounts (e.g. `0.1 + 0.2`) with exact `decimal` precision — the regression test for the bug described below.
- `GetSummary` only includes the requesting user's own transactions.

There's no integration-level coverage yet — the service layer is unit-tested, but the controllers and the JWT/authorization pipeline itself (e.g. via `WebApplicationFactory<Program>`) aren't.

## A bug found and fixed: `Amount` was `double`

`Transaction.Amount` — plus both transaction DTOs and `TransactionSummaryDto`'s totals — used to be `double`, mapped to SQL Server's `float`. Binary floating-point can't exactly represent most decimal fractions (`0.1 + 0.2` evaluates to `0.30000000000000004`, not `0.3`), and those per-transaction rounding errors compound as `GetSummary` adds a user's transactions into a balance — a silent, easy-to-miss bug in exactly the kind of app where it matters most.

Fixed by switching `Amount` (and the summary totals) to `decimal`, explicitly mapped to `decimal(18,2)` so EF Core doesn't fall back to a provider default. Since the production API and its database share the same schema at runtime, the EF Core migration was applied to the production database *first*, then the updated code deployed — deploying the code alone first would have had the live API reading a `decimal`-typed column that was still physically `float`, breaking every transaction endpoint.

## Known limitations

- **Synchronous EF Core calls.** `TransactionsService` uses `ToList()` / `FirstOrDefault()` / `SaveChanges()`, not their `Async` counterparts. Not a functional problem at this app's scale, but a synchronous DB call blocks a thread-pool thread for the query's duration — under real concurrent load this should be converted to `async`/`await` throughout the service and controller layers.
- **No integration tests** — see Testing above.

## Deploying (Azure)

`appsettings.Production.json` documents every value that must be overridden before this goes anywhere real — it ships with `REPLACE_WITH_*` placeholders on purpose, so the app is obviously misconfigured (rather than silently insecure) if you forget one:

- `ConnectionStrings:Default` → your Azure SQL connection string
- `Jwt:Issuer` / `Jwt:Audience` → your deployed API's URL
- `Jwt:Key` → a freshly generated secret, **not** the development one
- `AllowedOrigins` → your deployed frontend's URL

Prefer setting these via Azure App Service's **Configuration → Application settings** blade (environment variables, using double-underscore for nested keys — e.g. `Jwt__Key`, `AllowedOrigins__0`) or Key Vault rather than editing `appsettings.Production.json` directly, so secrets never sit in source control. Saving Application settings triggers an automatic restart of the app.

**Azure SQL firewall**: by default, an Azure SQL Server rejects every connection, including from your own App Service. Under the SQL **Server** resource (not the database) → **Security → Networking**, check **"Allow Azure services and resources to access this server"** — without it, every DB-touching endpoint returns `500` even though the connection string, JWT config, and CORS are all correct. This one is easy to miss because Swagger's static docs page still loads fine (it never touches the database), so the failure only shows up once you actually call an endpoint.
