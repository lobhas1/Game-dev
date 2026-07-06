# Spell Verb Specification — v0.2

**Scope.** The closed vocabulary a compiled spell may reference, and the contract each symbol imposes on the engine. This is the *interpreter surface* of the spellcraft system: the oracle (LLM) composes sentences from these symbols at generation time; the compiler resolves budgets into numbers; the engine executes. Nothing in this document is written by an LLM at runtime, ever.

**The law, restated.** Generative content scales by growing *data* under a fixed interpreter. Every symbol below is a promise of hand-written, tested engine code — implemented once, then free forever. The oracle may never reference a symbol not in this document; concepts outside the grammar are quantized to the nearest expressible composition and their prose regenerated to match.

**v0.2 changelog.** Tier-3 dragons (23–26) upgraded from stubs to full contracts, so the refusal to build them is priced, not vague. Three verbs added after passing the basis-vector test: **27 `modifyAbility`** (ability-economy axis), **28 `commandMinions`** (friendly-AI channel), **29 `castSpell`** (the grammar referencing itself). `spawnZone` gains enter/exit clause hooks (traps become compositions, not a verb). `disarm` added to the status catalog. Convention: **verb numbers are stable IDs, not ordering** — new entries take the next number and sit in their tier; nothing ever renumbers.

**How to read the tiers.**

| Tier | Meaning | Honest cost (solo dev) |
|---|---|---|
| **T0** | Minimum viable combat. Nothing ships without all eight. | Weeks. |
| **T1** | Composition depth. Combat becomes *interesting*. | Weeks–months, incremental. |
| **T2** | Systemic verbs touching AI, factions, entity lifecycle. | Each one is a project. |
| **T3** | Dragons. Engine-wide implications. Do not pave speculatively. | Months each. Probably never. |

**Conventions.** Signatures are typed pseudocode (TypeScript-flavored), deliberately engine-neutral — the JSON spell schema is the contract; the engine language (recommendation: C#) is an implementation detail. `Magnitude` and `Duration` are always **compiler-resolved concrete numbers**: verbs never see budget shares, tiers, or rarity. All verbs are deterministic given `(worldState, params, rng)` — the RNG is seeded per cast for replay, testing, and any future networking.

---

## 1. Shared types

```ts
type EntityRef   = OpaqueId            // stable handle into world state
type Vec3        = { x, y, z }         // gameplay logic runs on the XZ plane; y exists for launch/terrain
type Element     = 'fire'|'water'|'earth'|'air'|'light'|'shadow'|'nature'|'arcane'
type Magnitude   = number              // resolved by compiler: tierBudget × share × multipliers
type Duration    = number              // seconds, compiler-resolved
type StatId      = 'moveSpeed'|'castSpeed'|'damageOut'|'damageIn'|'armor'|'resist(element)'|'critChance'|'lifesteal'

type TargetSet = {
  units: EntityRef[]                   // resolved by the delivery layer
  point?: Vec3                         // ground-targeted deliveries
  direction?: Vec3
}

type ImpactCtx = {
  point: Vec3, direction: Vec3, normal?: Vec3
  deliveredBy: DeliveryType            // lets clauses behave differently per delivery (rare, allowed)
}

type EffectCtx = {
  caster: EntityRef
  spellId: SpellId, clauseIndex: number
  rng: SeededRng
  world: WorldApi                      // queries + mutation requests; verbs never touch renderer state
  impact: ImpactCtx
  events: EventBus                     // every verb EMITS; VFX, audio, combat log, AI all subscribe
  depth: number                        // trigger-recursion depth (see §2 guards)
}
```

**Every verb returns a structured result AND emits events.** Results feed conditional clauses in the same cast; events feed everything else (VFX cues, combat log, AI threat, quest triggers). A verb that returns `void` is a verb you cannot build combos, logs, or telemetry on.

---

## 2. Resolution pipeline

Order of operations for one cast. Deviating from a fixed order is how "it works in my tests" games die in players' hands.

1. **Cost & gate check** — resource, cooldown, silence/stun state, cast-while-moving rules.
2. **Cast layer** — windup/channel/charge timing, interruption rules.
3. **Delivery layer** — resolves a `TargetSet` + `ImpactCtx` (projectile flight, chain hops, AoE queries happen here).
4. **Clause loop** — for each effect clause, in listed order: hit/evasion check where applicable → shields absorb before health → verb executes → result recorded.
5. **Trigger phase** — templates (§7) enqueue follow-up clauses. **Guards: max trigger depth 3, max enqueued clauses per cast 12, a clause may not re-trigger its own template.** Detonate-chains and on-hit loops are the classic infinite-recursion factory.
6. **Event flush** — one batched emission; subscribers (VFX/audio/log/AI) consume after state is consistent.

**Stacking rules** (global, per status category unless overridden): DoTs stack to `maxStacks` with refresh; stat modifiers take the strongest per `StatId` per source-class; hard CC does **not** stack — it refreshes, and repeated CC within 10 s applies diminishing returns (100% → 50% → 25% → immune 5 s). Ship DR in T0 or your first `applyStatus` chain-stun build will delete the game.

---

## 3. Cast layer

```ts
type CastMode = 'instant'|'cast'|'channel'|'charge'|'passive'|'reactive'|'aura'
type CastSpec = {
  mode: CastMode
  castTime?: Duration          // cast, charge(max)
  channelDuration?: Duration   // channel: clauses tick on channelInterval
  cooldown: Duration
  cost: { resource: 'mana'|'stamina'|'health', amount: Magnitude }
  interruptible: boolean
  moveWhileCasting: boolean
  reactiveTrigger?: TemplateId // reactive mode only (e.g. onHitTaken)
}
```

Budget interaction (compiler-owned, illustrative): `instant ×0.85`, `cast ×1.0`, long `cast ×1.25`, `channel ×1.3`, `charge ×1.0–1.4` by charge fraction, `reactive ×0.9`. Slow casts buy big effects; that trade is where spell identity lives.

| Mode | Notes / engine cost |
|---|---|
| instant | Trivial. |
| cast | Windup, interrupt window, animation hook. T0. |
| channel | Repeated clause ticks; interruption mid-effect; T0–T1. |
| charge | Hold-to-empower; magnitude scaling by hold time. T1. |
| passive | Always-on stat clauses; no delivery. T1. |
| reactive | Fires on a trigger (block, hit-taken). Needs event bus maturity. T2. |
| aura | Persistent zone glued to caster = `spawnZone(follow: caster)`. T1. |

---

## 4. Delivery layer

Delivery resolves *who and where*; it owns no effect logic. Each mode returns `TargetSet + ImpactCtx`.

| Mode | Key params | Budget × | Tier | Engine notes |
|---|---|---|---|---|
| `self` | — | ×0.8 | T0 | Trivial. |
| `melee` | reach, arcDeg | ×0.9 | T0 | Cone query in front of caster. |
| `targetUnit` | range, requiresLoS | ×1.0 | T0 | Click-target; LoS raycast. |
| `projectile` | speed, radius, pierce(n), gravityArc?, homing? | ×1.0 (+0.15 pierce, +0.2 homing) | T0 | Physics or kinematic sweep; impact point feeds `displace(to:'impact')`. Homing needs target-leading; arc needs true 3D. |
| `groundAoE` | shape: circle\|cone\|line\|ring; radius/angle/length; telegraph | ×1.6 | T0 | Overlap query + **telegraph rendering** — the telegraph is half the implementation. |
| `nova` | radius | ×1.5 | T1 | Self-centered circle; cheap once groundAoE exists. |
| `beam` | width, length, duration, tickInterval | ×1.4 | T1 | Continuous swept query; interacts with channel mode. |
| `chain` | jumps, hopRange, falloff/jump | ×1.4 | T1 | Nearest-neighbor hops, no revisits; falloff multiplies clause magnitudes per hop. |
| `atPoint` | range | ×1.0 | T1 | For summon/zone placement without an AoE. |

---

## 5. Effect verbs

Format per verb: signature → args → returns → contract & implementation notes.

### Tier 0 — the MVP eight

---

**1. `damage`**
```ts
damage(ctx, target: EntityRef, p: {
  amount: Magnitude, element: Element,
  canCrit?: boolean, tags?: string[],           // 'melee','dot','detonation'… feeds triggers & resists
  leechPct?: number                              // % of final damage returned to caster as heal
}) -> { final: number, absorbedByShield: number, crit: boolean, killed: boolean, overkill: number }
```
The spine of combat. Pipeline inside: evasion → crit roll (seeded rng) → elemental resist → armor → shields → health. `killed` feeds `onKill` triggers; `tags` let statuses like `vulnerable('fire')` and templates discriminate. Leech is a parameter here, not a separate verb — one verb, one pipeline, or your damage math forks.

---

**2. `heal`**
```ts
heal(ctx, target: EntityRef, p: { amount: Magnitude, canOverhealToShield?: boolean })
  -> { effective: number, overheal: number }
```
Respects healing-received modifiers (`weaken` may cut it) and death state (no healing corpses — that's `resurrect`, T3). Overheal-to-shield is a flag because it changes balance class.

---

**3. `shield`**
```ts
shield(ctx, target: EntityRef, p: { amount: Magnitude, duration: Duration, element?: Element })
  -> { shieldId, amount }
```
Temporary absorb pool consumed before health. Elemental shields absorb only their element (rich combo space, cheap to add). Stacking: additive per source, cap at `maxHp × k`.

---

**4. `applyStatus`**
```ts
applyStatus(ctx, target: EntityRef, p: {
  status: StatusId, duration: Duration, stacks?: number, potency?: Magnitude
}) -> { applied: boolean, resisted: boolean, stacksNow: number, refreshed: boolean }
```
Gateway to the entire status catalog (§6). Checks immunities (`unstoppable`), applies DR for hard CC, resolves stacking rule from the catalog. **Potency is compiler-resolved** — a T5 burn ticks harder than a T1 burn through this number, never through a different status id.

---

**5. `dispel`**
```ts
dispel(ctx, target: EntityRef, p: {
  category: 'buff'|'debuff'|'dot'|'all', element?: Element, count: number,
  order: 'newest'|'oldest'|'strongest'
}) -> { removed: StatusInstance[] }
```
Counterplay primitive. Returning the removed list matters: `detonate` variants and "steal a buff" designs read it.

---

**6. `modifyStat`**
```ts
modifyStat(ctx, target: EntityRef, p: {
  stat: StatId, amountPct: Magnitude,            // ±; percent-based to survive rebalances
  duration: Duration
}) -> { modifierId }
```
Buffs/debuffs as first-class handles (dispellable, inspectable in UI). Strongest-per-stat-per-sourceclass stacking, per §2.

---

**7. `displace`**
```ts
displace(ctx, subject: EntityRef, p: {
  mode: 'teleport'|'dash'|'push'|'pull'|'launch'|'swap',
  to?: 'point'|'impact'|'caster'|'behindTarget',
  distance?: Magnitude, speed?: Magnitude, swapWith?: EntityRef
}) -> { finalPos: Vec3, moved: number, blocked: boolean, collidedWith?: EntityRef }
```
Your "teleportation or not" boolean, done right: six modes, one verb. Contract: teleport **validates destinations** (navmesh reachable, not inside geometry, not out of bounds — clamp to nearest valid); dash/push/pull are physical (body-blocking, wall-slam candidates via `collidedWith`); `launch` requires the y axis and is the one mode you should defer if you go 2.5D; `unstoppable` immunes all of it. **The most expensive T0 verb** — it touches physics, navmesh, camera, and (someday) net prediction simultaneously. Budget accordingly.

---

**8. `spawnZone`**
```ts
spawnZone(ctx, at: Vec3|{follow: EntityRef}, p: {
  shape: 'circle'|'cone'|'line'|'ring', size: Magnitude,
  duration: Duration, tickInterval: Duration,
  tickClauses: Clause[],                          // full recursion into the grammar — the composition engine
  onEnterClauses?: Clause[], onExitClauses?: Clause[],  // traps, sanctuaries, toll-gates
  affects: 'enemies'|'allies'|'all',
  visionModifier?: 'reveal'|'obscure'
}) -> { zoneId }
```
Ground effects, auras (`follow`), darkness, consecrations, damage pools. `tickClauses` is what makes the grammar generative rather than a menu — a gravity well is just a zone ticking `displace(pull)`, and the enter/exit hooks make traps and ambushes compositions rather than verbs. Guard: zones may not spawn zones beyond depth 1.

---

### Tier 1 — composition depth

---

**9. `summon`**
```ts
summon(ctx, at: Vec3, p: {
  archetype: SummonId,                            // closed list: 'wisp','golem','decoy','turret','swarm'
  count: number, duration: Duration, inheritStatsPct: Magnitude
}) -> { entities: EntityRef[] }
```
**The hidden-cost champion of T1**: every archetype needs AI, pathfinding, animation, and a leash policy. Cap concurrent summons per caster. Archetypes are a closed list exactly like statuses — the oracle picks, never invents.

---

**10. `mark`**
```ts
mark(ctx, target: EntityRef, p: { markId: string, duration: Duration, maxStacks: number })
  -> { stacksNow: number }
```
Inert counter on a target; does nothing alone. Exists so `detonate` and `ifStatus` templates have fuel. Cheap to build, huge combo yield.

---

**11. `detonate`**
```ts
detonate(ctx, target: EntityRef, p: {
  consume: StatusId|MarkId, perStackClauses: Clause[], radiusOnDetonate?: Magnitude
}) -> { consumedStacks: number, results: EffectResult[] }
```
Consumes marks/statuses, executes clauses scaled by stacks — the payoff verb for every setup archetype (frost shatter, poison pop). Subject to trigger-depth guards; the classic runaway loop is detonate→applies status→detonate.

---

**12. `drainResource`**
```ts
drainResource(ctx, target: EntityRef, p: {
  resource: 'mana'|'stamina', amount: Magnitude, transferPct?: number
}) -> { drained: number, transferred: number }
```
Anti-caster tech. Keep `health` out of this verb — health drain is `damage(leechPct)` or you fork the damage pipeline.

---

**13. `createBarrier`**
```ts
createBarrier(ctx, p: {
  from: Vec3, to: Vec3, height?: Magnitude,
  hp: Magnitude, duration: Duration,
  blocks: ('movement'|'projectiles'|'vision')[]
}) -> { barrierId }
```
Walls. Engine cost is not the mesh — it's **dynamic navmesh cutting** so AI reroutes, plus projectile collision, plus vision occlusion if you block sight. One of the highest fun-per-verb ratios in the genre; also one of the most bug-fertile.

---

**14. `reveal`**
```ts
reveal(ctx, area: {point: Vec3, radius: Magnitude}|EntityRef, p: { duration: Duration })
  -> { revealed: EntityRef[] }
```
Counters `stealth` and `obscure` zones. Trivial once a vision system exists; meaningless before one.

---

**15. `taunt`**
```ts
taunt(ctx, targets: EntityRef[], p: { duration: Duration }) -> { taunted: EntityRef[] }
```
Forces AI target lock onto caster. Pure AI-layer verb — its entire cost lives in the threat system honoring it.

---

**16. `reflect`**
```ts
reflect(ctx, target: EntityRef, p: {
  kind: 'projectile'|'melee'|'spell', pct: Magnitude, duration: Duration, charges?: number
}) -> { reflectHandle }
```
Hooks the damage pipeline pre-mitigation. Reflected projectiles need re-owned physics objects (team swap, new collision mask) — fiddlier than it sounds.

---

**27. `modifyAbility`** *(added v0.2)*
```ts
modifyAbility(ctx, target: EntityRef, p: {
  scope: 'all'|'tag(spellTag)'|'spell(spellRef)'|'next',
  op: 'cooldownFlat'|'cooldownPct'|'reset'|'refundCost'|'empower'|'addCharge',
  amount?: Magnitude,                             // seconds, %, refund %, empower multiplier
  charges?: number                                // for 'next'/'empower': how many casts it rides
}) -> { affected: SpellId[], handle?: ModifierHandle }
```
The ability-economy axis — the one dimension nothing else in the grammar touches: the caster's own spell-system state. Enables reset-on-kill, "your next spell is free," and proc-CDR archetypes via templates (`onKill` + `reset` is half the genre's power fantasies). Implementation is pure bookkeeping in the ability system — **the cheapest verb per unit of design value in this document.** Guards: a spell may never reset or empower *itself* (the recursion guard's sibling); one empower handle per spell, no stacking. Balance warning: cooldown reduction is the most warping economy stat in the genre — price it punitively in the budget grammar.

---

### Tier 2 — systemic verbs (each one is a project)

---

**17. `tether`**
```ts
tether(ctx, anchor: EntityRef|Vec3, target: EntityRef, p: {
  maxRange: Magnitude, duration: Duration, onBreakClauses: Clause[], onTickClauses?: Clause[]
}) -> { tetherId }
```
Leash mechanics; continuous distance monitoring; break-vs-expire semantics must be unambiguous.

**18. `transform`**
```ts
transform(ctx, target: EntityRef, p: {
  form: FormId,                                   // closed list: 'sheep','statue','shade'
  duration: Duration, statMap: Partial<Record<StatId, number>>, suppressAbilities: boolean
}) -> { transformHandle }
```
Polymorph. Touches animation, ability bars, AI, and every "is this unit currently X" query in the codebase. The definition of cross-cutting.

**19. `lifeLink`**
```ts
lifeLink(ctx, a: EntityRef, b: EntityRef, p: { sharePct: Magnitude, duration: Duration, direction: 'both'|'aToB' })
  -> { linkId }
```
Damage sharing. Guard against link chains and self-links; order of operations with shields must be pinned in tests.

**20. `absorb`**
```ts
absorb(ctx, target: EntityRef, p: {
  element: Element|'all', pct: Magnitude, convertTo: 'mana'|'health'|'shield', duration: Duration
}) -> { absorbHandle }
```
Damage-to-resource conversion; sits in the same pipeline slot as `reflect` — build them together.

**21. `charm`**
```ts
charm(ctx, target: EntityRef, p: { duration: Duration }) -> { controlHandle }
```
Faction flip. AI must fight for the caster, then *cleanly revert* — target selection, threat tables, and summon ownership all need faction-aware rewrites. PvE-only until you enjoy pain.

**22. `banish`**
```ts
banish(ctx, target: EntityRef, p: { duration: Duration }) -> { banished: boolean }
```
Removes an entity from play (untargetable, frozen, invulnerable) and returns it. Every system that iterates entities needs a banish-aware filter — grep-the-codebase tier.

**28. `commandMinions`** *(added v0.2)*
```ts
commandMinions(ctx, p: {
  order: 'attack'|'moveTo'|'recall'|'holdPosition',
  target?: EntityRef, point?: Vec3,
  duration?: Duration                             // priority window before autonomy resumes
}) -> { commanded: EntityRef[] }
```
The friendly half of the AI channel: `taunt` coerces *enemy* targeting; until now nothing directed your own units. Turns summoner play from spawn-and-pray into an archetype (send the golem, recall the swarm, hold the choke). Implementation: an order queue that outranks autonomous behavior and degrades gracefully — dead target, unreachable point, order expiry. Meaningless before `summon` is stable; build strictly after it, never alongside.

---

### Tier 3 — dragons (fully priced so the refusal is informed)

These contracts exist to make "no" a costed decision instead of a vague one. Each dragon's real price is architectural virality: systems far from combat must know it exists.

---

**23. `resurrect`**
```ts
resurrect(ctx, corpse: EntityRef, p: {
  hpPct: Magnitude, asAlly?: boolean,             // asAlly = necromancy: uses summon ownership/leash/cap machinery
  decayWindow: Duration                            // corpses older than this refuse
}) -> { entity?: EntityRef, failed?: 'noCorpse'|'expired'|'blocked' }
```
The verb itself is ten lines; the price is the **corpse lifecycle system** T0 combat never needed — death must produce a persistent corpse entity with a decay timer, cleanup policy, and save-state, retroactively touching every kill path. Encounter design landmine: every boss and elite needs an explicit resurrectable/blocked flag or your raid design collapses. `asAlly` must route through summon caps or necromancer scaling breaks the game by headcount alone.

---

**24. `terraform`**
```ts
terraform(ctx, area: {point: Vec3, shape: Shape, size: Magnitude}, p: {
  op: 'raise'|'lower'|'pit'|'ramp', amount: Magnitude,
  duration: Duration                               // duration-bounded ONLY; permanent deformation entangles the save system
}) -> { deformId, navRebuilt: boolean }
```
The verb that eats engines, itemized: realtime navmesh rebuild at interactive rates; controller and camera surviving sudden height deltas under the player's feet; projectile collision and line-of-sight against a *dynamic* heightfield; AI re-pathing mid-behavior. **Decision coupling: wanting terraform ever means choosing true 3D and a dynamic-nav architecture on day one** — it silently revokes the 2.5D shortcut recommended earlier. If the fantasy isn't core, fake 90% of it with `createBarrier` + `slow` zones and keep your engine.

---

**25. `timeDilate`**
```ts
timeDilate(ctx, scope: {area: Zone}|{target: EntityRef}, p: {
  factor: Magnitude,                               // clamp 0.2–2.0; outside this, readability dies
  duration: Duration, affects: 'enemies'|'allies'|'all'
}) -> { fieldId }
```
Requires a **per-entity clock from day one**: every duration, DoT tick, cooldown, animation, and projectile integration step must consume entity-local `dt`. Retrofitting means touching every timed system in the codebase — hence decide day 1 or never. Interaction rules must be pinned and frozen: statuses tick in the *victim's* local time, cooldowns in the *caster's*, zones in *world* time — pick once, write tests, never revisit. Add VFX/audio pitch-shift or dilated fights are unreadable.

---

**26. `possess`**
```ts
possess(ctx, target: EntityRef, p: {
  duration: Duration, breakOnDamagePct?: Magnitude,
  casterBody: 'stasis'|'vulnerableShell'
}) -> { controlHandle }
```
Honest decomposition: 80% UX engineering, 20% game logic — input routing, camera handoff, ability-bar swap (whose spells? if the target's, you've just taken a dependency on **29**), then a clean reversion where AI resumes mid-combat without amnesia. Needs a per-archetype possessable flag (bosses: no). In any networked future this is an identity and authority nightmare; single-player it's a *feature* wearing a verb's clothes.

---

**29. `castSpell`** *(added v0.2 — dragon #5, the grammar referencing itself)*
```ts
castSpell(ctx, p: {
  source: 'lastCastBy(target)'|'lastCastOnMe'|'spellRef',
  as: 'caster',                                    // executes under caster ownership, stats, and faction
  budgetClamp: 'casterTier',                       // shares re-resolved against the CASTER's tier budget
  costPolicy: 'free'|'payOriginal'
}) -> { executed?: SpellId, failed?: 'noSource'|'unclonable'|'depth' }
```
Echo, mimic, spellsteal. In a game whose spells are *player-discovered artifacts*, stealing a rival's discovery is the most on-fantasy dragon in the tier — the one T3 with genuine pull for this specific game. The viral cost is architectural: `budgetClamp` means magnitudes must be re-resolvable at runtime, so **v0.2 amends the conventions — descriptors carry budget *shares* alongside resolved numbers**, with the engine holding a server-signed per-tier budget table. The arithmetic moves client-side; the *authority* (prices, tables, canon) stays on the server. Guards: depth 1 hard — a sourced spell containing `castSpell` fails with `depth`; an `unclonable` flag on spells is the balance escape hatch you will eventually thank yourself for. Versioning: your compiled-spell format just became a runtime value exchanged between entities — every schema migration now has a compatibility story.

---

## 6. Status catalog (20)

Statuses are **data referencing shared machinery** (DoT ticker, stat modifier, CC controller) — adding one is cheap; adding a new *category* is not.

| Status | Category | Effect sketch | Stacking | Countered by | Tier |
|---|---|---|---|---|---|
| burn | DoT | fire dmg/tick, spreads on death (opt) | stacks(5), refresh | dispel, water-element zones | T0 |
| poison | DoT | nature dmg/tick, reduces healing | stacks(10) | dispel | T0 |
| bleed | DoT | physical dmg/tick, amplified by movement | stacks(5) | — | T1 |
| chill | statmod | −moveSpeed, −castSpeed | strongest | dispel | T0 |
| freeze | hard CC | full immobilize; heavy hits shatter (combo tag) | refresh, DR | unstoppable | T0 |
| shock | proc | micro-interrupt on application | none | — | T1 |
| stun | hard CC | no move/cast | refresh, DR | unstoppable | T0 |
| root | soft CC | no move; can cast | refresh, DR | unstoppable | T0 |
| slow | statmod | −moveSpeed | strongest | dispel | T0 |
| silence | soft CC | no casting | refresh, DR | — | T1 |
| disarm | soft CC | no basic attacks; casting allowed (silence's mirror) *(added v0.2)* | refresh, DR | dispel | T1 |
| blind | statmod | large miss chance | refresh | dispel | T1 |
| fear | hard CC | AI-driven flee | refresh, DR | unstoppable | T2 (AI) |
| weaken | statmod | −damageOut, −healingDone | strongest | dispel | T0 |
| vulnerable | statmod | +damageIn (optionally per-element) | strongest | dispel | T0 |
| haste | statmod | +moveSpeed, +castSpeed | strongest | — | T0 |
| regen | HoT | heal/tick | stacks(3) | — | T0 |
| stealth | utility | untargetable by AI; broken by acting | none | reveal | T2 (vision) |
| unstoppable | utility | immune to CC & displace | none | — | T1 |
| invulnerable | utility | immune to damage (never to CC — that's how you avoid degenerate turtling) | none | — | T1 |

---

## 7. Conditional / trigger templates (8)

Templates wrap clauses; they are compiler-validated structures, not free text. All obey §2 recursion guards.

| Template | Fires when | Payload | Notes |
|---|---|---|---|
| `onHit` | clause dealt damage | clauses on victim | The rider workhorse. |
| `onKill` | `damage.killed` | clauses at victim pos | Snowball tuning risk. |
| `onCrit` | `damage.crit` | clauses | — |
| `ifStatus(s)` | target has status s | amplify ×k or replace-clauses | "If frozen → shatter." Pair with `consume?: bool`. |
| `delayed(t)` | t seconds after cast | clauses at stored point | Telegraphed bombs. |
| `repeating(n, dt)` | n times every dt | clauses | Multi-wave casts. |
| `onExpire` | status/zone/summon ends | clauses at owner pos | Death-rattle designs. |
| `hpThreshold(pct, side)` | target above/below pct | amplify or gate | Execute mechanics. |

---

## 8. Composition proofs

Four spells people would call "new mechanics," written as sentences over the vocabulary above — zero new engine code.

**Blink Strike** — "teleport to my projectile's impact and blind what it hit":
```json
{ "cast": {"mode":"cast","castTime":0.4,"cooldown":9,"cost":{"resource":"mana","amount":30},"interruptible":true,"moveWhileCasting":false},
  "delivery": {"type":"projectile","speed":"fast"},
  "clauses": [
    {"verb":"displace","subject":"caster","mode":"teleport","to":"impact"},
    {"verb":"damage","element":"shadow","share":0.6},
    {"verb":"applyStatus","status":"blind","share":0.4,"duration":"short"} ] }
```

**Gravity Well** — a zone that ticks a pull:
```json
{ "cast": {"mode":"cast","castTime":0.8,"cooldown":14},
  "delivery": {"type":"groundAoE","shape":"circle","size":"medium","telegraph":true},
  "clauses": [
    {"verb":"spawnZone","shape":"circle","size":"medium","duration":"medium","tickInterval":0.25,
     "affects":"enemies",
     "tickClauses":[
       {"verb":"displace","subject":"target","mode":"pull","distance":"tiny"},
       {"verb":"damage","element":"arcane","share":0.1}]} ] }
```

**Frost Shatter** — setup/payoff via `ifStatus`:
```json
{ "delivery": {"type":"targetUnit","range":"medium"},
  "clauses": [
    {"verb":"damage","element":"water","share":0.5,
     "template":{"ifStatus":"freeze","consume":true,"amplify":2.5,
                 "also":[{"verb":"applyStatus","status":"vulnerable","duration":"short","share":0.2}]}} ] }
```

**Quantization example** — oracle proposes *"rewinds the target's wounds through time"*; no `rewind` verb exists. Compiler quantizes to `heal(large) + dispel(debuff, count:2)` and **regenerates the flavor from the quantized descriptor** ("knits the last few seconds of harm closed"), so prose never promises mechanics the interpreter can't keep. Log the miss: `rewind` enters the frequency table that decides which verb the engine grows next. That loop — canon grows continuously, interpreter grows deliberately — is the shippable meaning of "the magic evolves."

---

## 9. Paving order

1. **T0 core loop first**: `damage`, `heal`, `applyStatus` (+6 statuses: burn, chill, freeze, stun, slow, vulnerable), `displace` (teleport/dash/push only), `spawnZone`, with deliveries `self / melee / targetUnit / projectile / groundAoE` and cast modes `instant / cast`. That is a playable sandbox and the smallest thing worth pointing the oracle at.
2. `shield`, `modifyStat`, `dispel` complete T0; then `mark` + `detonate` + `ifStatus` before anything else in T1 — setup/payoff is the highest fun-per-line addition in the document. `modifyAbility` slots in here too: near-zero engine cost, and reset/empower archetypes multiply what eight verbs can express.
3. `summon` and `createBarrier` only when AI and navmesh are stable; they consume whatever stability exists. `commandMinions` strictly after `summon` has proven stable in players' hands, never alongside it.
4. T2 by demand: let the oracle's quantization-miss frequency table (§8) vote, not your enthusiasm.
5. T3 is now fully priced — that's what v0.2 bought. The correct number to *implement* at launch remains plausibly zero. The one standing exception: `castSpell` is the only dragon whose fantasy is native to this game — revisit it when, and only when, players own discoveries worth stealing. `terraform` and `timeDilate` are day-one architecture decisions wearing verb costumes: decide *against* them explicitly, in writing, or they will haunt every refactor.

**Final count: 29 verbs, 20 statuses, 9 deliveries, 7 cast modes, 8 templates ≈ 73 symbols.** The record so far: estimated 60, v0.1 shipped at 69 under active pushback, v0.2 sits at 73 — and this round the growth was commissioned by the person the pushback was protecting. The ratchet clicks politely, by invitation. The 30th verb slot stays empty on purpose: the next candidate must span a dimension no composition reaches, and every candidate evaluated for v0.2 that failed that test — stagger, phase, sacrifice, stealBuff, interrupt — failed it as a synonym or a composition. Guard the slot.