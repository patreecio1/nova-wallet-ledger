# AI Usage

This entire service was built in a single pair-programming session with **Claude Code** (Claude
Sonnet 5), used as an active collaborator across the whole task: scaffolding the solution, writing
the domain/application/infrastructure/API code, generating the EF Core migration, writing and
debugging both test projects against a real Postgres instance, and writing this documentation.

The brief is explicit that judgment in directing the AI — including catching where it was wrong —
is what's being assessed, so this file documents real prompts from this session and a real
correctness bug the AI introduced and then caught by actually running the tests, not a
hypothetical one.

## Representative prompts and what came back

**1. "Build me a .NET 8 API for this using a modular-monolith / Clean Architecture style — shared
BuildingBlocks plus a per-module Domain/Application/Infrastructure/API split, CQRS via MediatR, a
`Result`/`Error` pattern instead of exceptions for business failures — and tell me if that's a good
fit here."**
The AI scaffolded the solution along those lines (BuildingBlocks layering, the `Result`/`Error`
type, MediatR + pipeline behaviours, `Carter`-based endpoints, a `BaseDbContext` concurrency-token
convention, per-module DI registration) and reported back a concrete trade-off assessment before
writing feature code — namely that a full outbox/integration-event pattern would be over-engineering
for a single-module service at this size, so it was deliberately left out rather than half-built
(see README's "Stretch goals" section for what that would look like if the service grew more
modules).

**2. "How should idempotency and the transfer's row-locking interact with the database
transaction?"**
The AI's first draft nested claiming the `Idempotency-Key` *inside* the same transaction as the
wallet locking and balance mutation, catching a unique-constraint violation on the claim insert as
an ordinary `try/catch`. This is the caught mistake — see below.

**3. "Actually run the integration tests against Docker, don't just assume the SQL is right."**
Running `dotnet test` against a live Testcontainers-provisioned Postgres (rather than trusting the
code by inspection) surfaced a real bug in the row-locking query — see below.

## A concrete mistake the AI made, why it was unsafe, and how it was caught

**The bug:** `WalletRepository.LockForUpdateAsync` originally issued raw SQL as
`SELECT *, xmin FROM wallet.wallets WHERE id = ANY(@ids) ORDER BY id FOR UPDATE`. This compiled
and looked correct. Running the integration test suite against a real Postgres instance (a
disposable Postgres container the test spins up itself) immediately failed with
`42703: column "id" does not exist` on every credit/transfer call.

**Why it happened:** EF Core's default Npgsql provider preserves C# property names verbatim as
quoted, case-sensitive column names (`"Id"`, not `id`). Unquoted identifiers in Postgres fold to
lowercase, so `id` and `xmin` in the raw SQL silently referred to the wrong (nonexistent) name
instead of the actual `"Id"` column. This is exactly the kind of thing an LLM (or a developer
copy-pasting from a MySQL/SQL-Server-flavored example) gets wrong by default, because MySQL/SQL
Server are far more forgiving about identifier case.

**Why it would have been unsafe to ship:** this query is *the* mechanism that makes concurrent
transfers safe — it is the `SELECT ... FOR UPDATE` row lock that every other correctness guarantee
in this service (no negative balances, no lost updates under concurrency) is built on top of. Had
this shipped, every credit and every transfer would 500 in production, immediately, on the very
first request — a total outage, not a subtle edge case. It's also the kind of failure that a
by-inspection code review is likely to miss, because the query reads as obviously correct at a
glance.

**How it was caught:** by refusing to trust "the code compiles and the unit tests (which mock the
repository) pass" as sufficient evidence, and instead running the integration test suite against a
real Postgres container before considering the concurrency work done. The fix was to quote the
identifiers explicitly: `WHERE "Id" = ANY(@ids) ORDER BY "Id"`. This is also the reasoning behind
this repo's testing strategy generally — the concurrency and idempotency tests
(`TransferConcurrencyTests`) deliberately hit real HTTP endpoints backed by a real database rather
than mocking the repository, specifically because bugs like this one only exist at the SQL
boundary and are invisible to a mocked-repository unit test.

## A design flaw the AI caught in its own draft before it shipped

The first design for the `Idempotency-Key` contract claimed the key (an `INSERT` into an
`idempotency_keys` table) *inside* the same database transaction as the wallet-locking and
balance-mutation logic, planning to catch a `DbUpdateException` on a unique-constraint violation
as an ordinary in-transaction retry path. This is unsafe on Postgres specifically: Postgres aborts
an *entire* transaction after the first statement error (unlike, say, SQL Server, where a caught
error can sometimes let the transaction continue) — every subsequent statement, including the
final `COMMIT`, fails with "current transaction is aborted" once the constraint violation occurs,
unless the caller has explicitly wrapped the risky statement in a `SAVEPOINT`. Catching the
exception without a savepoint would have looked like it worked in casual testing (no exception
escapes the method) while actually poisoning every concurrent request that raced a fresh
idempotency key.

The fix applied: claiming the key is its own small, independent unit of work — a single
`SaveChangesAsync` call with no explicit ambient transaction, so a losing insert fails and rolls
back cleanly on its own (EF Core's implicit per-`SaveChanges` transaction), *before* the transfer's
own transaction is ever opened. This is documented inline in
`TransferCommandHandler.Handle` and `IdempotencyStore.TryClaimAsync`, and is covered by
`TransferConcurrencyTests.Concurrent_replays_of_the_same_idempotency_key_only_apply_the_transfer_once`,
which fires ten concurrent requests carrying the *same* key and asserts exactly one transfer is
ever applied.

## A third mistake, caught by writing a test for a claim that had never actually been tested

The transfer handler's own doc-comment claimed that locking both wallets in ascending-Id order
"rules out deadlocks between opposite-direction transfers of the same pair" — but until asked to
close the gap, nothing actually exercised that specific scenario (transfers *from* A *to* B and
*from* B *to* A, fired concurrently, many times). Writing
`AdditionalConcurrencyTests.Opposite_direction_transfers_between_the_same_pair_never_deadlock_and_balances_reconcile`
to test exactly that surfaced a real bug — just not the one being tested for.

**The bug:** the rate limiter on `/api/transfers` partitions by `httpContext.User.Identity?.Name`,
falling back to the caller's IP address. The JWT issued by this service never carries a
"name"-typed claim — only `sub` (customer id) and `role` — so `Identity.Name` is *always* null,
and the rate limiter *always* fell back to partitioning by IP. In the test, two different
customers hitting the endpoint from the same test-server connection immediately tripped a shared
30-requests/minute budget and got 429s, which looked at first exactly like a deadlock symptom
(requests failing under concurrent load) until the actual status code was inspected.

**Why it was unsafe:** in any real deployment, many customers sit behind the same NAT gateway or
mobile carrier IP. The rate limit was never actually per-customer — it was per-IP, silently,
meaning one customer's burst of activity (or a single scripted abuser) could throttle every other
customer transacting from the same network. This is the opposite of what "rate-limit the transfer
endpoint" is supposed to protect against.

**How it was caught:** a test written for one specific concurrency claim (no deadlocks) failed for
an unrelated reason (429s), and the fix was to actually read what status code came back rather
than assume the failure confirmed the hypothesis being tested. **Fix:** partition on the JWT's
`sub` claim directly instead of `Identity.Name`, so the limit is genuinely per-customer regardless
of network topology.
