# Cast-time round — regression report (all five criteria)

Companion to `docs/specs/2026-07-17-cast-time.md`. Fully offline; no key. Every number below was
produced by the scripts/commands named next to it.

## Criterion 1 — band ↔ gap, all 61 showcases: **61/61 exact**

Per showcase: `t(CastResolved) − t(CastStarted)` equals the source spell's declared band (instant 0,
quick 0.4, normal 0.8) within the 1e-3 float-quantum tolerance, and the first effect event carries
the resolution timestamp. 18 instants: gap 0. 29 quick: 0.4. 14 normal: 0.8. `long` still has no
corpus material. Consistency cross-check: exactly the 43 cast-mode showcases show timestamp motion
(their `CastStarted` moved to the true start instant); the 18 instants show none.

## Criterion 2 — fight-outcome invariance: **holds everywhere**

- **Tournament:** `arena/results.csv` regenerated under the new code is **byte-identical** to the
  pre-cast-time file (cmp) — every winner, duration, HP, damage total, verb count, and lead-change
  count over 130 fights is unmoved.
- **3 M1 replays:** winner, endReason, durationSeconds, and the entity manifest identical; the
  canonical-line multiset minus `castResolved` lines identical; zero lines removed:

| replay | events | added | timestamp-moved lines |
|---|---|---|---|
| frost-ember-seed1 | 116 → 141 | +25 castResolved (11+14 casts) | 14 |
| warden-vs-duelist-t2-seed1 | 96 → 106 | +10 castResolved | 50 |
| shadow-duel-t3-seed1 | 113 → 129 | +16 castResolved | 33 |

## Criterion 3 — Phase A C1–C4 offline at registered thresholds: **PASS, numerically unchanged**

```
prompt sha: cd1bcf68e2ee6e0bcfe13ce215e224a5a7c17e37ab3a299ff8371cb87349afde
config: hpScale 8.0, mana 250.0, amplifyMajor 1.75
  [PASS] C1 first-pass validity >=70%   100.0% (60/60, matching-hash runs)
  [PASS] C2 median duration 15-90s      median=20.3s  (130 fights, 42 distinct)
  [PASS] C3 >=3 distinct verbs / fight  130/130 (min 4)
  [PASS] C4 >=50% show a lead swing     103/130 (79.2%)
OVERALL: PASS
```

Verdict doc: `docs/experiments/2026-07-18-173640-phase-a-verdict-v4.md`. As the spec predicted, the
world clock already ticked through windups, so no duration could move; the C2 risk did not
materialize. No retuning performed.

## Criterion 4 — golden re-blessed, old archived

Old golden preserved verbatim at `tests/Arena.Tests/goldens/frost-ember-seed1.projection.pre-cast-time.txt`
(never overwritten). New golden blessed at the canonical path: 141 lines = old 116 + exactly 25
`castResolved`; zero removed; non-castResolved multiset unchanged; winner ember, death, 37.6s; all
V3 summary numbers (casts 11/14, verbs 4, statuses 15, lead changes 4) untouched.

## Criterion 5 — diff report: **timestamps-and-castResolved only, 64/64 files**

Across the 3 M1 replays and all 61 showcases: zero canonical lines removed, every added line is a
`castResolved`, the non-castResolved multiset is identical per file, and headers (winner, endReason,
durationSeconds, entities) are invariant. Timestamp motion is confined to (a) `CastStarted` moving
to the true pre-windup instant on cast-mode spells and (b) zone/DoT ticks inside fight quanta now
carrying their true tick times instead of the harvest stamp.

Standing guarantee re-verified: `replay-verify --diff-against-live` on all 64 regenerated files —
**64 OK / 0 FAIL**.

`manifest.json` and the vocabulary coverage doc are **byte-identical** (no vocabulary or metadata
change). Full test suite green: 188 passed, 1 skipped (live-oracle, offline).

## Changed files — the Unreal resync list

- `arena/showcases/*.replay.json` — **all 61** (each gains its `castResolved` line; cast-mode ones
  additionally carry the real window: CastStarted at 0, resolution at 0.4/0.8).
- `arena/replays/frost-ember-seed1.json`, `warden-vs-duelist-t2-seed1.json`,
  `shadow-duel-t3-seed1.json`.
- **Renderer-contract addition (needs an Unreal handler):** new event type `CastResolved`
  (`t`/`type`/`canonical`/`payload{caster,spell}`, schema stays `"1"`). Cast bar runs
  `CastStarted.t → CastResolved.t`; effect VFX anchor at `CastResolved`. Until the handler lands,
  skipping unknown event types degrades to today's zero-width behavior.
- NOT part of the corpus sync: `src/Spellcraft` (CombatModel/CombatSim), `src/Arena/Replay.cs`,
  tests, the re-blessed golden + archive, the spec + this report, the verdict doc.
