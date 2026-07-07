# Phase A verdict

- **Date:** 2026-07-07
- **Model(s):** claude-sonnet-4-6
- **Prompt sha:** `88d1535d1379a12d640d8a758cf2ab05e5ce4ba25b446adc75d782d10fbfebca`
- **OVERALL:** FAIL

Generated from `arena/generation.log`, `arena/results.csv`, and the committed prompt. No number here is hand-authored.

## First-pass validity by brief (C1, matching-hash runs)

| brief | generated | first-pass | validity |
|---|---:|---:|---:|
| a defensive earth warden | 6 | 6 | 100.0% |
| a hit-and-run wind duelist | 6 | 4 | 66.7% |
| a patient frost controller | 6 | 6 | 100.0% |
| a reckless fire skirmisher | 6 | 6 | 100.0% |
| a shadow assassin built on setup and burst | 6 | 4 | 66.7% |
| **aggregate** | **30** | **26** | **86.7%** |

## Verb marginals (matching-hash runs, recursive)

applyStatus=33, damage=17, displace=8, modifyStat=19, shield=5, spawnZone=5

## Fights

450 fights in `results.csv`; 90 distinct outcomes (seeds replicate when no RNG is drawn).

## Scorecard

```
=== PHASE A VERDICT — pre-registered decision rule ===
prompt sha: 88d1535d1379a12d640d8a758cf2ab05e5ce4ba25b446adc75d782d10fbfebca
  [PASS] C1 first-pass validity >=70%   86.7% (26/30, matching-hash runs)
  [FAIL] C2 median duration 15-90s      median=1.7s  (450 fights, 90 distinct)
  [PASS] C3 >=3 distinct verbs / fight  450/450 (min 3)
  [FAIL] C4 >=50% show a lead swing     105/450 (23.3%)
OVERALL: FAIL
```

## Pinned interpretations (pre-registered, immutable)

> first-pass validity ≥70%; median fight duration 15–90s sim-time; ≥3 distinct verbs firing per fight; ≥50% of fights show a lead change or status-driven swing (sign flip of the HP difference, sampled 1/s).

- **C1** — first-pass-accepted ÷ generated, over all `generation.log` rows whose `promptSha` equals `sha256(prompts/proposal-oracle.md)`. ≥70% passes.
- **C2** — median over all fights in `results.csv` as run (mirrors and seed replicates included). 15–90s passes.
- **C3** — EVERY fight has ≥3 distinct verbs firing.
- **C4** — ≥50% of fights have `leadChanges ≥ 1`.
- **OVERALL** — PASS iff C1–C4 all PASS. INCOMPLETE if C1 has no matching-hash data, or any kit lacks a matching-hash generation record.

## Diagnosis (data only) — filled 2026-07-07 after independent re-derivation

Stratified by tier-matchup: same-tier n=130, median 1.8s [FAIL], swings 15.4% [FAIL];
T1v1 5.8s, T2v2 1.3s, T3v3 0.9s. Cross-tier corpus design (71% of fights) inflated the
headline but is not causal.

1. PRIMARY — amplify=major (x2.5) on 0.7–0.8-share clauses deals ~1.75–2.0x tier budget
   per hit (T1 200, T2 344 observed, T3 ~450) into flat 300 HP. Setup-detonate appears
   in every kit; monotonic same-tier TTK collapse matches.
2. DEFENSE INERT — every oracle shield was elemental; elemental shields absorb only
   their element (by design, verified); mixed-element arena => absorbed=0 in all 450
   fights. Prompt teaches the field, not its cost; pricing does not discount it.
3. Corpus: cross-tier pairings measure the tier ladder, not composition (protocol flaw,
   secondary). 4. Policy never defends; no RNG drawn; flat mana => T2v2 oom pocket (10).

C1 (86.7%, sha 88d1535d) is unaffected and stands. No constants, prompt, or thresholds
were modified. Corrective protocol: v3 (pre-registered) — calibration sweep on this
corpus (offline), then fresh kits under a v3 prompt (shield-element semantics +
displace-has-no-share lines), same-tier round-robin, unchanged thresholds.
