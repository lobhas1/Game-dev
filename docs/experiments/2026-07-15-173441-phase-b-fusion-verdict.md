# Phase B fusion verdict

- **Date:** 2026-07-15
- **Protocol:** phase-b, spec docs/specs/2026-07-11-phase-b-fusion-mechanics.md
- **Naming prompt sha:** `d08bf90c4ea83be588e1852cb89dde0edcdff42c1e5346226d302aeda5e0f2d0`
- **Mechanics prompt sha:** `dadc702f5e34b23246a6610b73113649a51d24ba7301e3ac1155f8ff7cb2c498`
- **Machine:** f1pass=true f2pass=true f3=PENDING
- **OVERALL:** INCOMPLETE (F3 pending)

Computed from `arena/fusions/*.record.json` (matching both prompt shas). No number hand-authored.

## Corpus origin

All scored records are live-sourced; 0 stub records in the corpus.

## F1 — gate validity (first-try)

20/20 = 100.0% — **PASS** (threshold ≥70%, live records only). Excluded 0 stale-sha and 0 stub record(s).

## F2 — semantic coverage

Mean tag-coverage **97.5%** vs decoy baseline **44.2%** (shift-by-one derangement) — **PASS** (threshold ≥70% AND strictly above decoy). 20 fusion(s) had ≥1 mappable tag.

| fusion | tier | coverage | unsatisfied tags |
|---|---:|---:|---|
| barrow | 3 | 3/3 (100.0%) | — |
| beacon | 2 | 1/1 (100.0%) | — |
| beacon | 2 | 1/1 (100.0%) | — |
| blaze | 2 | 3/3 (100.0%) | — |
| cairn | 2 | 1/1 (100.0%) | — |
| cairn | 2 | 2/2 (100.0%) | — |
| cinder | 3 | 3/3 (100.0%) | — |
| dew | 2 | 1/1 (100.0%) | — |
| dust-devil | 2 | 3/3 (100.0%) | — |
| hearth-stone | 2 | 2/2 (100.0%) | — |
| lighthouse | 3 | 2/2 (100.0%) | — |
| lodestone | 3 | 1/1 (100.0%) | — |
| mist | 2 | 2/2 (100.0%) | — |
| mud | 2 | 1/2 (50.0%) | ward |
| murk | 2 | 3/3 (100.0%) | — |
| murk | 2 | 2/2 (100.0%) | — |
| penumbra | 2 | 1/1 (100.0%) | — |
| pyre | 3 | 2/2 (100.0%) | — |
| smoke | 2 | 3/3 (100.0%) | — |
| steam | 2 | 2/2 (100.0%) | — |

## Quantization pressure (unmappable tags demanded)

perceive=8  (8 fusion(s) carried ≥1 unmappable tag — the widening signal, by design).

## F3 — human blind matching

**PENDING.** Run the quiz (concept + two clause lists, real vs same-tier decoy, sides randomized), 20 trials, ≥14/20. Append with `record-f3` — OVERALL stays INCOMPLETE until then.

## Pinned criteria (pre-registered, immutable)

- **F1** — ≥70% of fusion-mechanics proposals pass the gate first-try (one repair allowed; repairs and discards logged).
- **F2** — mean tag-coverage ≥70% AND strictly greater than the decoy baseline (concepts scored against a derangement of the other fusions' clause lists). summon/transform/perceive are unmappable: excluded from the denominator, counted as quantization pressure.
- **F3** — a stranger matches concept → clause list vs a same-tier decoy for 20 trials; ≥14/20 correct.
- **OVERALL** — INCOMPLETE until F3 appended; then PASS iff F1, F2, F3 all pass, else FAIL.
