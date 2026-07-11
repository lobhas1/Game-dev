# LLM Spellcraft — Project Ledger: The Foundation
**Closed 2026-07-11.** Every load-bearing claim tested, published, independently re-derived.

## The premise and the law
One idea: an LLM is the *meaning* oracle of a spell-fusion game — it names fused concepts
and composes their mechanics from a closed vocabulary — while a deterministic C# compiler
and simulator own every number (shares, bands, tier budget = 100 × 1.6^(tier−1)), a strict
parser+compiler gate decides what is real, and the engine only performs what the sim decided.
Standing law from week one: thresholds are pre-registered and never move after data;
calibrate on old corpora, confirm only on fresh ones; one dial changes per round; FAIL
verdicts are committed and pushed with the same dignity as PASS.

## Phase A — can the oracle compose valid spells that make good fights?
Criteria (fixed before any data): C1 first-pass validity ≥70%; C2 median fight 15–90s;
C3 ≥3 distinct verbs per fight; C4 ≥50% of fights show a lead swing.

**v1 — FAIL** (2026-07-07, 10 kits, 450 fights): C1 86.7% (26/30) · C2 median **1.7s** ·
C4 23.3%. Diagnosis from raw data: amplified detonations (×2.5 on 0.7–0.8 share) deal
~1.75–2.0× tier budget into flat 300 HP — 200/T1, 344.62/T2, 537.6/T3, formula-exact;
every oracle shield was elemental and absorbed **zero** across all 450 fights (the prompt
taught the field, not its cost); the cross-tier corpus (71% of pairings, reviewer's own
design error) inflated the headline without causing it.

**v3 — FAIL** (hpScale locked at 8 from an offline sweep; two prompt sentences added,
new fingerprint cd1bcf68): C1 **100%** (30/30) · C2 **13.9s** — missed by 1.1s · C4 76.9%.
The sweep predicted 29s; fresh kits were ~2× deadlier because the prompt fixes freed
wasted budget into damage. The two-corpus rule caught a systematic shift that
self-calibration would have shipped as a pass.

**v4 — PASS** (2026-07-08; single dial: amplifyMajor 2.5 → 1.75, registered pre-run,
engine defaults untouched): **C1 100% (60/60) · C2 20.3s · C3 130/130 (min 4) · C4 79.2%.**
Per tier: T1 17.2s, T2 25.9s, T3 25.6s. Prediction on record before the run: 18.3s from
calibration, margin should hold. It held.

## The naming premise — the week-one debt
A stranger (first contact with the project) played the fusion prototype: **25 fusions —
Guessed-it 15 · Plausible-surprise 6 · Nonsense 4 → 16% nonsense: GREEN** (band: <20%).
Live convergence observed twice over: Steam, Smoke, Penumbra reproduced across independent
sessions; Murk and Cairn each reached from two different recipes — then re-converged again
in a later recovery run. Qualitative findings, empirically confirmed: overwrought
"mysterious" flavor register; lexical crutches (hide, blind, threshold, border).

## Phase B — do fused concepts get mechanics that carry their meaning?
Pipeline: parents → naming oracle (sha 26ac883f) → mechanics oracle (sha c6b12c63) →
strict gate (one repair allowed) → provenance-stamped records.
**F1 gate validity: 100% (20/20 first-pass, live).**
**F2 semantic coverage: 95.8% vs 46.7% shuffled-decoy baseline.**
**F3 stranger blind-matching: 19/20** (threshold ≥14/20) — **OVERALL: PASS** (2026-07-11).
Quantization pressure: perceive=6 — the corpus's formal vote for a future `reveal` verb.
Caveat recorded *before* scoring: a hard element→verb lock (fire→damage 100%, water→heal
100%) lets stereotype alone win some trials; a harder same-element-decoy variant is specified.

## Phase D milestone 1 — does the engine tell the truth?
Law: the C# sim is never rewritten; Unreal performs recordings. Gate: three producers
byte-identical — live sim, replay-verify from the exported file, and the renderer's own
event log from real playback. **Result: 3/3 byte-for-byte, speed-invariant (1× and 4×
identical logs), zero combat math in 460 lines of renderer (audited).** Exported replay
files are byte-identical across Windows and Linux. Independent audit: fresh references
minted on a second machine matched the renderer's committed testimony exactly.

## Integrity systems built along the way
Pre-registered protocols with lock-in commits; CRLF-normalized prompt fingerprinting with
per-generation logging; provenance stamps (live vs stub) with fail-safe default-to-stub;
tier law enforced at the gate; collision-proof archives with rediscovery logging;
timestamped no-clobber verdict files; refusal to score contaminated or mixed-config
corpora; golden regression fights; append-only ledgers; two-machine re-derivation of every
verdict before belief.

## Defects caught by review before they could lie
Same-day evaluate runs silently overwrote the historical v1 FAIL doc (restored from git);
stub fusions were indistinguishable from live ones; wrong-tier spells passed the gate;
name collisions destroyed two records (both re-fused, both re-converged); replay-verify
accepted a corrupted header; an 18-trial quiz sat under a ≥14/20 threshold. Each found
empirically, fixed, and re-verified with the probe that caught it.

## Errors ledger (nobody exempt)
Reviewer: designed the cross-tier v1 corpus; predicted amplify 2.0 (table said otherwise);
shipped a pairs-file format that split on the space in "D:\Python projects"; two Windows
encoding traps (BOM, CRLF) diagnosed only after they bit. Operator: six commit messages
in the hall of shame — "added html" ×2, "ammedments", "<what evaluate printed>",
"verfication and test", "<put the real word here>" — streak broken 2026-07-11. Agents:
claimed "source complete" for code no compiler had seen; otherwise repeatedly caught the
reviewer's own spec flaws and refused to fabricate a green build. Culture propagated.

## What these numbers do NOT claim
One tester, one evening (n=1 human; one more nonsense press was the yellow band).
Tier-3 fight data rests on a single kit pair. All oracle output is one model
(claude-sonnet-4-6); ship-model transfer untested. Corpora are small (10 kits, 20 fusions).
F3 partially rides the element stereotype. Green means "survived its first real test,"
not "proven at scale."

## The notebook (ambition, not debt)
Mana ceiling (flat 250; two-thirds of T2 fights end out-of-gas). Band spacing
(1.5/1.75/4.0 crowding). The `reveal` verb. Naming-prompt v4: plain register, lexical
variety, "element flavors, it does not dictate verbs," off-stereotype anchor spells
(Cauterize, Riptide), same-element decoys. Ship-model transfer round. fuse-batch quoted
paths. Packaging-era VS2022-shell workaround.

## Cost of all evidence
A few euros of API calls, one stranger's evening, five weeks of discipline.

## Scoreboard
| Gate | Result | Evidence |
|---|---|---|
| Phase A v1 | FAIL (published) | 2026-07-07 verdict doc |
| Phase A v3 | FAIL (published) | 2026-07-07-213018 verdict doc |
| Phase A v4 | **PASS** | 2026-07-08-104144 verdict doc |
| Naming kill-test | **GREEN** 16% | 2026-07-08 stranger-naming-test |
| Phase B F1/F2 | **PASS** 100% / 95.8% | 2026-07-08-145511 verdict doc |
| Phase B F3 | **PASS** 19/20 | same doc, appended 2026-07-11 |
| Renderer gate M1 | **PASS** 3/3 | unreal-game-idea, renderer-gate-milestone-1.md |