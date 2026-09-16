# NovaWallet Ledger Service

A wallet ledger for FirstBank NovaPay's NovaWallet module — create wallets, credit them (simulating
inbound NIP settlement), transfer between them, and query balance/statement — built to the
"never lose, duplicate, or miscount money" standard the take-home brief asks for.

## Architecture

This follows a modular-monolith / Clean Architecture style — per-module Domain/Application/
Infrastructure/API projects on top of a shared set of building blocks — scaled down to what a
single bounded context actually needs:

```
src/
  BuildingBlocks/                          # shared across any future module
    NovaWallet.BuildingBlocks.Domain/       # Entity, AggregateRoot, Result<T>/Error
    NovaWallet.BuildingBlocks.Application/  # ICommand/IQuery + MediatR pipeline behaviours
    NovaWallet.BuildingBlocks.Infrastructure/# BaseDbContext (xmin concurrency convention), Postgres resilience
  Modules/Wallet/                          # the one bounded context this service has
    NovaWallet.Wallet.Domain/               # WalletAccount, LedgerEntry, AuditEntry — no EF/HTTP references
    NovaWallet.Wallet.Application/          # one file per feature: command/query + validator + handler
    NovaWallet.Wallet.Infrastructure/        # EF Core, the repository, idempotency store, migrations
    NovaWallet.Wallet.API/                  # Carter endpoints, request/response contracts
  Api/NovaWallet.Api/                       # composition root: Program.cs, JWT, Scalar API docs, health checks
tests/
  NovaWallet.Wallet.UnitTests/              # domain invariants, handler logic (mocked repo), validators
  NovaWallet.IntegrationTests/               # real Postgres via Testcontainers, real HTTP, real concurrency
```

Each layer only depends inward (API/Infrastructure → Application → Domain), and the Wallet module
is the only module — but it's split into four projects instead of folders in one project, because
that boundary is what would let a second module (e.g. NovaSave) be added later without the two
becoming tangled. For a service this size that's a deliberate trade of a little ceremony for
enforcing the boundary at compile time.

### Key building blocks

- **`Result<T>` / `Error`** — handlers never throw for expected business failures (not found,
  insufficient funds, forbidden, daily limit exceeded). They return `Result.Failure(error)`, and
  `Error.Type` maps to an HTTP status at the API layer (`ResultExtensions.ToProblem`). Exceptions
  are reserved for the genuinely unexpected (malformed input past validation, DB conflicts,
  bugs) and are caught once, centrally, by `GlobalExceptionHandler`.
- **CQRS via MediatR** — `ICommand<T>`/`IQuery<T>` are thin markers over `IRequest<Result<T>>`.
  A `ValidationBehaviour` runs FluentValidation before any handler executes and throws (→ 400)
  rather than returning a Result, so "malformed request" and "well-formed request the business
  rejected" stay on visibly different paths.
- **`xmin` as the optimistic-concurrency token** — `WalletAccount` uses Postgres's own `xmin`
  system column (via a shadow `uint` property EF is told to treat as a row-version) instead of a
  hand-maintained counter. Postgres bumps it on every UPDATE for free.

## How correctness is actually enforced

The brief's hard constraint is "balance must never go negative under any interleaving of
concurrent requests." Three independent mechanisms combine to guarantee that for `Transfer`:

1. **Row-level pessimistic locking, canonical order.** `WalletRepository.LockForUpdateAsync` runs
   `SELECT ... WHERE "Id" = ANY(@ids) ORDER BY "Id" FOR UPDATE` — always sorting the wallet ids
   ascending before locking, regardless of which is source/destination. Two transfers between the
   same pair of wallets (even in opposite directions) always request the locks in the same order,
   so one simply waits for the other instead of deadlocking.
2. **Business rules checked after the lock is held**, using the just-locked row's balance — not a
   value read earlier. Insufficient-funds and the WAT daily-limit check both happen inside the
   locked section, so a second concurrent transfer can't sneak in between "check balance" and
   "debit balance."
3. **The aggregate itself refuses to go negative.** `WalletAccount.Debit` throws if the amount
   exceeds the balance — a last-resort invariant even if 1/2 were somehow bypassed.

`xmin`-based optimistic concurrency is still configured on top of this as defense in depth (e.g.
for `CreditWallet`, which locks a single row rather than needing a canonical multi-row order), but
the pessimistic lock is what actually carries the transfer's correctness guarantee — see
`TransferConcurrencyTests.Concurrent_transfers_that_would_overdraw_the_wallet_only_let_the_affordable_ones_through`,
which fires 20 concurrent transfer requests against a wallet that can only afford 10, and asserts
exactly 10 succeed and the final balance is exactly zero (never negative, nothing duplicated).

### Idempotency

The `Idempotency-Key` header on `POST /api/transfers` is backed by a dedicated `idempotency_keys`
table with the key as its primary key. Claiming a key is deliberately **its own database
round-trip**, separate from the transfer's own transaction — Postgres aborts an entire
transaction on the first statement error, so catching a unique-violation on the claim insert
*inside* the transfer's transaction would have poisoned it for everything that followed. Two
concurrent requests racing the same fresh key: one wins the insert, the other gets a clean
"in flight" conflict (409) without ever touching the transfer transaction. See
`TransferConcurrencyTests.Concurrent_replays_of_the_same_idempotency_key_only_apply_the_transfer_once`.

Replaying a **completed** key returns the originally cached success/failure verbatim — including a
cached business failure (e.g. a retried request that already failed with insufficient funds gets
the same failure back, not a fresh re-evaluation against a balance that may have changed since).
Reusing a key with a different request payload is rejected outright.

### Daily limit

`WalletAccount.DailyOutboundLimitKobo` defaults to ₦500,000/day. The window resets at midnight
**WAT** (`WatTime.GetCurrentDayWindowUtc`) — WAT is a fixed UTC+1 offset with no daylight saving,
so this is simple arithmetic, not a timezone-database lookup. The check sums today's
`TransferOut` ledger entries for the wallet (inside the same lock as the transfer itself) rather
than maintaining a separate running counter, trading a bit of query cost for one less piece of
denormalized state that could drift.

### Audit log vs. statement

`ledger_entries` is the customer-facing statement (paginated, newest first). `audit_entries` is a
separate, append-only table — no `Update`/`Delete` is exposed anywhere in the repository or
`DbContext` for it. They're intentionally two tables: the statement's shape is allowed to evolve
for presentation reasons; the audit trail's shouldn't.

## Auth

JWT bearer, as the brief asks — a **mock issuer** (`POST /api/auth/dev-token`, mounted only
outside `Production`), since building a real KYC-backed identity provider is out of scope. It
mints a token for whatever `customerId`/`role` it's asked for, with no credential check at all —
the point of this take-home is the middleware and claims handling, not an auth server. Claims are
`sub` (customer id) and `role` (`customer` | `system`).

- `role=customer`: can create/query only their own wallet, and can only initiate transfers *from*
  a wallet they own.
- `role=system`: stands in for a trusted internal caller (e.g. the NIP inbound-settlement
  webhook). Only `system` may call `POST /wallets/{id}/credit` — a customer cannot credit their
  own wallet directly, matching how money actually arrives in a real wallet.

## Errors

All failures are RFC 7807 Problem Details. Two independent paths produce them:
`Result.Failure` → `ResultExtensions.ToProblem` (expected business outcomes, status derived from
`Error.Type`), and uncaught exceptions → `GlobalExceptionHandler` (validation exceptions → 400,
`DbUpdateException`/`DbUpdateConcurrencyException` → 409, everything else → 500, logged as an
error rather than a warning).

## Stretch goals implemented

- **Rate limiting** on `POST /api/transfers` specifically (fixed window, 30/min per authenticated
  subject or IP) — the one endpoint a scripted client could otherwise hammer.
- **Structured logging with correlation ids** — Serilog request logging plus a
  `CorrelationId` (the ASP.NET Core `TraceIdentifier`) pushed onto the log context for every
  request.
- **Health endpoints** — `/health/live` and `/health/ready` (the latter checks Postgres
  connectivity), suitable for container orchestration probes.

Not implemented: the outbox pattern / `TransferCompleted` event. Given the time box, the idempotent
transfer + statement/audit trail felt like the higher-value correctness work for a ledger
specifically; publishing an integration event would be the natural next addition (an
`OutboxMessages` table written in the same transaction as the transfer, drained by a background
job) and is called out here rather than half-implemented.

## Assumptions made

- One wallet per customer (`CustomerId` has a unique index). The brief doesn't say otherwise, and
  it keeps ownership checks simple; multiple wallets per customer would mean the API takes an
  explicit "which wallet" concept everywhere instead of implying it from the token.
- The `Idempotency-Key` contract only applies to `POST /api/transfers` — the brief specifies it
  for "the transfer endpoint."
- Money amounts arrive as plain JSON integers (kobo) — the brief already commits to integer kobo,
  so no additional string/decimal parsing layer was added at the boundary.

## Running it

```bash
docker compose up --build
```

This builds the API image, starts Postgres, waits for Postgres's health check, and runs EF Core
migrations automatically at API startup (see `Program.cs`) — no separate migration step. Once up:

- API docs (Scalar): http://localhost:8080/scalar
- Raw OpenAPI document: http://localhost:8080/swagger/v1/swagger.json
- Health: http://localhost:8080/health/live, http://localhost:8080/health/ready

Get a token, then call the API:

```bash
curl -X POST http://localhost:8080/api/auth/dev-token \
  -H "Content-Type: application/json" \
  -d '{"customerId":"11111111-1111-1111-1111-111111111111","role":"customer"}'
```

## Running the tests

```bash
dotnet test tests/NovaWallet.Wallet.UnitTests/NovaWallet.Wallet.UnitTests.csproj   # fast, no Docker needed
dotnet test tests/NovaWallet.IntegrationTests/NovaWallet.IntegrationTests.csproj   # needs Docker (Testcontainers starts its own Postgres)
```

The integration project starts its own disposable Postgres container per test run — it does not
use (and cannot see) the `docker-compose.yml` Postgres instance, and needs nothing already running
besides a working Docker daemon.
