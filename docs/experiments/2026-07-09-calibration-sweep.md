# Phase A v3 — HP-scale calibration sweep (EXPLORATORY)

Same-tier round-robin over the corpus (10 kits), both orderings, 5 seeds, mana 250.0. **Exploratory — no verdict, no PASS/FAIL.** The registered `hpScale` is chosen from this table by the human and reviewer, then written into the v3 spec.

| hpScale | fights | same-tier median | swing % | end-reason mix | T1vT1 | T2vT2 | T3vT3 |
|---|---|---|---|---|---|---|---|
| 3.0 | 130 | 6.4s | 19% | death:120 oom:10 | 5.9s | 8.4s | 1.1s |
| 4.0 | 130 | 11.1s | 42% | death:110 oom:20 | 9.1s | 16.0s | 1.5s |
| 5.0 | 130 | 13.8s | 54% | death:110 oom:20 | 10.6s | 21.0s | 3.3s |
| 6.0 | 130 | 19.4s | 54% | death:100 oom:30 | 12.1s | 29.2s | 10.7s |
| 8.0 | 130 | 29.0s | 61% | death:80 oom:50 | 14.7s | 32.2s | 15.9s |
