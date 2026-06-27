# Changelog

All notable changes for `ccopto/basta-backend`.

## Categories

- **Improvements**: user-facing features, Epic functionality, architecture improvements, and new capabilities.
- **Fixes**: bug fixes, security fixes, correctness fixes, and reliability repairs.
- **Patches**: documentation, tests, CI, dependency maintenance, release markers, and other non-feature maintenance work.

## `v1.0.2` - 2026-06-27

Audit remediation fixes for validation logic.

**Fixes**

- Treated blank and single-character answers as final invalid without requiring peer review.

**Patches**

- Added tests for blank and short answer handling.

## `v1.0.1` - 2026-06-26

Release documentation maintenance.

**Patches**

- Added the backend repository changelog.
- Started tracking backend agent instructions.
- Documented release-tag SemVer rules in the backend agent instructions.
- Updated `.gitignore` so `AGENTS.md` can be versioned.

## `v1.0.0` - 2026-06-25

Final target release tag for the backend repository.

**Patches**

- Tagged the final target release on the same commit as `v0.13.2`: `fbce73b6a7133d0d41974e0fd1e680506651c847`.

## `v0.13.2` - 2026-06-25

Security, game-logic, reconnect, and dependency review fixes.

**Fixes**

- Secured SignalR group joins with player validation.
- Added reconnect-safe disconnect handling.
- Fixed scoring uniqueness by grouping on category and answer.
- Unified game language handling across create/update paths and persistence.
- Made answer submission DB-first for better atomicity.
- Pinned the SQLitePCLRaw bundle dependency to address the NU1903 vulnerability.

## `v0.13.1` - 2026-06-25

Bug 02 backend timer fix.

**Fixes**

- Safely injected `IHubContext<BastaHub>` to prevent `ObjectDisposedException` during background timer execution.

**Patches**

- Persisted the latest category validation migration.

## `v0.13.0` - 2026-05-11

Epic 13 intelligent dictionary validation.

**Improvements**

- Added server-side two-phase dictionary validation.
- Integrated Hunspell-backed English and Spanish word validation.
- Added bundled proper-noun datasets for countries, cities, names, and animals.
- Added peer-review fallback for answers that fail dictionary validation.
- Added game language persistence, category validation types, and answer dictionary status.

**Patches**

- Added the related EF Core migrations.
- Updated tests for dictionary validation behavior.

## `v0.12.0` - 2026-05-10

Epic 10 backend testing.

**Patches**

- Added GamesController boundary validation coverage.
- Added full game-loop integration test coverage, including the five-player cap.

## `v0.11.0` - 2026-05-10

Epic 9 backend hardening.

**Improvements**

- Configured SignalR keep-alive and timeout intervals.
- Enabled detailed SignalR errors in development.

**Patches**

- Added tests for the five-player session cap.

## `v0.10.0` - 2026-05-10

Epic 8 backend internationalization.

**Improvements**

- Persisted session language in the in-memory game session.
- Wired preferred language through session creation and lobby snapshots.

**Patches**

- Added test coverage for localized categories via `?lang=en|es`.

## `v0.9.0` - 2026-05-07

Epic 7 backend game-over and leaderboard support.

**Improvements**

- Added leaderboard DTOs.
- Implemented leaderboard calculation with shared-rank tie handling.
- Broadcast final leaderboard data when the game ends.

**Patches**

- Added ranking and hub-event tests.

## `v0.8.1` - 2026-05-01

Lobby hang backend fix.

**Fixes**

- Made SignalR lobby player registration defensive and idempotent.
- Ensured SignalR connection can recover player registration even when the REST join call is delayed.

**Patches**

- Added hub tests for defensive registration.

## `v0.8.0` - 2026-04-22

Epic 6 backend scoring and validation.

**Improvements**

- Added round validation progress tracking.
- Implemented validation submission and all-answers-submitted checks.
- Persisted validation updates through the game operation service.
- Added scoring calculation and cumulative score updates.
- Coordinated SignalR transitions between gameplay, validation, and results.

## `v0.7.0` - 2026-04-22

Epic 5 backend gameplay completion.

**Improvements**

- Added round-lock grace period handling.
- Implemented game-over behavior for completed rounds or exhausted letter pool.
- Synchronized round increments and state broadcasts.

**Patches**

- Added tests for letter uniqueness, grace-period submissions, and game-over broadcasting.

## `v0.6.0` - 2026-04-21

Epic 4/5 backend SignalR contract reconciliation.

**Improvements**

- Added atomic session settings updates for rounds, timer, and categories.
- Renamed and expanded the hub settings update method to match frontend calls.
- Stabilized `SubmitAnswers` and `CallBasta` around connection context.

**Patches**

- Added service and hub tests for the updated contract.

## `v0.5.0` - 2026-04-18

Backend quality gates.

**Patches**

- Added hub tests for SignalR event broadcasts and session locking.
- Expanded game operation service tests for join and submit-answer workflows.

## `v0.4.0` - 2026-04-18

Epic 5 backend gameplay loop.

**Improvements**

- Added round state, current letter, answer, and lock tracking.
- Implemented thread-safe round management and letter picking.
- Added `RoundAnswer` persistence.
- Added SignalR methods for starting rounds, calling Basta, and submitting answers.
- Added server-driven timer support.

**Patches**

- Added EF Core migrations.

## `v0.3.0` - 2026-04-18

Epic 4 backend game setup.

**Improvements**

- Added category persistence with English and Spanish names.
- Seeded default categories.
- Added `GET /api/categories?lang=en|es`.
- Added selected categories to game sessions.
- Added start-game validation requiring at least one selected category.

## `v0.2.0` - 2026-04-15

Epic 3 backend lobby and join flow.

**Improvements**

- Added thread-safe player registration and nickname validation.
- Added lobby snapshot generation.
- Added REST endpoints for joining and reading lobby state.
- Added SignalR lobby group membership, lobby updates, host-only start game, and disconnect handling.

## `v0.1.0` - 2026-04-15

Epic 2 backend game creation.

**Improvements**

- Added game creation API foundation, DTOs, entities, EF Core context, and migrations.
- Added in-memory game session service and persistent game operation service.

**Patches**

- Added tests for controller, operation service, and session service behavior.
- Context source: PR title plus Epic 2 user stories and merge diff.
