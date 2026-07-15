# Target-filter round — step-4 regression report

Companion to `docs/specs/2026-07-15-target-filter.md`. Every number here was produced offline
under the new sim law; no API key was used at any point.

## Criterion (a) — Phase A C1–C4, re-run offline on the v4 kit corpus

`tournament arena/kits --same-tier --hp-scale 8 --mana 250 --amplify-major 1.75 --seeds 1..5`, then
`evaluate arena/kits`, at the registered v4 thresholds:

```
[PASS] C1 first-pass validity >=70%   100.0% (60/60, matching-hash runs)   [unchanged — no regen]
[PASS] C2 median duration 15-90s      median=20.3s  (130 fights, 42 distinct)
[PASS] C3 >=3 distinct verbs / fight  130/130 (min 4)
[PASS] C4 >=50% show a lead swing     103/130 (79.2%)
OVERALL: PASS
```

Verdict doc: `docs/experiments/2026-07-15-190233-phase-a-verdict-v4.md`. 24 of 130 fight rows shifted,
all in the earth-warden kits (the only same-tier kits that self-hit): their own area damage vanishes
(e.g. dmgToB 262.4→134.4) while winners and durations stay stable. Median, C3, and C4 are numerically
unchanged; one distinct-outcome bucket collapsed (43→42). No criterion failed; no retuning performed.

## Criterion (b) — the golden frost-ember seed-1 fight, re-blessed

Regenerated `arena/replays/frost-ember-seed1.json` under the new law: **byte-identical** to the
committed golden (git sees no change), and the golden test `Defaults_ReproduceTodaysFight_Seed1`
stays green. The frost and ember kits deliver by projectile / targetUnit and carry no self-covering
AoE, so the rule does not touch this matchup. **Re-bless verdict: unchanged** — the new outcome equals
the old (winner ember, death, 37.6s, 116 events). There is no new version to archive; the committed
golden IS the new golden.

## The 3 M1 fight replays — regenerated, all byte-identical

| replay | before → after | change |
|---|---|---|
| frost-ember-seed1 | 116 → 116 events | byte-identical |
| warden-vs-duelist-t2-seed1 | 96 → 96 events | byte-identical |
| shadow-duel-t3-seed1 | 113 → 113 events | byte-identical |

warden-1 vs duelist-1 at seed 1 is one of the tournament rows that did NOT shift; shadow assassins
use targetUnit/conceal, no self-AoE. Zero projection lines vanished across all three.

## Criterion (c) — per-file diff across the 26 showcases

The 26 committed showcases were built (at commit `ed9555b`, m2 phase 1) from the **v3 fusion corpus**,
which was later archived to `arena/fusions-v3-archive/` when v4 re-fused (`88f6b8e`) and the anchor
run added 30 children (`50db890`). Their authoritative sources are therefore the 6 seeds in
`fixtures/seeds/` + the 20 clean-named records in `arena/fusions-v3-archive/`, not the current
hash-named `arena/fusions/` corpus. Per the ratified scope decision (**hold at 26**), the 26 were
regenerated from those true sources under the new law (the live 50-record corpus was set aside and
restored byte-identically; `arena/fusions` is untouched).

**Result: 22 of 26 byte-identical; manifest and coverage doc byte-identical; 4 changed.** The 4 are
exactly the showcases whose spell delivers a self-covering AoE / self-ticking zone — the evidence set.

| showcase | Δ | vanished lines |
|---|---|---|
| **blaze** | −14, +0 | self-damage 115.2 Fire; self-Burn + its 11 self-DoT ticks (64 each); self-Push displace; self-Burn removal. **Only self-offense.** |
| **conflagration** | −14, +0 | self-damage 143.36 Fire; self-Burn + 11 self-DoT ticks (61.44 each); self-Push displace; self-Burn removal. **Only self-offense.** |
| **hearthstone** | −1, +0 | `damage E1 amt=0 absorbed=61.44` — its own blast absorbed by its own shield. The ward is no longer wasted on itself. **Only self-offense.** |
| **lighthouse** | −29, +9 | self-offense vanished (self-damage 102.4/117.76/235.52 Light, self-Vulnerable, an evaded self-hit) — **but** the beneficial self-heal lines also *changed value* (see below). |

### Lighthouse — a criterion-(c) letter-trip, flagged not silently passed

Lighthouse is a `spawnZone affects:"all"` centred on the caster; each tick applies **heal (0.35)** +
**damage light + onHit vulnerable (offensive)**. The offensive half correctly stops hitting the owner.
The heal is beneficial and correctly still fires — but with the self-damage gone the caster sits at
full HP, so each tick's heal turns to pure overheal: `heal E1 eff=117.76 over=25.6` → `heal E1 eff=0
over=143.36`. Nine heal lines thus **changed value rather than vanishing**.

Criterion (c) was pre-registered as "only self-offense lines vanish; anything else moving is a FAIL."
By the letter, lighthouse trips it. By behaviour it is correct and required: the rule says beneficial
self-effects "continue to apply exactly as today," and a heal's effective/overheal split necessarily
depends on the caster's current HP — which the (correct) removal of self-damage changes. The shift is
entirely on the caster (E1), entirely downstream of removing self-offense; no opponent line moves.

**This is surfaced for a ruling** (per the round's no-silent-pass discipline): accept it as an expected
second-order consequence and amend criterion (c) to exempt beneficial self-effects whose magnitude
tracks removed self-damage — or investigate further. It is not silently marked PASS.

## Changed files — flag for the Unreal re-sync (act one)

Only these move; everything else (22 showcases, manifest, coverage doc, 3 replays, golden) is
byte-identical:

- `arena/showcases/blaze.replay.json`
- `arena/showcases/conflagration.replay.json`
- `arena/showcases/hearthstone.replay.json`
- `arena/showcases/lighthouse.replay.json`

## Pre-existing suite state, fixed this round (per ratified decision)

Two tests were red before this round from the anchor live run growing `arena/fusions` 20→50 (verified
by stashing the sim change). Fixed to match reality: `ShowcaseTests.Batch` now asserts one showcase
per corpus input (6 seeds + N records) instead of a hardcoded 26; `AnchorRoundTests` baseline now
asserts the 10% non-vacuity bar (the anchor spec's own line) instead of a pinned 0%, since the corpus
now contains the anchor children that express those signatures. The deeper fix the anchor doc flagged
— excluding anchor children from the baseline denominator — remains open.
