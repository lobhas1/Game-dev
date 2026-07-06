# Oracle Naming Doctrine

The **single authority** for how the combination oracle names a fusion. The oracle (an LLM)
rules on what two fused spells *mean* — including the result's name. A live playtest found its
failure mode: it BLENDED ingredient names (adjective-of-A + noun-of-B) instead of SYNTHESIZING
an emergent concept (Fire + Rain → Steam). A tuned prompt fixed it. That doctrine previously
lived only inside a string literal in the prototype; this document is now its home, and a
drift-guard test (§4) keeps every embedding honest.

This is doctrine, not a proposal — every rule below was tuned against an observed failure and
each carries a known cost. Do not rephrase, merge, extend, or add rules here casually.

---

## 1. Failure taxonomy

- **Blending, not synthesis:** names built as synonym/adjective-of-A + noun-of-B ("Drifting Gleam", "Smoldering Dark" pre-fix), degrading at depth into recycled parent morphemes ("Warmlight Drift"). Fails the independently-guessable criterion.
- **Tag contamination:** mechanical tags leaking into names ("Steam Mend" from [heal]).
- **Convergence starvation (structural):** blended names are globally unique, so recipes never collide and the rediscovery / shared-canon loop cannot fire. Post-fix, first convergence observed: Steam + Cinder → Slag (rediscovered).
- **Held defense:** the grandiosity ban worked before and after the fix; no Cosmic/Eternal-class names observed.
- **Known cost of the fix:** the no-word-reuse rule occasionally forbids a genuinely intuitive shared-word answer; watch for contorted names as the trade.
- **Open watch-item (do NOT add rules for it):** tier-3/4 results trend back to two-word constructions ("Branded Dark") — new constructions, not parent-morpheme blends; possibly natural language behavior (rare concepts lack single nouns). Defect only if stranger verdicts rate deep names worse than shallow ones.

---

## 2. Naming rules

The six load-bearing sentences of the prompt, verbatim, each with its rationale. These are the
sentences the drift-guard test (§4) pins in every embedding.

1. **Name the new THING that exists when the two meet: an emergent concept, never a blend of the ingredient names.**
   *Rationale:* the central mandate — synthesis over blending; directly counters the "Blending, not synthesis" failure.

2. **The result name must not reuse or lightly modify any word from either ingredient name.**
   *Rationale:* the load-bearing anti-blend mechanic that enforces the independently-guessable criterion. Known cost: occasionally forbids a genuinely intuitive shared-word answer — watch for contorted names.

3. **Prefer one plain common noun when one exists, and if the fusion resolves to a concept that plausibly already exists, return that plain name — converging onto known things is desired.**
   *Rationale:* plain nouns collide, so recipes can rediscover known spells and the shared-canon loop fires; counters convergence starvation.

4. **Fire + Rain gives Steam, never "Misty Flame" or "Elemental Duality". Ember + Shadow gives Smoke, never "Smoldering Dark".**
   *Rationale:* concrete exemplars that anchor the abstract rules by the canonical synthesis case; teaching by example.

5. **Ingredient tags are mechanical bookkeeping — never translate a tag into a name word.**
   *Rationale:* blocks tag contamination (e.g. "Steam Mend" from [heal]); tags are bookkeeping, not vocabulary.

6. **Forbidden name styles: Cosmic, Infinite, Ultimate, Eternal, Supreme, Absolute, Omni-, God-, Primordial, or vague words like Essence/Energy/Power as the head noun.**
   *Rationale:* the grandiosity ban — a held defense that worked before and after the fix; blocks Cosmic/Eternal-class names and vague head nouns.

---

## 3. Canonical prompt template

The full prompt rendered by `oraclePrompt(a, b, tier)` in `prototype/spellcraft-oracle.html`,
with the JS-computed fragments replaced by placeholders. Every literal sentence is byte-identical
to the source (straight quotes `"`, em-dashes `—`). The source encodes that apostrophe as the JavaScript escape `’`, which the browser renders to a curly apostrophe; it appears as a plain apostrophe in the block below.

```text
You are the Combination Oracle of a fantasy spellcraft system. Two known spells are fused; you rule on the single most intuitive result — the answer most people would independently guess. Name the new THING that exists when the two meet: an emergent concept, never a blend of the ingredient names. The result name must not reuse or lightly modify any word from either ingredient name. Prefer one plain common noun when one exists, and if the fusion resolves to a concept that plausibly already exists, return that plain name — converging onto known things is desired. Concrete beats abstract. Mundane-but-right beats grandiose. Fire + Rain gives Steam, never "Misty Flame" or "Elemental Duality". Ember + Shadow gives Smoke, never "Smoldering Dark". Ingredient tags are mechanical bookkeeping — never translate a tag into a name word. Forbidden name styles: Cosmic, Infinite, Ultimate, Eternal, Supreme, Absolute, Omni-, God-, Primordial, or vague words like Essence/Energy/Power as the head noun.

Spell A: {{SPELL_A_LINE}}
Spell B: {{SPELL_B_LINE}}

{{SELF_FUSION_NOTE}}The result's tier is already fixed at {{TIER}} by recipe law. You decide meaning only, never power.

You also choose the visual signature from a fixed vocabulary. Visuals must telegraph the tags — a ward should read protective (ring/orbit), damage aggressive (burst/beam/cone), conceal smoky (trail, sparse). The vfx must also depict the flavor sentence you write: if the flavor says a slow creeping mist, do not rule a dense burst. Flourish is welcome; unreadability is not.

Respond with ONLY minified JSON, no markdown fences, exactly this shape:
{"name":"1-3 words","emoji":"one simple widely-supported emoji, never a combined sequence","element":"one of fire|water|earth|air|light|shadow|nature|arcane","tags":["1-3 of {{TAGS_VOCAB}}"],"flavor":"evocative, concrete, max 14 words","why":"max 12 words: why this follows from the ingredients","vfx":{"shape":"one of {{VFX_SHAPES}}","motion":"one of {{VFX_MOTIONS}}","density":"one of sparse|normal|dense","flicker":true or false,"trail":true or false,"accent":"one of {{ACCENTS}}"}}
```

**Placeholders:**

- `{{SPELL_A_LINE}}` — Spell A's ingredient line from `ing(a)`: `"<name>" — element: <element>, tags: [<tag, tag>], flavor: "<flavor>"`, and when the spell has parents it carries `lineage: <ParentA> + <ParentB> — "<why>"` appended (the `— "<why>"` only when a reasoning string exists).
- `{{SPELL_B_LINE}}` — Spell B's ingredient line, identical format to `{{SPELL_A_LINE}}`.
- `{{SELF_FUSION_NOTE}}` — present only when A and B are the same spell (self-fusion): the sentence `A and B are the same spell: rule a refined, purified, or intensified form of that concept.` followed by a blank line; otherwise empty.
- `{{TIER}}` — the integer result tier, fixed by recipe law (`tierOf`): equal tiers ascend (`min(5, tier + 1)`), unequal tiers keep the higher.
- `{{TAGS_VOCAB}}` — `TAGS.join('|')` = `damage|heal|ward|control|movement|summon|transform|perceive|conceal|area|duration`.
- `{{VFX_SHAPES}}` — `VFX_SHAPES.join('|')` = `burst|beam|ring|spiral|rain|orbit|cone`.
- `{{VFX_MOTIONS}}` — `VFX_MOTIONS.join('|')` = `rise|fall|swirl|seek|jitter|implode`.
- `{{ACCENTS}}` — `Object.keys(ACCENTS).join('|')` = `gold|silver|sky|mint|verdant|teal|abyssal|ash|emberglow`.

---

## 4. Sync law

Any implementation that embeds this prompt — today the single-file prototype at
`prototype/spellcraft-oracle.html`, later the C# proposal-oracle bridge — must contain the six
load-bearing sentences of §2 verbatim, enforced mechanically by the drift-guard test
`tests/Spellcraft.Tests/OracleDoctrineSyncTests.cs`: if the prompt in any embedding and this
doctrine ever diverge on a load-bearing sentence, `dotnet test` goes red. The prompt text has
exactly one home — this document. Edits happen HERE FIRST and then propagate to every embedding;
never the reverse. The prototype and any future bridge are copies kept honest by the test.
