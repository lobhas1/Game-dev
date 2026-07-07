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

## Diagnosis (data only)

OVERALL is FAIL. Fill each cause from the evidence below; make **no** fixes here — raw data and this section go to review first.

### 1. Oracle composition
Evidence — aggregate first-pass 86.7% (26/30); marginals: applyStatus=33, damage=17, displace=8, modifyStat=19, shield=5, spawnZone=5.
- 

### 2. Pricing (e.g. ifStatus amplify)
Evidence — median duration 1.7s vs 15–90s window; 450 fights, 90 distinct.
- 

### 3. Arena constants
Evidence — lead swings 105/450 (23.3%); distinct verbs min 3, 450/450 fights ≥3.
- 
