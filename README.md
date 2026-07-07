# Spellcraft — headless combat sim

A reference implementation of the closed-vocabulary spellcraft system specified in
[verbs.md](verbs.md): an oracle composes spells from a fixed symbol set as JSON, a
compiler prices budget shares into numbers, and the engine executes. **Spells are data,
never code** — nothing is written by an LLM at runtime.

This is the first vertical slice — the *interpreter* plus the Tier-0 verbs — proven by
executing the spec's own §8 composition proofs from their JSON descriptors.

## Layout

| Path | What |
|---|---|
| [verbs.md](verbs.md) | The spell verb specification (source of truth) |
| [docs/specs/](docs/specs/) | The slice design spec |
| [docs/specs/2026-07-07-oracle-naming-doctrine.md](docs/specs/2026-07-07-oracle-naming-doctrine.md) | Oracle naming doctrine — the single authority for the fusion-naming prompt |
| [schema/](schema/) | Normative two-stage JSON Schema (proposal → compiled) |
| [fixtures/](fixtures/) | The §8 composition proofs as proposal-stage descriptors |
| [prototype/spellcraft-oracle.html](prototype/spellcraft-oracle.html) | Single-file combination-oracle prototype (the LLM that names two-spell fusions) |
| `src/Spellcraft/CombatModel.cs` | Nouns: value types, closed vocabulary, `WorldState`, catalog, balance tables |
| `src/Spellcraft/CombatSim.cs` | Verbs + interpreter: parser, compiler, pipeline, clock, triggers |
| `tests/Spellcraft.Tests/` | xUnit golden + unit tests |
| [docs/specs/2026-07-08-arena-bridge.md](docs/specs/2026-07-08-arena-bridge.md) | Arena bridge spec (Phase A: oracle → gate → duel → decision rule) |
| [prompts/proposal-oracle.md](prompts/proposal-oracle.md) | Single-authority oracle prompt for kit generation |
| `src/Arena/` | The oracle → sim bridge console app: `generate` / `fight` / `tournament` |
| [fixtures/kits/](fixtures/kits/) | Hand-written sample kits (`frost`, `ember`) for offline fight/tournament |
| `tests/Arena.Tests/` | Arena offline verification (gate + repair, determinism, economy, prompt-sync) |

The oracle's naming prompt has one home — the naming-doctrine spec — and any file embedding it (the prototype today, a C# oracle bridge later) must carry the doctrine's load-bearing sentences verbatim; `OracleDoctrineSyncTests` fails the build if they drift.

## Build & test

Requires the .NET 8 SDK.

```
dotnet test
```

## Status

Tier-0 slice. Implements the eight Tier-0 verbs (`damage`, `heal`, `shield`, `applyStatus`,
`dispel`, `modifyStat`, `displace`, `spawnZone`), the §2 resolution pipeline, a fixed-timestep
clock, the §7 trigger framework (`ifStatus` and `onHit` executing; the other six parse-validate),
forked-substream deterministic RNG, and strict closed-vocabulary parsing. The three §8 proofs —
Blink Strike, Gravity Well, Frost Shatter — run end to end from JSON. Run `dotnet test` for the
current suite status.

## Arena — the oracle → sim bridge

Phase A's live experiment ([spec](docs/specs/2026-07-08-arena-bridge.md)): an LLM generates spell
kits as proposal JSON, the strict parser gates them, two AI fighters run them headlessly, and a
scorecard answers a pre-registered decision rule. The offline harness (`fight`, `tournament`, all
tests) needs no network and no key; only `generate` calls the API, with the key read solely from
`ANTHROPIC_API_KEY` (never in code, args, logs, or commits). The prompt at
[prompts/proposal-oracle.md](prompts/proposal-oracle.md) is the single vocabulary authority —
`PromptVocabularySyncTests` fails the build if it drifts from the C# verbs/deliveries/bands.

Modes: `generate --brief "…" --tier N --kit-size 3 --count M [--model claude-sonnet-4-6] [--out …]`
· `fight <kitA.json> <kitB.json> --seed S` · `tournament <kitsDir> --seeds A..B` · `evaluate <kitsDir>`.

**The engine phase begins only when a committed verdict doc (`docs/experiments/<date>-phase-a-verdict.md`) reads `OVERALL: PASS`.**

### Phase A verdict run (human)

Offline sanity first — `dotnet test` all green, and an offline duel reads as an exchange:
```
dotnet run --project src/Arena -- fight fixtures/kits/frost.json fixtures/kits/ember.json --seed 1
```
(Red flags: a kill in under 5 s, or an endless nothing-castable loop.) Then the live run:

1. Archive the pre-fix corpus so it isn't mixed in:
   ```
   git mv arena/kits arena/kits-prefix-archive
   ```
2. Set a spend-capped key (never in code, args, logs, or commits):
   ```
   $env:ANTHROPIC_API_KEY="…"          # bash: export ANTHROPIC_API_KEY=…
   ```
3. Generate ~30 spells — `--count 2 --kit-size 3` per brief (cost on the order of cents):
   ```
   dotnet run --project src/Arena -- generate --brief "a patient frost controller"                --tier 1 --kit-size 3 --count 2
   dotnet run --project src/Arena -- generate --brief "a reckless fire skirmisher"                 --tier 1 --kit-size 3 --count 2
   dotnet run --project src/Arena -- generate --brief "a defensive earth warden"                   --tier 2 --kit-size 3 --count 2
   dotnet run --project src/Arena -- generate --brief "a hit-and-run wind duelist"                 --tier 2 --kit-size 3 --count 2
   dotnet run --project src/Arena -- generate --brief "a shadow assassin built on setup and burst" --tier 3 --kit-size 3 --count 2
   ```
   Watch the printed first-pass validity; on surprises read `arena/rejections.log`. Each run appends one line to `arena/generation.log`.
4. Tournament (both orderings per pair, seeds 1–5):
   ```
   dotnet run --project src/Arena -- tournament arena/kits --seeds 1..5
   ```
5. Render the verdict:
   ```
   dotnet run --project src/Arena -- evaluate arena/kits
   ```
   Then read `docs/experiments/<date>-phase-a-verdict.md`.
6. Commit **everything** — kits, `arena/generation.log`, `arena/rejections.log`, `arena/results.csv`, and the verdict doc — with real commit messages, then push.
7. `OVERALL: PASS` → the engine gate is open. `FAIL` → tune nothing; the doc's "Diagnosis (data only)" section plus the raw data go to review first. `INCOMPLETE` → the doc names exactly what is missing (an unrecorded kit, or no generation rows matching the current prompt hash — usually a prompt edit made after generating).
