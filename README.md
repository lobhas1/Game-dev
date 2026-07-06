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
