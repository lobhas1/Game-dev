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

Modes: `generate --brief "…" --tier N --kit-size 3 --count M [--model …] [--out …]`
· `fight <kitA> <kitB> --seed S` · `tournament <kitsDir> [--same-tier] [--hp-scale k] [--mana N] --seeds A..B`
· `sweep <kitsDir> --seeds A..B --hp-scales 3,4,5,6,8` · `evaluate <kitsDir>`.

**The engine phase begins only when a committed verdict doc (`docs/experiments/<timestamp>-phase-a-verdict-v4.md`) reads `OVERALL: PASS`.** v3 was FAIL (median 13.9s, under the 15s floor) — the `ifStatus 'major'` amplify (×2.5) kills faster than the window allows. v4 makes that multiplier a run config and, after an offline sweep, registers **amplifyMajor = 1.75** (`hpScale` stays 8) — see [the v4 spec](docs/specs/2026-07-10-phase-a-v4.md).

### Phase A v4 verdict run (human)

Offline sanity first — `dotnet test` all green, and the fixture duel still reads as an exchange:
```
dotnet run --project src/Arena -- fight fixtures/kits/frost.json fixtures/kits/ember.json --seed 1
```

1. **Archive the v3 corpus** with its results, out of the way:
   ```
   git mv arena/kits arena/kits-v3-archive
   cp arena/results.csv arena/kits-v3-archive/results.csv      # PowerShell: copy
   ```
2. **Amplify constant is already registered.** The offline amplify sweep is committed and the human picked **amplifyMajor = 1.75** from it (median 18.3s, every tier-class in-window), now in the [v4 spec](docs/specs/2026-07-10-phase-a-v4.md). To reproduce it (EXPLORATORY — no verdict):
   ```
   dotnet run --project src/Arena -- sweep arena/kits-v3-archive --seeds 1..5 --hp-scale 8 --amplify-majors 2.5,2.25,2.0,1.75,1.5
   ```
   Read `docs/experiments/2026-07-10-amplify-sweep.md`. The tournament commands below carry `--hp-scale 8 --amplify-major 1.75`.
3. **Set a spend-capped key** (never in code, args, logs, or commits):
   ```
   $env:ANTHROPIC_API_KEY="…"          # bash: export ANTHROPIC_API_KEY=…
   ```
4. **Generate ~30 fresh v4 spells.** The prompt is UNCHANGED (sha `cd1bcf68…`), so these rows pool with v3's under C1 — the verdict shows a per-run table:
   ```
   dotnet run --project src/Arena -- generate --brief "a patient frost controller"                --tier 1 --kit-size 3 --count 2
   dotnet run --project src/Arena -- generate --brief "a reckless fire skirmisher"                 --tier 1 --kit-size 3 --count 2
   dotnet run --project src/Arena -- generate --brief "a defensive earth warden"                   --tier 2 --kit-size 3 --count 2
   dotnet run --project src/Arena -- generate --brief "a hit-and-run wind duelist"                 --tier 2 --kit-size 3 --count 2
   dotnet run --project src/Arena -- generate --brief "a shadow assassin built on setup and burst" --tier 3 --kit-size 3 --count 2
   ```
5. **Ladder** (informational, full corpus — capture the printed T×vT× table):
   ```
   dotnet run --project src/Arena -- tournament arena/kits --hp-scale 8 --amplify-major 1.75 --seeds 1..5
   ```
6. **Criterion corpus** (same-tier only — this is the `results.csv` that `evaluate` scores; run it LAST so the full-corpus run above does not clobber it):
   ```
   dotnet run --project src/Arena -- tournament arena/kits --same-tier --hp-scale 8 --amplify-major 1.75 --seeds 1..5
   ```
7. **Render the verdict:**
   ```
   dotnet run --project src/Arena -- evaluate arena/kits
   ```
   Read the written `docs/experiments/<timestamp>-phase-a-verdict-v4.md` (timestamped, never overwrites history).
8. **Commit everything** — fresh kits, `arena/generation.log`, `arena/rejections.log`, `arena/results.csv`, the ladder doc, and the verdict doc — with a commit message that **states the OVERALL result** (past runs shipped a `<what evaluate printed>` placeholder and a `verfication and test` message — do not repeat that). Push.
9. `OVERALL: PASS` → the engine gate opens. `FAIL` → tune nothing; the doc's data-only diagnosis plus raw data go to review. `INCOMPLETE` → the doc names what is missing (an unrecorded kit, or no generation rows matching the current prompt hash).
