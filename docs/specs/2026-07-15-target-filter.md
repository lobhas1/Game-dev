# Target-filter round — self-exclusion for offensive effects

## The rule (ratified)

> An entity's own offensive effects — damage, offensive statuses, and displaces — NEVER apply to the
> caster. Beneficial self-effects — heal, buffs, stealth, regen, self-dash — continue to apply
> exactly as today.

This is a **sim-law change**: `src/Spellcraft` targeting is in scope and the new behavior becomes the
default. It is enforced at the single effect-application choke point (`Sim.RunClause`), so every path
— a `groundAoE` delivery whose radius covers the caster, a zone ticking over its own owner, an
`onHit`/`also` trigger, a displace — is filtered identically.

### Offensive vs beneficial (the classification, frozen)

An effect landing on the caster is **excluded iff it is offensive**:

| verb | offensive when | beneficial (still applies to self) |
|---|---|---|
| `damage` | always | — |
| `applyStatus` | `StatusCatalog.IsDebuff(status)` — DoTs, CC, harmful stat-mods | buffs: haste, regen, unstoppable, invulnerable, stealth |
| `modifyStat` | `AmountPct < 0` (a debuff) | `AmountPct > 0` (a buff) |
| `displace` | `Subject != "caster"` (pushing/pulling a unit) | `Subject == "caster"` (self-dash / self-teleport) |
| `heal`, `shield`, `dispel`, `spawnZone` | never | all |

The excluded effect produces **no event** — its projection line simply does not exist (never a
"resisted"/"absorbed" line; the effect did not happen). Effects on the OPPONENT are untouched.

## Evidence (from the committed corpus — the self-hits this rule removes)

| showcase | caster self-hit today |
|---|---|
| blaze | 115.2 own area damage |
| conflagration | 143.36 own area damage |
| lighthouse | 102.4 own area damage |
| hearthstone | own blast absorbed by its own shield (wastes the ward) |

## Pre-registered regression criteria

- **(a) Phase A C1–C4 re-run OFFLINE** on the existing v4 kit corpus (`arena/kits`), same seeds
  (1..5) and config (hpScale 8, mana 250, amplifyMajor 1.75), judged at the **registered v4
  thresholds**: C1 first-pass ≥70%; C2 median duration 15–90s; C3 every fight ≥3 distinct verbs;
  C4 ≥50% of fights show a lead swing. C1 is unchanged (no re-generation); C2–C4 are recomputed on
  the new fights. Any criterion failing → **STOP, file the FAIL doc, no silent retuning** — a rule
  this deep is allowed to demand a balance round, and we want to know.
- **(b) The golden frost-ember seed-1 fight is re-blessed** — the new outcome is recorded and the
  old golden is **archived, never overwritten**.
- **(c) A diff report** across the 3 M1 fight replays and 26 showcases: per file, exactly which
  projection lines vanished. **Expected: ONLY self-offense lines** (a caster's own damage / offensive
  status / non-self displace on itself). Anything else moving — a line changing rather than
  vanishing, an opponent line shifting, a new line — is a **FAIL**.

## Scope and immutables

In scope: `src/Spellcraft/**` targeting (the choke-point guard). Byte-untouched: `prompts/**` (all
three shas survive — `d08bf90c…`, `dadc702f…`, `cd1bcf68…`), `schema/`, all experiment docs, all
registered thresholds (the four Phase A thresholds do not move — they are the bar this change is
judged against).

## Downstream (step 4 — flag for the Unreal re-sync)

The 3 M1 replays, 26 showcases, `manifest.json`, LF references, and the coverage doc are regenerated;
every changed file is flagged. The Unreal repo re-syncs these at the start of act one, not before.
