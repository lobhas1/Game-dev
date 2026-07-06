# Headless Combat Sim — Slice Spec v0.1

**Acceptance.** If it can't run §8 of `verbs.md` from JSON — Blink Strike, Gravity Well, and Frost Shatter, parsed and executed end to end — it isn't a reference implementation of the spec. Every decision below serves that one sentence.

---

## 1. Reframe

This is not a verbs library. It is a headless, text-only combat sim (two fighters). The reference implementation of `verbs.md` is the *interpreter that runs spells* — the resolution pipeline, the clock, and the trigger phase — not the bag of functions it dispatches to. The 29 verbs are the cheap part. This slice builds the interpreter plus the Tier-0 eight verbs, and proves it by executing the spec's own §8 examples.

---

## 2. Architecture

### 2.1 Spells are data, not code
A `Clause` is a serializable record — `{ verb, <flat params>, template? }` — and a dispatcher maps `verb → function`. Delegates are internal dispatch machinery only, never a clause's shape. This is forced by the spec's premise: the oracle emits JSON, the compiler prices it, canon stores it, it travels on the wire, and a delegate round-trips through none of that. Clauses **nest**: `spawnZone.tickClauses` and a template's `also` are arrays of clauses, so the JSON converter is recursive from day one — otherwise Gravity Well and Frost Shatter do not parse.

### 2.2 The seam is cut at rules-vs-engine
- **`WorldState`** (owned, in-file, *the model*): health, death state, shields, statuses, marks, resources, cooldowns / ability state, stat modifiers, XZ positions, faction membership, entity registry. Everything rules-shaped, and everything the pipelines read and write. This cut is what makes the damage pipeline testable in isolation.
- **`IWorldApi`** (the engine seam, shrunk): only the genuinely un-scaffoldable — `NavClamp` (a documented identity in the headless build), entity spawn, faction / AI ops. Nothing rules-shaped hides behind it.
- Evasion and crit are rolls over `WorldState` stats through `ctx.Rng`, in the file — not behind the seam. "Heal refuses corpses" is checked against `WorldState` death state.

### 2.3 Two-stage schema
- **Proposal stage** (oracle): qualitative bands (`"short"`, `"medium"`, `"tiny"`, `"fast"`) and `share` fractions only. No raw numbers — no `amplify: 2.5`, no `tickInterval: 0.25`.
- **Compiled stage** (compiler): carries `id` and `tier` at the root and adds resolved numbers alongside the shares, per the v0.2 amendment.
- The sim consumes the **compiled** stage. The compiler resolves proposal → compiled through the balance tables (§2.5). Golden path: proposal JSON → strict parse → `compile(tier)` → sim.

### 2.4 Resolution pipeline + clock + guards
- Full six-step pipeline (§2 of `verbs.md`): cost/gate → cast → delivery → clause loop (hit/evasion → shields absorb before health → verb executes → result recorded) → trigger phase (§7 templates enqueue follow-ups) → event flush.
- **Fixed-timestep clock:** time accumulates into 50 ms quanta; DoTs, HoTs, zones, DR windows, and cooldowns tick on quantum boundaries. Variable `dt` is banned — it makes tick counts cadence-dependent and goldens flaky by construction.
- **Guards at one enforcement point** (the trigger phase): trigger depth ≤ 3, ≤ 12 enqueued clauses per cast, no clause re-triggers its own template, zones may not spawn zones past depth 1. Never scattered per-verb, where enforcement drifts.

### 2.5 Balance authority
All band tables — sizes, durations, rates, the tier-budget curve, delivery multipliers (§4), cast multipliers (§3) — live in one named place, `BalanceTables`. That table *is* the balance authority in code form: `Magnitude = tierBudget(tier) × share × multipliers`, with durations / sizes / rates resolved from their bands.

### 2.6 Spatial-lite (not no-op)
XZ positions in `WorldState`; deterministic analytic **line-sweep** (projectile → nearest hit) and **circle-overlap** (groundAoE / nova → units within radius) implemented in the sim; `NavClamp` a documented identity. Without this, Blink Strike's projectile hits nothing and the golden is vacuous.

### 2.7 Deterministic RNG with forked substreams
`SeededRng` is a documented deterministic PRNG seeded per cast (a stable hash, never `string.GetHashCode`, which is per-process randomized). Named substreams — `Fork("crit")`, `Fork("evasion")` — so adding a new roll type does not shift every subsequent draw and invalidate all goldens.

### 2.8 Strict deserialization
An unknown verb, status, or field is a hard parse failure, never silently ignored. Deserialization is where the closed vocabulary becomes law.

---

## 3. Scope of this slice

- **Verbs (Tier-0 eight):** `damage`, `heal`, `shield`, `applyStatus`, `dispel`, `modifyStat`, `displace`, `spawnZone`.
- **Deliveries:** `self`, `targetUnit`, `projectile`, `groundAoE`. **`melee` is consciously dropped** — a knowing deviation from §9 step 1; no §8 proof needs it.
- **Cast modes:** `instant`, `cast` (cost/gate + windup advanced on the clock).
- **Templates (§7):** parse-validate all eight; **execute `ifStatus` and `onHit`**; the other six (`onKill`, `onCrit`, `delayed`, `repeating`, `onExpire`, `hpThreshold`) are stubbed at execution and clearly marked. Frost Shatter needs the first; `onHit` is the workhorse.
- **Statuses:** all 20 present as catalog data; the DoT / DR / stat-mod machinery is driven by the clock. Only the categories this slice exercises are on the hot path.
- **Widen (out of slice):** the other 21 verbs, more deliveries and cast modes, the six stubbed templates, `castSpell`'s cross-tier re-resolve, and wiring JSON-Schema validation into CI.

---

## 4. §8 errata — the examples are fixtures, not spec

The three §8 listings mix proposal- and compiled-stage fields: `amplify: 2.5`, `tickInterval: 0.25`, and `castTime: 0.4` are raw numbers that do not belong in a proposal-stage descriptor. The conformance fixtures correct these prose-mode accidents — fixtures are authored at the **proposal** stage (bands + shares), and the compiler produces the numbers. The corrected fixtures and the normative JSON Schema are the contract; the prose examples are not. §8 gets errata rights.

---

## 5. Deliverables

- This spec.
- `schema/spell-proposal.schema.json` and `schema/spell-compiled.schema.json` — normative, closed-vocabulary.
- `fixtures/*.proposal.json` — corrected Blink Strike, Gravity Well, Frost Shatter.
- `src/Spellcraft/CombatModel.cs` — nouns only (value types, closed-list enums, `WorldState`, `IWorldApi`, `StatusCatalog`, `BalanceTables`, descriptor model, events). No behavior.
- `src/Spellcraft/CombatSim.cs` — verbs + interpreter (budget resolver, strict recursive converter, dispatcher, pipeline, delivery, cast, templates, clock, guards, geometry, sim runner).
- `tests/Spellcraft.Tests/` — xUnit (`dotnet test`, CI-able). Golden tests assert a **canonical event projection plus final-state invariants** — never raw exact sequences, which rot into regenerate-on-red reflexes. Runs the three §8 proofs from JSON. Unit tests cover the hard-CC DR curve (100 → 50 → 25 → immune), shields-before-health ordering, and DoT tick counts under the fixed clock.

---

## 6. Layout

```
docs/specs/2026-07-06-headless-combat-sim.md
schema/spell-proposal.schema.json
schema/spell-compiled.schema.json
fixtures/blink-strike.proposal.json
fixtures/gravity-well.proposal.json
fixtures/frost-shatter.proposal.json
src/Spellcraft/Spellcraft.csproj
src/Spellcraft/CombatModel.cs      # nouns
src/Spellcraft/CombatSim.cs        # verbs + interpreter
tests/Spellcraft.Tests/Spellcraft.Tests.csproj
tests/Spellcraft.Tests/GoldenTests.cs
tests/Spellcraft.Tests/UnitTests.cs
Spellcraft.sln
```
