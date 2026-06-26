# AGENTS.md — Basta! Backend

This file is read by all AI coding agents (Antigravity, Claude Code, OpenCode, etc.) working on this repository.
For project-wide conventions, also see [AGENTS.md in the monorepo](https://github.com/ccopto/basta).

---

## Stack

- **.NET 10** / **ASP.NET Core Web API**
- **Entity Framework Core 10** with **SQLite**
- **SignalR** for real-time game events
- **xUnit** + **Moq** + **FluentAssertions** for testing

---

## macOS Environment Quirks

### `dotnet` is NOT on PATH

Always use the full path:

```bash
/usr/local/share/dotnet/dotnet build
/usr/local/share/dotnet/dotnet test --verbosity normal
/usr/local/share/dotnet/dotnet ef migrations add <Name>
```

### GPG Signing

All commits must be GPG signed. If signing fails, stop and inform the user. Do NOT use the `-c commit.gpgsign=false` override.

---

## Architecture Rules

### Thin Controllers
Controllers are **HTTP adapters only** — they validate input, delegate to a service, and return a response.
Business logic (DB transactions, orchestration, in-memory state) belongs in the **Services layer**.

```
GamesController  →  IGameOperationService  →  IGameSessionService + DbContext
```

### Service Lifetimes
| Service | Lifetime | Reason |
|---|---|---|
| `IGameSessionService` | Singleton | Holds in-memory game state across all requests |
| `IGameOperationService` | Scoped | Wraps a DbContext transaction per request |
| `BastaDbContext` | Scoped | Standard EF Core pattern |

### Database Transactions
Any operation that touches multiple DB entities must be wrapped in a transaction:
```csharp
await using var transaction = await _context.Database.BeginTransactionAsync();
try { /* ... */ await transaction.CommitAsync(); }
catch { await transaction.RollbackAsync(); throw; }
```

### Auto-Migrations — Development Only
`db.Database.Migrate()` at startup is guarded behind `IsDevelopment()`.
For production, add a `dotnet ef database update` step in the CI/CD pipeline.

---

## Testing Conventions

- **Controller tests**: mock `IGameOperationService` (no EF dependency in controller tests).
- **Service integration tests**: use `UseInMemoryDatabase` + suppress `TransactionIgnoredWarning`:

```csharp
.UseInMemoryDatabase(Guid.NewGuid().ToString())
.ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
```

- **GameSession service tests**: assert code regex against the restricted charset `^[ABCDEFGHJKLMNPRSTUWXY]{4}$`.

---

## Git Workflow

### Branch Strategy
```
master    ← production
develop   ← integration (PRs target this)
feature/* ← short-lived
```

### ⚠️ Atomic Commits — CRITICAL
One commit per logical change. Never batch all PR review fixes into a single commit.

```bash
# Good
git commit -m "fix(thread-safety): replace Random with Random.Shared"
git commit -m "fix(atomicity): wrap CreateGameAsync in a DB transaction"

# Bad
git commit -m "fix: address all review comments"
```

### Conventional Commits
`feat`, `fix`, `refactor`, `test`, `chore`, `docs`

### Release Tag SemVer Rules

Release tags use SemVer with a `v` prefix: `vX.Y.Z`.

- `X` is the major version. The current final target is `v1.0.0`.
- Before the final target, all project history tags must remain below `v1.0.0`.
- `Y` increments sequentially for feature/epic functionality in chronological merge order. The Epic number is a planning guideline only; do not map Epic numbers directly to minor versions.
- `Z` increments for fixes and maintenance changes, including `fix`, `test`, `chore`, and `docs`.
- If a fix or maintenance PR merges before the first feature/epic tag, use `v0.0.Z`.
- Version each repository independently: parent, backend, and frontend can have different current `0.Y.Z` values.
- Tags should point at the merge commit on `develop` for the PR they represent.
- When the final target is reached, tag the current `develop` commit with both the latest pre-`1.0.0` tag and `v1.0.0`.

---

## Game Code Generator

The 4-letter game session code uses a **restricted character set** (excludes visually ambiguous characters):
```
ABCDEFGHJKLMNPRSTUWXY   (no I, O, Q, V, Z)
```
This gives ~194k unique codes — sufficient for a casual multiplayer game.
