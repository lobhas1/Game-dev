# Phase B — fusion emits mechanics

Phase A is closed: **OVERALL PASS**
([docs/experiments/2026-07-08-104144-phase-a-verdict-v4.md](../experiments/2026-07-08-104144-phase-a-verdict-v4.md)),
config hpScale 8 / mana 250 / amplifyMajor 1.75. The oracle composes valid, well-paced kits from
briefs. Phase B asks the next question: when two spells **fuse** into a named concept (the
[naming doctrine](2026-07-07-oracle-naming-doctrine.md), prototype committed), can the oracle also
emit **mechanics** for that concept such that **meaning and mechanics cohere**?

## Pipeline

```
parents (proposal JSON, bundled with naming metadata)
   → naming oracle        → concept {name, element, tags, flavor, why}
   → fusion-mechanics oracle → ONE proposal-stage spell (clauses that express the concept)
   → the existing strict gate (SpellJson.Parse + SpellCompiler.Compile), one repair allowed
   → castable spell + audit record
```

A **seed / parent card** bundles meaning and mechanics: `{name, emoji, element, tags, flavor,
spell}` where `spell` is a gate-clean proposal-stage spell (its `clauses` are the parent's
mechanics, its top-level fields are the naming line). The prototype's `SEEDS` are meaning-only
cards; Phase B gives each a real T0-verb body. A **depth-2 parent** is a fusion record loaded the
same way — but its ingredient identity (name, element, tags, flavor) is read from the record's
`concept` block, not its top-level `name` (which is the kebab file-id, e.g. `steam`), so the naming
line reads `"Steam"`, not `"steam"`. Six tier-1 seeds, mechanically distinct:

| seed | element | parent tag | body (T0 verbs) |
|---|---|---|---|
| Ember | fire | damage | fire `damage` + `applyStatus` burn |
| Droplet | water | heal | `heal` |
| Pebble | earth | ward | `shield` |
| Gust | air | movement | `displace` push |
| Glimmer | light | perceive | `modifyStat` critChance buff |
| Umbra | shadow | conceal | `applyStatus` blind |

## Pre-registered criteria (thresholds never move)

- **F1 gate validity:** ≥70% of fusion-mechanics proposals pass the gate first-try (one repair
  allowed, repairs logged, discards logged).
- **F2 semantic coverage:** over all gated fusions, mean tag-coverage ≥70% AND strictly greater
  than the decoy baseline. Tag-coverage of one fusion = fraction of its concept's MAPPABLE tags
  that have ≥1 matching mechanic in its clauses, per the mapping table below. Decoy baseline = the
  same concepts scored against a derangement (shuffled, non-self) of the other fusions' clause
  lists.
- **F3 human blind matching:** a stranger sees concept + two clause lists (real vs same-tier decoy,
  sides randomized) for 20 trials; ≥14/20 correct. PENDING until run — the verdict doc carries F3
  as PENDING and OVERALL as INCOMPLETE until the human result is appended.

### Tag → mechanics mapping (fixed BEFORE any live call)

- **damage** → any damage clause
- **heal** → heal
- **ward** → shield OR armor/resist buff
- **control** → applyStatus of freeze/stun/root/slow/chill/fear/silence/disarm OR displace pull
- **movement** → displace
- **conceal** → applyStatus stealth/blind OR a zone
- **area** → groundAoE delivery OR spawnZone
- **duration** → any duration medium/long OR a zone
- **summon / transform / perceive** → UNMAPPABLE in the T0 slice: excluded from the coverage
  denominator, but COUNTED and reported as **"quantization pressure"** (tags demanding verbs the
  engine lacks — the widening signal, by design).

A tag is matched by walking the gated spell's delivery type and every clause, recursing into
`spawnZone` tick/enter/exit clauses and template `also`/trigger clauses. `armor/resist buff` =
`modifyStat` on `armor` or `resist` with `direction: buff`. `a zone` = any `spawnZone` clause.
`any duration medium/long` = any clause carrying a `duration` band of `medium` or `long`.

## Tier law

`tier(a, b) = a.tier == b.tier ? min(3, a.tier + 1) : max(a.tier, b.tier)`. Equal tiers ascend by
one; unequal keep the higher; the T0 slice caps at **tier 3** (the doctrine's cap-5 is out of
slice). The tier is computed in code and passed to BOTH oracle prompts as `{{TIER}}`; the mechanics
spell must emit exactly that tier. **The gate enforces it:** a spell whose `tier` ≠ the law's tier
fails the gate with `tier mismatch: law requires N` and flows into the one-repair path (wrong tier
once → repaired; twice → discarded), never a silent overwrite. Tier drives the power budget and the
same-tier decoy pairing, so an unchecked tier would corrupt F1, F2, and the quiz. The record stores
the tier-law result as authoritative.

## Decoy construction (F2 and F3)

- **F2 decoy baseline:** order the gated fusions by name; score concept *i* against the clause list
  of fusion `(i + 1) mod n` — a cyclic shift by one, which is fixed-point-free (a valid derangement)
  for n ≥ 2, deterministic, and RNG-free. The mean of these mismatched-coverage scores is the
  baseline F2 must strictly beat.
- **F3 quiz decoy:** each trial pairs a fusion's concept with its real clause list and a **same-tier
  decoy** clause list drawn from another gated fusion of equal tier-law tier. Sides (A/B) are
  randomized by a seeded RNG; the answer key records the real side. Deterministic given `--seed`.
  A concept with no same-tier peer is skipped (logged).

## Two-sha integrity rule

Every fusion record carries the naming prompt's sha256 **and** the mechanics prompt's sha256
(line-ending-normalized, as in Phase A). `evaluate-fusions` scores only records whose BOTH shas
match the committed prompts today; records under a stale naming OR mechanics prompt are excluded and
reported. This is the Phase-A prompt-sha gate, doubled — a fusion is a joint product of two prompts,
so both must be current for its record to count.

## Origin guard (the fingerprint is not enough)

Stub runs use the *same* prompt files, so a stubbed fusion carries the *same* two shas as a live one
— the fingerprint cannot tell canned data from real. So every record also carries **`source`**
(`live` | `stub`) and **`model`**. `evaluate-fusions` scores only `source: live` records; stub
records — and any record missing an `origin` block (the fail-safe default is `stub`) — are excluded
from F1, F2, and the quiz, and reported separately. **Phase 2's live verdict requires zero stub
records in the corpus.** Without this, anyone (including future-me, by accident) could pass the
automatic criteria with a hand-written text file — the same class of hole the sha gate closes, one
layer up. `source` is set by the pipeline from the oracle it used (a `StubOracle` from
`--stub-responses` → `stub`; the live Anthropic oracle → `live`), never by the record's author.

## Machinery

- **`fuse <parentA.json> <parentB.json> [--tier auto]`** — naming call → mechanics call → gate (one
  repair) → writes `arena/fusions/<name>-<hash8>.spell.json` (the gated proposal spell) and
  `arena/fusions/<name>-<hash8>.record.json` (parents, concept, clauses, both shas, **origin: source
  live|stub + model**, timestamps, repair/discard notes), and appends `arena/fusions.log`. The
  filename carries **`<hash8>` = the first 8 hex of `sha256(parentApath|parentBpath)`** so two recipes
  that converge on the same NAME get distinct files — convergence is canon-correct and must not
  overwrite. When a name reappears under a different parents-hash the run appends
  `rediscovered: <Name> via <A>+<B> (first seen: <A0>+<B0>)` to the log; a re-fused identical pair
  self-overwrites (same hash). Every reader globs `*.record.json`, so both the legacy `<name>.` and
  new `<name>-<hash8>.` filenames are read. `--tier auto` (default) applies the tier law; an explicit
  `--tier N` overrides. `--stub-responses <f>` (offline) drives the pipeline from a canned-reply file
  and tags every record `source: stub`; `--arena <dir>` (offline/test) redirects the output tree.
- **`fuse-batch --pairs-file <f>`** — one `fuse` per line (`parentA.json parentB.json`), a scripted
  run list. Rejections and discards are data — a pair is never rerun.
- **`evaluate-fusions <fusionsDir>`** — computes F1 (first-try gate rate) and F2 (mean coverage +
  decoy baseline + quantization-pressure report) over matching-sha, **`source: live`** records only;
  stub records are excluded from scoring and reported (count must be 0 for a live verdict). Renders
  F3 as PENDING and writes a timestamped verdict doc (`<ts>-phase-b-fusion-verdict.md`, same
  no-clobber + refuse-overwrite rules as Phase A). OVERALL is INCOMPLETE until F3 is appended.
- **`quiz <fusionsDir> --seed S`** — from the records, writes `docs/experiments/<ts>-phase-b-quiz.md`
  (20 trials max: concept + clause lists A/B in randomized sides, no answers) and a SEPARATE
  `<ts>-phase-b-answer-key.md`. Deterministic given `--seed`.
- **`record-f3 <verdictDoc> --score n/20`** (Phase 3) — appends F3 = n/20 and the final OVERALL to
  the verdict doc; append-only, refuses if F3 is already set.

## Immutable

`src/Spellcraft/**`, `schema/`, `fixtures/*.proposal.json`, `tests/Spellcraft.Tests/**`,
`prompts/proposal-oracle.md` (sha `cd1bcf68…` — Phase A's record depends on it). The naming
doctrine's six load-bearing sentences are reproduced VERBATIM in `prompts/naming-oracle.md` and the
doctrine sync test extends to it; the fusion-mechanics prompt duplicates the proposal prompt's
closed vocabulary, drift-guarded by `PromptVocabularySyncTests`. Experiment docs are append-only;
thresholds, prompts, seeds, and the mapping never move after the first live call.

## Data-loss disclosure (run 483a70f)

Run `483a70f` fused 20 pairs, but records were filed by name alone, so two naming convergences
overwrote earlier records before this fix: **Droplet + Umbra → Murk** (clobbered by Gust + Umbra →
Murk) and **Glimmer + Pebble → Cairn** (clobbered by Pebble + Umbra → Cairn) were lost — the log has
20 lines, the archive 18 files. Convergence is canon-correct for the game; destroying the archive
was a bug, now closed by the hashed filenames above. The two lost pairs will be **re-fused as NEW
samples** under the same (unchanged) prompt shas — disclosed **data-loss recovery, not
result-fishing**: fishing means rerunning a pair to chase a better score, which stays forbidden;
restoring an archive the tooling destroyed is a different act. After recovery the corpus is 20 again
and the quiz regenerates at **20 trials, seed 42**, to match the pre-registered ≥14/20 bar (the bar
never moves; the trial count returns to 20).

## Two-stage plan

1. **Phase 1 (offline, always):** build the pipeline, evaluator, and quiz against a StubOracle;
   seeds; both prompts; tests. Commit spec / seeds+prompts / pipeline+tests; STOP.
2. **Phase 2 (live):** all 15 seed pairs + 5 fixed depth-2 fusions (the first five gated results
   alphabetically, paired round-robin), one `fuse-batch` run; `evaluate-fusions`; quiz + key; commit
   with the real F1/F2 numbers; push. OVERALL stays INCOMPLETE (F3 pending). **Precondition: zero
   stub records in the corpus** — `evaluate-fusions` reports the stub count and it must read 0 (delete
   any offline stub fusions before the live run).
3. **Phase 3 (human):** one stranger runs the quiz (never the key); `record-f3` appends F3 = n/20
   and the final OVERALL; commit. Same sitting runs the naming kill-test with the HTML prototype.
