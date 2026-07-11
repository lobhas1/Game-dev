# Phase B fusion verdict

- **Date:** 2026-07-11
- **Protocol:** phase-b, spec docs/specs/2026-07-11-phase-b-fusion-mechanics.md
- **Naming prompt sha:** `d08bf90c4ea83be588e1852cb89dde0edcdff42c1e5346226d302aeda5e0f2d0`
- **Mechanics prompt sha:** `dadc702f5e34b23246a6610b73113649a51d24ba7301e3ac1155f8ff7cb2c498`
- **Machine:** f1pass=false f2pass=false f3=PENDING
- **OVERALL:** INCOMPLETE (F3 pending)

Computed from `arena/fusions/*.record.json` (matching both prompt shas). No number hand-authored.

## Corpus origin

All scored records are live-sourced; 0 stub records in the corpus.

## F1 — gate validity (first-try)

0/0 = 0.0% — **FAIL** (threshold ≥70%, live records only). Excluded 0 stale-sha and 0 stub record(s).

## F2 — semantic coverage

Mean tag-coverage **0.0%** vs decoy baseline **0.0%** (shift-by-one derangement) — **FAIL** (threshold ≥70% AND strictly above decoy). 0 fusion(s) had ≥1 mappable tag.

| fusion | tier | coverage | unsatisfied tags |
|---|---:|---:|---|

## Quantization pressure (unmappable tags demanded)

(none) — no gated concept demanded summon/transform/perceive.

## F3 — human blind matching

**PENDING.** Run the quiz (concept + two clause lists, real vs same-tier decoy, sides randomized), 20 trials, ≥14/20. Append with `record-f3` — OVERALL stays INCOMPLETE until then.

## Pinned criteria (pre-registered, immutable)

- **F1** — ≥70% of fusion-mechanics proposals pass the gate first-try (one repair allowed; repairs and discards logged).
- **F2** — mean tag-coverage ≥70% AND strictly greater than the decoy baseline (concepts scored against a derangement of the other fusions' clause lists). summon/transform/perceive are unmappable: excluded from the denominator, counted as quantization pressure.
- **F3** — a stranger matches concept → clause list vs a same-tier decoy for 20 trials; ≥14/20 correct.
- **OVERALL** — INCOMPLETE until F3 appended; then PASS iff F1, F2, F3 all pass, else FAIL.
