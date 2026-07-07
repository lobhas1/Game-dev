# Phase A v4 — amplify 'major' calibration sweep (EXPLORATORY)

Same-tier round-robin over the corpus (10 kits), both orderings, 5 seeds, hpScale 8.0, mana 250.0. The `major` amplify multiplier is re-priced per row — the oracle emits bands and the compiler prices them, so the same committed kits re-price for free. **Exploratory — no verdict, no PASS/FAIL.** The `2.5` row is the control and reproduces v3's criterion (median 13.9s, swing 76.9%). The registered `amplifyMajor` is chosen from this table by the human and reviewer, then written into the v4 spec.

| amplifyMajor | fights | same-tier median | swing % | end-reason mix | T1vT1 | T2vT2 | T3vT3 |
|---|---|---|---|---|---|---|---|
| 2.5 | 130 | 13.9s | 76.9% | death:110 oom:20 | 16.7s | 13.7s | 13.6s |
| 2.25 | 130 | 14.4s | 76.9% | death:110 oom:20 | 17.4s | 13.7s | 14.1s |
| 2.0 | 130 | 17.2s | 76.9% | death:110 oom:20 | 17.4s | 19.9s | 15.1s |
| 1.75 | 130 | 18.3s | 76.9% | death:110 oom:20 | 17.6s | 19.9s | 19.5s |
| 1.5 | 130 | 18.3s | 76.9% | death:110 oom:20 | 17.6s | 19.9s | 19.5s |
