# The Anchor Round — authored spells enter the gene pool

## Rationale

The canon has a proven ancestry ceiling: no lineage can produce a fire-heal, a water-damage, or any
nature/arcane concept — verified from the v4 corpus (element counts: shadow 7, light 4, earth 4,
fire 2, air 2, water 1; **nature 0, arcane 0**). Cause: seed orthogonality (each of the six seeds is
one element + one archetypal verb) plus the element→verb priors the prompt no longer enforces but the
gene pool still embodies. Fix (per docs/NOTEBOOK.md, AUTHORED ANCHORS): hand-authored **anchor**
spells enter the fusable pool through the standard gate — **as content, not law**. The designer
speaks through the gene pool; the prompts and engine do not change. This round changes the gene pool
only.

## Immutables (the point of the round)

`prompts/**` BYTE-UNTOUCHED — all three shas survive (naming `d08bf90c…`, mechanics `dadc702f…`,
proposal `cd1bcf68…`). Also untouched: `schema/`, `src/Spellcraft/**`, `fixtures/seeds/**` (anchors
live in the NEW `fixtures/anchors/`, never in `fixtures/seeds/`), all experiment docs, all past
thresholds.

## Pre-registered criteria (frozen)

- **A0 gate validity:** all anchors pass the strict gate first-try as tier-1 proposals.
- **A1 signature inheritance:** each anchor injects ONE signature pairing (element+core-verb, e.g.
  fire+heal). Fuse each anchor with each of the 6 original seeds once (30 fusions). A1 = fraction of
  an anchor's children that express that anchor's signature pairing as a tag-justified core verb ON
  A CONCEPT WHOSE ELEMENT makes it off-stereotype, OR carry the signature meaning-tag with the
  matching verb. PASS per-anchor iff A1 ≥ 1/3 of its children AND ≥ baseline + 25 percentage points.
  BASELINE measured FIRST from the committed v4 corpus with the same script.
- **A2 wing opening:** among children of the nature anchor and the arcane anchor, ≥1 concept wearing
  element nature and ≥1 wearing arcane, respectively.
- **A3 regression:** F1 first-pass validity of the 30 new fusions ≥90%; naming doctrine holds (no
  forbidden styles).

### Frozen `express()` (the "same script" for baseline and A1)

The signature pairing is **element + verb** — "fire+heal" means *a fire concept that heals*, not
"heal anywhere". So a fusion **expresses** signature (element E_a, signature-tags S) iff:

> the concept's element == E_a **and** for some off-stereotype tag T ∈ S, the concept **claims** T
> (T is one of its tags — tag-justified) **and** its mechanics **deliver** T (the frozen F2 mapper,
> `TagCoverage.Covers`, the single source shared with evaluate-fusions).

"Off-stereotype" is relative to the element's seed archetype: fire→damage, water→heal, earth→ward,
air→movement, light→perceive, shadow→conceal; **nature/arcane have no seed, so every tag on them is
off-stereotype** (the two wings this round opens). `perceive` is unmappable (no verb), so Rune's
*mechanized* half is `ward`; `perceive` is meaning-only.

**Why element-scoped, not element-agnostic (the vacuity flag).** An element-agnostic reading —
"claims T and delivers T on any off-stereotype element" — is **vacuous**: measured on the v4 corpus
it already yields ≈35% for `damage` and ≈30% for `ward` (shadow/light/earth concepts legitimately
carry and deliver those off their own elements), and ≈10% for `heal` (Dew, Lighthouse are light-heal).
A criterion the old corpus already satisfies is not a test. Element-scoping is the redesign the
task's safety valve prescribes, and it matches the pairing notation exactly.

## Baseline (v4 corpus, measured BEFORE the live run — `ancestry-eval arena/fusions`)

| signature | baseline expression rate | A2 element count |
|---|---|---|
| Phoenix Tear — fire+heal | **0.0%** (0/20) | — |
| Riptide — water+damage | **0.0%** (0/20) | — |
| Wind Cutter — air+damage | **0.0%** (0/20) | — |
| Bramble — nature+control/damage | **0.0%** (0/20) | nature: **0** |
| Rune — arcane+ward/perceive | **0.0%** (0/20) | arcane: **0** |

All baselines are 0.0% — no signature exceeds 10%, none is vacuous. With baseline 0, A1's binding bar
is **≥1/3 of an anchor's children** (0 + 25pp = 25% is the looser of the two). (Note: the v4 air
concepts Dust Devil and Steam carry incidental `damage` *verbs* but do not *claim* the `damage` tag,
so air+damage is genuinely 0 under the tag-justified rule.)

## The five anchors (`fixtures/anchors/`, seed-card format, tier-1, gate-clean)

| name | element | signature pairing | design |
|---|---|---|---|
| Phoenix Tear | fire | fire+heal | heal DOMINANT (heal 0.7 + regen 0.3), self |
| Riptide | water | water+damage | damage dominant (water damage 1.0 + push) |
| Wind Cutter | air | air+damage | damage dominant (air damage 0.8 + bleed 0.2), NO displace |
| Bramble | nature | nature+control/damage | root 0.5 + nature damage 0.5 — opens the nature wing |
| Rune | arcane | arcane+ward/perceive | shield 0.6 + critChance buff 0.4 — opens the arcane wing |

Names are doctrine-legal knowns (none in the forbidden-style list). Flavor lines follow the v4
register (≤14 words, concrete, banned list respected). Full cards presented at STOP ONE for
ratification.

## Two stop points

- **STOP ONE — ratification (now):** all five anchor cards + the baseline row are presented; the
  human is the designer and approves, edits, or renames each anchor. **No fusion, and no commit,
  before ratification.**
- **STOP TWO — awaiting the live run:** after ratification, the anchors + spec + machinery are
  committed, `arena/anchor-pairs.txt` (each anchor × each seed = 30 pairs) is generated, and the live
  runbook is printed.

## Live runbook (human, after STOP TWO, ~1–2€)

1. `fuse-batch --pairs-file arena/anchor-pairs.txt` — 30 fusions, ONCE each (rejections are data;
   never rerun a pair). Fusions land in `arena/fusions/` alongside the v4 records — same prompt shas,
   same generation, so the evaluate-fusions mixed-sha guard permits it.
2. `ancestry-eval arena/fusions` — A1 per anchor (PASS iff ≥1/3 AND ≥ baseline+25pp) and A2 wings.
3. `evaluate-fusions arena/fusions` — A3 (F1 ≥90%) and doctrine.
4. Commit results with the REAL numbers in the message.
