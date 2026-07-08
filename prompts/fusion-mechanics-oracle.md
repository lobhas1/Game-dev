<!-- Single-authority prompt for Arena `fuse` mode (the mechanics half). The whole file (after
     placeholder substitution: {{PARENT_A}} {{PARENT_B}} {{CONCEPT}} {{TIER}}) is sent to the
     fusion-mechanics oracle. The closed vocabulary below is DUPLICATED from proposal-oracle.md and
     kept honest by Arena.Tests/PromptVocabularySyncTests (both prompts are checked). Edit the
     vocabulary in proposal-oracle.md FIRST, then propagate here. -->

# Spellcraft Fusion-Mechanics Oracle

Two parent spells have fused into a named concept. The concept's *meaning* is already decided (its
name, element, tags, flavor, and why). Your job is its *mechanics*: emit ONE proposal-stage spell
whose clauses EXPRESS that concept — the tags are the promise, the clauses are how it is kept. A
deterministic parser is the law: unknown verbs, fields, elements, statuses, or band names are
rejected outright, and raw numbers where a band is expected are rejected. Compose only from the
vocabulary below.

## Parents

- **Parent A:** {{PARENT_A}}
- **Parent B:** {{PARENT_B}}

## Fused concept

{{CONCEPT}}

- **Tier:** the spell's `tier` is exactly {{TIER}} (fixed by recipe law — you decide mechanics, never power).

## Output format

Return ONLY a single spell object — no prose, no markdown fences. `id` is the kebab-case of the
concept's name; `tier` is exactly {{TIER}}. Shape:

```
{
  "id": "kebab-case-of-concept-name",
  "tier": {{TIER}},
  "cast": { "mode": "instant|cast", "castTime": "<band, only if mode is cast>",
            "cooldown": "<band>", "cost": { "resource": "mana", "amount": "<band>" },
            "interruptible": true, "moveWhileCasting": false },
  "delivery": { "type": "self|targetUnit|projectile|groundAoE", "...": "delivery fields" },
  "clauses": [ { "verb": "...", "...": "verb fields", "template": { } }, ... ]
}
```

## Verbs — use only these eight

- **damage** — deal elemental damage to the target. Fields: `element`, `share`.
- **heal** — restore health to the target (never revives a corpse). Field: `share`.
- **shield** — grant a temporary damage-absorbing pool. Fields: `share`, `duration`, optional `element`. An elemental shield absorbs only damage of its own element — omit `element` for a shield that blocks everything; element is a costly restriction, not flavor.
- **applyStatus** — apply a status for a duration. Fields: `status`, `duration`, `share` (its potency), optional `stacks`.
- **dispel** — remove statuses from the target. Fields: `category` (buff, debuff, dot, or all), optional `count`.
- **modifyStat** — buff or debuff a stat by a percentage for a duration. Fields: `stat` (exactly one of `moveSpeed`, `castSpeed`, `damageOut`, `damageIn`, `armor`, `critChance`, `critMult`, `evasion`, `lifesteal`, or `resist` paired with an `element`), `share`, `direction` (buff or debuff), `duration`. Use these names exactly — no others (e.g. there is no `speed` or `attackSpeed`).
- **displace** — move a subject. Fields: `subject` (caster or target), `mode` (teleport, dash, push, or pull), optional `distance`. displace has no `share`; its magnitude is the `distance` band.
- **spawnZone** — create a ground zone that ticks its clauses each interval. Fields: `shape`, `size`, `duration`, `tickInterval`, `affects` (enemies, allies, or all), and `tickClauses` (an array of clauses). It has **no `share` of its own** — the magnitude lives in the `share`s inside its `tickClauses`.

## Deliveries — choose exactly one per spell

- **self** — affects the caster (use for shields, self-buffs, self-heals).
- **targetUnit** — one enemy at range. Field: `range`.
- **projectile** — a bolt that flies to the enemy. Field: `speed`.
- **groundAoE** — a ground area at the enemy's position. Fields: `shape` (circle, cone, line, or ring), `size`.

## Cast modes

- **instant** — no windup (a little weaker; omit `castTime`).
- **cast** — a telegraphed windup (`castTime`); interruptible.

## Elements

fire, water, earth, air, light, shadow, nature, arcane.

## Statuses — for `applyStatus`

- Damage-over-time: burn, poison, bleed.
- Crowd control: freeze, stun, root, silence, disarm, fear.
- Stat modifiers: chill, slow, weaken, vulnerable, haste, blind.
- Utility: shock, regen, stealth, unstoppable, invulnerable.

## Qualitative bands — never write raw numbers; use these words

- **cast time** (`castTime`): instant, quick, normal, long.
- **duration** (`duration`): instant, short, medium, long.
- **cooldown** (`cooldown`): short, medium, long.
- **size / range / distance** (`size`, `range`, `distance`): tiny, small, medium, large.
- **projectile speed** (`speed`): slow, fast.
- **tick interval** (`tickInterval`): fast, medium, slow.
- **cost** (`amount`): trivial, low, moderate, high.
- **amplify** (in an `ifStatus` template): minor, major, extreme.

## Templates — only these two, optional, attached to a clause via `"template"`

- **ifStatus** — if the target already has a status, amplify this clause and optionally consume it and run extra clauses:
  `{ "kind": "ifStatus", "status": "freeze", "consume": true, "amplify": "major", "also": [ ... ] }`.
- **onHit** — when this clause deals damage, run follow-up clauses on the victim:
  `{ "kind": "onHit", "clauses": [ ... ] }`.

## Shares

Every magnitude is a `share`: a fraction of the tier's power budget. A single spell's clause
shares should sum to about 1.0 — e.g. a bolt that deals damage and applies a status might be
`damage` 0.7 plus `applyStatus` 0.3. Bigger share means bigger effect.

## Express the concept

Let every tag be legible in a mechanic: a `damage` tag wants a damage clause, a `ward` tag a shield
or an armor/resist buff, a `control` tag a crowd-control status or a pull, `movement` a displace,
`conceal` stealth/blind or a zone, `area` a groundAoE or a zone, `duration` a medium/long effect or
a zone. Where a tag names something the engine has no verb for (summon, transform, perceive), express
its spirit with the closest available mechanic. Clause shares should sum to about 1.0. Return only
the single JSON spell object, at tier {{TIER}}, that a stranger would recognize as the concept.
