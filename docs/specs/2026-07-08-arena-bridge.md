# Phase A — Arena: the oracle → sim bridge

The project's first live experiment. An LLM generates spell **kits** as proposal-stage JSON;
the strict parser (`SpellJson.Parse`) gates them; two AI fighters run them headlessly in the
tested combat sim; metrics answer a **pre-registered decision rule**. This harness and all
offline verification are built here. The **human** runs the live generation with their own API
key — the harness never has, needs, or asks for the key.

**The only variable under test is mechanical composition.** No fusion, no naming layer. Kits are
generated from archetype *briefs* ("a patient frost controller"); the question is whether an LLM,
given a compact prose vocabulary, composes valid, mechanically interesting spells.

---

## 1. Architecture

```
brief ─▶ oracle (LLM) ─▶ proposal JSON array ─▶ SpellJson.Parse (THE GATE) ─▶ kit
                                                      │ reject → one repair → discard+log
kit A, kit B ─▶ headless duel (Spellcraft.Sim, seeded) ─▶ event projection + metrics
                                                      │
                                              pre-registered decision rule ─▶ scorecard
```

- **`IOracle`** abstracts the LLM. `LiveAnthropicOracle` calls the Anthropic Messages API via
  `HttpClient`; `StubOracle` returns deterministic canned responses for tests. Offline (fight,
  tournament, all tests) the live oracle is never constructed.
- **The parser is the sole gate.** Unknown verbs, fields, or bands are hard failures by design
  (`SpellJson`), so no separate validation layer exists — the prose prompt guides, the parser is
  the law.
- **The sim is unmodified.** The bridge only calls public API: `Sim`, `Sim.Cast`, `Sim.Tick`,
  `WorldState.{AddEntity,Get,GetResource,CooldownRemaining,IsDead,Now}`, `SpellCompiler.Compile`,
  `SpellJson.Parse`, and reads `sim.Events.Events`.

---

## 2. Kit generation and the gate

- One oracle call renders `prompts/proposal-oracle.md` (placeholders `{{BRIEF}}` `{{TIER}}`
  `{{KIT_SIZE}}`) and returns a JSON array of `{{KIT_SIZE}}` proposal objects.
- **Exactly ONE repair attempt per spell.** A spell that fails `SpellJson.Parse` is re-prompted
  once with the parser error appended; the repaired spell is gated again; if it still fails it is
  **discarded and logged** to `arena/rejections.log` (brief, spell index, error, repaired?).
- Accepted spells form the kit (may be fewer than `{{KIT_SIZE}}`; empty kits are not persisted).
- `generate` prints **first-pass validity %** (spells accepted without repair ÷ spells generated)
  and **post-repair validity %** (spells accepted total ÷ spells generated), and **verb-usage
  marginals** (count of each verb across all accepted clauses).
- **Key handling:** `LiveAnthropicOracle` reads `ANTHROPIC_API_KEY` from the environment only.
  The key never appears in code, arguments, logs, or commits.

---

## 3. Arena constants (fixed)

- HP **300** each; Mana **250** each.
- Spawn **8 units apart on X**: fighter A at `(0,0,0)`, fighter B at `(8,0,0)`.
- Decision quantum **0.5 s**; timeout **90 s** sim-time (hard loop cap = 180 quanta).
- A fight ends on **death**, **timeout**, or **both-out-of-resources (oom)** — the end reason is
  recorded. (Resources never regenerate in the T0 slice, so oom = neither fighter can afford any
  spell in its kit; detected each quantum for an early, clean exit.)

## 3.1 The dumb policy (identical for both fighters)

Rotate through the kit in order; at each decision, scan from the rotation pointer for the first
**castable** spell (off cooldown *and* affordable), cast it, and advance the pointer past it. If
nothing is castable, wait one quantum. Targeting is purely by delivery:

- `self` → self; `targetUnit` / `projectile` → the enemy; `groundAoE` → the enemy's position.

The policy does not read stun/silence; a gated cast simply fails as a no-op that quantum (the sim
checks gates before spending, so no resource is wasted).

## 3.2 Metrics (all derived from the seeded event stream)

- **casts (per side):** successful `Sim.Cast` calls (`CastReport.Success`).
- **distinct verbs fired:** distinct verbs mapped from emitted events (`DamageDealt`→damage,
  `Healed`→heal, `ShieldGranted`→shield, `StatusApplied`→applyStatus, `Displaced`→displace,
  `ZoneSpawned`→spawnZone, `StatModified`→modifyStat, `Dispelled`→dispel) — i.e. verbs that
  actually *fired*, including via zone ticks.
- **statuses applied:** count of non-resisted `StatusApplied` events.
- **lead changes:** the HP difference `A.Hp − B.Hp` is sampled once per sim-second; a lead change
  is a sign flip between consecutive non-zero samples.
- **damage per side:** damage **taken** by each fighter (summed `DamageDealt.Amount` by target).
  Events carry no caster, so dealer-attribution is not available; damage-taken is the faithful,
  event-derived proxy and is data only (not used by the decision rule).
- **winner:** the fighter with higher HP at end (`draw` if equal); on death, the survivor.

---

## 4. Pre-registered decision rule (VERBATIM)

> first-pass validity ≥70%; median fight duration 15–90s sim-time; ≥3 distinct verbs firing per
> fight; ≥50% of fights show a lead change or status-driven swing (sign flip of the HP difference,
> sampled 1/s).

The `tournament` scorecard evaluates each criterion pass/fail. **First-pass validity is a
generate-mode metric**; in a purely offline tournament over hand-written kits there is no
generation pass, so that criterion is reported `n/a (offline)` and the other three are computed
from the fights.

---

## 5. Files delivered

- `prompts/proposal-oracle.md` — the single-authority oracle prompt (compact prose vocabulary).
- `src/Arena/` — console app (`generate` / `fight` / `tournament`), `IOracle` +
  `LiveAnthropicOracle` + `StubOracle`.
- `fixtures/kits/frost.json`, `fixtures/kits/ember.json` — hand-written, schema-valid,
  mechanically distinct kits so `fight`/`tournament` run fully offline.
- `tests/Arena.Tests/` — the offline verification suite.
- Kits are stored as a **JSON array of proposal objects**; the kit name is the file stem.

---

## 6. Verification (every claim from a command actually run, output pasted)

1. `dotnet test` green offline, including: prompt-vocabulary sync (drift-guard), parser gate +
   repair via `StubOracle`, determinism (same seed ⇒ identical projection), economy-edge (oom, no
   crash, no infinite loop), and a live-API test that auto-skips when `ANTHROPIC_API_KEY` is unset.
2. `fight` on the sample kits (seed 1) → summary block.
3. `tournament` on the sample kits (seeds 1..3) → scorecard.
4. seed-1 fight run twice, projections diffed → identical.
5. a scan for the Anthropic key prefix finds nothing in the tree; `git diff --stat` shows zero
   changes under `src/Spellcraft`, `schema/`, and existing tests.

---

## 7. Deviations and implementation choices (justified)

None redesign the fixed decisions; these are implementation clarifications required to honor the
"do not modify `src/Spellcraft` or existing tests" constraint.

1. **New test project `tests/Arena.Tests`** (not additions to `tests/Spellcraft.Tests`). Testing
   Arena needs a project referencing Arena; adding that reference would modify the existing test
   project. A separate project keeps `Spellcraft.Tests` byte-identical. Both run under `dotnet test`
   via the solution.
2. **Band-name sync by reflection.** The prompt-vocabulary drift-guard must assert the prompt
   contains every band name *defined by the C# tables*. Those band names are private dictionary
   keys in `BalanceTables`; exposing them publicly would modify `src/Spellcraft`. Instead the test
   reflects over `BalanceTables`' private `static Dictionary<string,float>` fields and unions their
   keys — reading the sim, not changing it, and catching real band drift.
3. **Conditional skip without a new package.** No new NuGet packages are allowed, and xUnit v2 has
   no built-in dynamic skip. A tiny `LiveApiFactAttribute : FactAttribute` sets `Skip` in its
   constructor when `ANTHROPIC_API_KEY` is unset — a true skip when unset, a real run when set.
4. **`damage per side` = damage taken** (see §3.2) — events carry no dealer; this is honest and
   rule-irrelevant.
5. **Scorecard criterion 1 offline** is `n/a` (see §4).
6. **The gate is Parse *then* Compile.** `SpellJson.Parse` validates the vocabulary (unknown
   verbs / statuses / elements / fields are hard failures), but *band* validity is enforced at
   compile time (`BalanceTables` lookups inside `SpellCompiler.Compile`) — an unknown band only
   throws once compiled. So a spell is accepted only if it both parses and compiles; both steps
   together are the sim's strict front door. No separate hand-rolled validator is added.

No `src/Spellcraft`, `schema/`, `fixtures/*.proposal.json`, or existing test is modified.

---

## 8. Post-audit refinements

An audit of the first live run surfaced fixes folded in here:

1. **`modifyStat` stat names + drift guard.** The prompt now names every legal stat (`moveSpeed`, `castSpeed`, `damageOut`, `damageIn`, `armor`, `resist`, `critChance`, `critMult`, `evasion`, `lifesteal`), and `PromptVocabularySyncTests` asserts every `StatKind` name appears. Without it the oracle guessed `speed`/`attackSpeed` — hard failures that also bias the verb marginals Phase B baselines on.
2. **Both orderings per pair.** First-mover advantage is decisive, so the tournament plays (A,B) and (B,A) for every pair and seed; winner / duration / end-reason no longer depend on filename order.
3. **Distinct-outcome count.** The scorecard reports distinct outcomes (ignoring seed) beside the fight count — with crit/evasion at zero, replicate seeds produce byte-identical fights and must not inflate the read. All rows stay in the CSV.
4. **Bidirectional band guard.** The sync test also asserts every band the prompt *declares* exists in the reflected tables (an invented band would otherwise pass and only fail live).
5. **Recursive verb marginals.** `generate` counts verbs inside `tickClauses` and template clauses, not just top-level — so zone and setup archetypes aren't undercounted in the baseline.
6. **`spawnZone` has no `share`** is stated in the prompt (the second observed rejection class), and a stray root duplicate of the prototype HTML was removed (canonical copy under `prototype/`).
