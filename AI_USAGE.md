# AI Usage

This entire service was built in a single pair-programming session with **Claude Code** (Claude
Sonnet 5), used as an active collaborator across the whole task: reading an existing internal
reference codebase to extract its architectural conventions, scaffolding the solution, writing the
domain/application/infrastructure/API code, generating the EF Core migration, writing and
debugging both test projects against a real Postgres instance, and writing this documentation.

The brief is explicit that judgment in directing the AI — including catching where it was wrong —
is what's being assessed, so this file documents real prompts from this session and a real
correctness bug the AI introduced and then caught by actually running the tests, not a
hypothetical one.

## Representative prompts and what came back

**1. "Read through the architecture in [a separate internal reference project] and build me a
.NET 8 API following the same structure, and tell me if it's a good architecture."**
The AI first spawned a research pass over the reference codebase (BuildingBlocks layering, the
`Result`/`Error` pattern, MediatR + pipeline behaviours, the `Carter`-based endpoint style, the
`BaseDbContext` concurrency-token convention, module DI registration conventions) and reported back
a concrete assessment before writing any code — including that the reference repo's own
transactional-outbox tables were defined but never actually wired up anywhere, a gap worth knowing
before copying the pattern verbatim. That assessment shaped what got carried over into this
service (the `Result`/CQRS/DbContext conventions) versus what was deliberately left out (the
unused outbox scaffolding — see README's "Stretch goals" section).

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
