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
- **xUnit** + **EF Core's InMemory provider** for service-layer unit tests, and **`WebApplicationFactory<Program>`** (`Microsoft.AspNetCore.Mvc.Testing`) for integration tests that drive the real HTTP pipeline (`Ledger.API.Tests`)

## Project structure

```
Controllers/
  AuthController.cs          Register, Login — issues JWTs
  TransactionsController.cs  CRUD + pagination + summary, all scoped to the logged-in user
  BudgetsController.cs       Per-category monthly budgets + spent-vs-limit progress, scoped to the user
Data/
  AppDbContext.cs             EF Core DbContext (Users, Transactions, Budgets)
  Services/
    TransactionsService.cs    Business logic + queries, called by the controller
    BudgetsService.cs         Budget CRUD + the monthly progress aggregation
Models/
  User.cs, Transaction.cs, Budget.cs  EF entities — money columns are decimal(18,2), not double/float
  Base/BaseEntity.cs          Shared Id/CreatedAt/UpdatedAt
Dtos/
  PostUserDto, LoginUserDto           Auth request shapes
  PostTransactionDto, PutTransactionDto  Transaction request shapes
  PagedResult<T>, TransactionSummaryDto  Response shapes
  SaveBudgetDto, UpdateBudgetDto      Budget request shapes (validated with data annotations)
  BudgetDto, BudgetProgressDto        Budget response shapes — entities are never returned directly
Migrations/                  EF Core migration history
```

```
Ledger.API.Tests/
  TransactionAmountPrecisionTests.cs  Unit tests against TransactionsService via EF Core's
                                       InMemory provider — no web host or real DB required
  TransactionMonthFilterTests.cs      Month filter: boundary days, all-time fallback, user isolation
  BudgetsServiceTests.cs              Budget CRUD, duplicates, ownership, and the progress math
  ApiFactory.cs                       WebApplicationFactory<Program> with an in-memory DB; registers users via the real endpoint
  ApiIntegrationTests.cs              Real HTTP requests through routing, JWT auth, controllers and services
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
| GET | `/api/Transactions/All?page=1&pageSize=10&year=2026&month=9` | — | `200 { items, totalCount, page, pageSize }` · `year`/`month` are optional but must be sent together (month 1–12), otherwise `400` |
| GET | `/api/Transactions/Summary?year=2026&month=9` | — | `200 { totalIncome, totalExpenses, netBalance }` — aggregated across **all** of the user's transactions (or all of one month when `year`/`month` are given), independent of pagination |
| GET | `/api/Transactions/Details/{id}` | — | `200 <transaction>` · `404` if missing or not owned by caller |
| POST | `/api/Transactions/Create` | `{ type, amount, category, createdAt }` | `200 <created transaction>` |
| PUT | `/api/Transactions/Update/{id}` | `{ type, amount, category, createdAt }` | `200 <updated transaction>` · `404` if missing or not owned |
| DELETE | `/api/Transactions/Delete/{id}` | — | `200` · `404` if missing or not owned |

`page`/`pageSize` are clamped server-side (`page` ≥ 1, `1 ≤ pageSize ≤ 100`) regardless of what's passed.

### Budgets

One monthly limit per category per user. Same ownership rules as transactions: every query filters by the caller's `UserId`, and another user's budget returns `404`, indistinguishable from one that doesn't exist.

| Method | Route | Body / Query | Response |
|---|---|---|---|
| GET | `/api/Budgets/All` | — | `200 [ { id, category, monthlyLimit } ]` |
| GET | `/api/Budgets/Progress?year=2026&month=9` | `year`/`month` optional (default: current UTC month), but sent together | `200 [ { budgetId, category, monthlyLimit, spent, remaining, percentUsed } ]` · `400` on bad month/year |
| POST | `/api/Budgets/Create` | `{ category, monthlyLimit }` | `200 <budget>` · `409` if that category already has a budget · `400` if invalid (blank/over 50 chars category, limit < 0.01) |
| PUT | `/api/Budgets/Update/{id}` | `{ monthlyLimit }` | `200 <budget>` · `404` if missing or not owned |
| DELETE | `/api/Budgets/Delete/{id}` | — | `200` · `404` if missing or not owned |

`Progress` sums only the caller's **expense** transactions for the month, grouped by category in SQL (`GroupBy` + `Sum`), then matches them to budgets by category name. `remaining` goes negative and `percentUsed` goes above 100 when over budget. A category can't be renamed — delete and recreate it. Uniqueness is enforced by a database index on `(UserId, Category)`, with a friendly `409` in front of it.

## Testing

```
dotnet test
```

Runs `Ledger.API.Tests` — 30 tests: fifteen unit tests against `TransactionsService` and `BudgetsService` (EF Core's InMemory provider, no web host or real database needed), plus fifteen integration tests (next section). The unit tests cover:

- `GetSummary` sums fractional amounts (e.g. `0.1 + 0.2`) with exact `decimal` precision — the regression test for the bug described below.
- `GetSummary` only includes the requesting user's own transactions.
- Month filter: `GetSummary` and `GetAll` include the first and last day of a month but not the neighbouring days (Aug 31 / Oct 1), still cover all time when no month is given, and never return another user's rows.

- Budgets: create/update/delete, trimmed categories, duplicate category rejected (but allowed for a different user), other users' budgets invisible and untouchable, and the progress math — only that user's expenses in that category and month, `decimal`-exact sums (10.10 + 20.20 = 30.30), negative remaining and >100% when over budget, and a zero-spend budget.

Budgets were also checked end to end against the real SQL Server database and the running Angular client with throwaway users: create, a `409` on a duplicate category, `400` on a zero limit or blank category, the September progress figures (30.30 spent / 69.70 left), a second user getting `[]` and `404` on someone else's budget, `401` without a token, and the dashboard bars rendering green, amber (83%) and red (`$10.00 over`, bar capped at 100%).

The month filter was also checked end to end against the real SQL Server database with a throwaway user (month boundaries, all-time vs. one month, and `400` on `month=13` or `year` without `month`).

### Integration tests

`ApiIntegrationTests` boots the real app through `WebApplicationFactory<Program>` (routing, JWT validation, controllers, services, EF Core) against an in-memory database, and sends real HTTP requests as freshly registered users — 15 tests, so 30 in total. It runs in a `Testing` environment on purpose: `Development` would load the developer's user-secrets, i.e. the real Azure SQL connection string. They cover:

- **Authentication:** protected endpoints return `401` with no token or a garbage token; duplicate registration is `400`; a wrong password is `401`.
- **Per-user isolation:** another user's transaction returns `404` for read, update and delete and is left untouched; "not yours" and "doesn't exist" produce the same response (apart from ASP.NET's per-request `traceId`); list and summary only include the caller's own data.
- **Regressions for the bugs documented here:** an edited transaction date is persisted, and the month filter / pagination clamp behave as documented (including `400` on bad input).
- **Budgets:** duplicates are `409`, invalid input is `400`, another user's budget is invisible and `404`, and `Progress` sums only that month's expenses in the category.

These tests are only worth having if they can fail. To check that, `GetById` was temporarily changed to ignore `UserId`: both isolation tests failed (another user got `200 OK` for someone else's transaction), and the file was then restored.

`Program` is made visible to the tests with an empty `public partial class Program { }` at the bottom of `Program.cs`.

## A bug found and fixed: `Amount` was `double`

`Transaction.Amount` — plus both transaction DTOs and `TransactionSummaryDto`'s totals — used to be `double`, mapped to SQL Server's `float`. Binary floating-point can't exactly represent most decimal fractions (`0.1 + 0.2` evaluates to `0.30000000000000004`, not `0.3`), and those per-transaction rounding errors compound as `GetSummary` adds a user's transactions into a balance — a silent, easy-to-miss bug in exactly the kind of app where it matters most.

Fixed by switching `Amount` (and the summary totals) to `decimal`, explicitly mapped to `decimal(18,2)` so EF Core doesn't fall back to a provider default. Since the production API and its database share the same schema at runtime, the EF Core migration was applied to the production database *first*, then the updated code deployed — deploying the code alone first would have had the live API reading a `decimal`-typed column that was still physically `float`, breaking every transaction endpoint.

## Known limitations

- **Synchronous EF Core calls.** `TransactionsService` uses `ToList()` / `FirstOrDefault()` / `SaveChanges()`, not their `Async` counterparts. Not a functional problem at this app's scale, but a synchronous DB call blocks a thread-pool thread for the query's duration — under real concurrent load this should be converted to `async`/`await` throughout the service and controller layers.
- **Integration tests use an in-memory database, not SQL Server.** They exercise the real HTTP pipeline, but not SQL Server-specific behaviour (collation, the unique-index race in `BudgetsService.Add`, serverless cold starts).
- **Categories are free text.** Neither transactions nor budgets validate a category against a server-side list; the dropdown lives in the Angular client, and a budget only "sees" spending whose category text matches it (case-insensitively). Fine while the client is the only writer, but a shared list on the server would be the next step.

## Roadmap

**Goal:** a user can see whether they're on track for the month. Today the app answers "what have I spent?" but not "am I overspending?", and its totals cover all time.

| Phase | Scope | Status |
|---|---|---|
| 1. Month filter | Optional year/month on `GetAll` and `GetSummary`; month selector on the dashboard. No schema change. | **Built** |
| 2. Budgets | New `Budget` entity (`UserId`, `Category`, `MonthlyLimit` as `decimal(18,2)`, unique per user and category), migration (`AddBudgets`), and a `BudgetsController` with the same per-user ownership rules as transactions. | **Built**; `AddBudgets` must be applied to the database before the code is deployed |
| 3. Progress view | Endpoint returning spent vs. limit per category for a month (`GroupBy` aggregation); progress bars in the dashboard. | **Built** (API + dashboard bars + `/budgets` page) |
| 4. Tests and write-up | Unit tests for the aggregation and budget ownership; README updated to match what shipped. | **Built** |

Deliberately out of scope for now: bank sync, CSV import, recurring transactions, charts, multi-currency, forgot-password, and converting EF Core calls to `async`.

Phase 2 changes the schema on a database that production shares, so its migration is applied first, then the code is deployed (same sequencing as the `decimal` fix above).

## Deploying (Azure)

`appsettings.Production.json` documents every value that must be overridden before this goes anywhere real — it ships with `REPLACE_WITH_*` placeholders on purpose, so the app is obviously misconfigured (rather than silently insecure) if you forget one:

- `ConnectionStrings:Default` → your Azure SQL connection string
- `Jwt:Issuer` / `Jwt:Audience` → your deployed API's URL
- `Jwt:Key` → a freshly generated secret, **not** the development one
- `AllowedOrigins` → your deployed frontend's URL

Prefer setting these via Azure App Service's **Configuration → Application settings** blade (environment variables, using double-underscore for nested keys — e.g. `Jwt__Key`, `AllowedOrigins__0`) or Key Vault rather than editing `appsettings.Production.json` directly, so secrets never sit in source control. Saving Application settings triggers an automatic restart of the app.

**Azure SQL firewall**: by default, an Azure SQL Server rejects every connection, including from your own App Service. Under the SQL **Server** resource (not the database) → **Security → Networking**, check **"Allow Azure services and resources to access this server"** — without it, every DB-touching endpoint returns `500` even though the connection string, JWT config, and CORS are all correct. This one is easy to miss because Swagger's static docs page still loads fine (it never touches the database), so the failure only shows up once you actually call an endpoint.
