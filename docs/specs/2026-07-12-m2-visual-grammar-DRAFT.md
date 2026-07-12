# M2 Visual Grammar — DRAFT v0 (argument copy; becomes law only after review)

## Laws
1. DERIVATION. Every spell's look derives from its JSON through this grammar. No
   per-spell authored effect, ever. The vocabulary is authored once; new spells
   arrive dressed.
2. TRUTH. Visuals never alter, delay, or invent events. G1 stands forever: full
   visuals on, REPLAY| testimony byte-identical at any speed. Where an event
   carries data (positions, radii, timings), the visual DEFERS to the event; band
   tables below apply only to cosmetic-only quantities.
3. TWO-LEVEL ELEMENT (data-discovered). The concept's element sets the spell's
   base palette. A clause's element (when present) tints that clause's effect;
   absent, it inherits. Bare spells (no concept): palette of the highest-share
   clause's element; none anywhere → arcane-neutral.
4. LEGIBILITY. Every mechanical fact in the events has a distinguishable signature
   at gameplay distance, and decoration never impersonates mechanics — nothing
   damage-shaped on a spell that doesn't damage. (This law is what G3 tests.)
5. RESTRAINT. Per-spell simultaneous-emitter cap; effects scale with bands but may
   never occlude the arena or another entity's status row.

## Elements → palette / motif / feel
| element | primary→secondary | motif | feel |
|---|---|---|---|
| fire | ember orange → deep red | rising sparks, licks | additive, hot glow |
| water | deep blue → cyan | droplets, flowing arcs | refractive, soft |
| earth | ochre → slate | shards, falling dust | opaque, heavy |
| air | pale cyan → white | streaks, ribbons | translucent, quick |
| light | warm white → gold | rays, glints | bloom, clean |
| shadow | violet → near-black | soft smoke, tendrils | darkening, matte |
| arcane | magenta → violet | geometric runes | crisp, synthetic |
| nature | leaf green → moss | leaves, spores | organic, drifting |

## Verbs → effect archetype
| verb | signature |
|---|---|
| damage | impact burst at target + brief flash; scale from share |
| heal | motes rise and converge on target; soft pulse on completion |
| shield | translucent shell; hits ripple at contact point; expiry pops gently. ELEMENTAL shields are element-tinted and wear a small element sigil; omni shields are neutral white — the pricing trade, made visible |
| applyStatus | status sigil rises, docks over head (see status table); aura tint while active; removal shatters the sigil |
| modifyStat | brief arrow stream (up gold / down violet) + faint ring while active; stat glyph from status table style |
| displace | motion trail on the moved body; dust burst at origin and landing; push vs pull read by trail direction |
| spawnZone | ground decal in delivery shape, element-tinted low-alpha fill; edge pulses on each tick event; expiry contracts and fades |
| summon / transform / dispel (spec'd, no corpus material yet) | rising pillar + silhouette / swirl-wrap / outward cleanse ring that pops sigils |

## Deliveries → trajectory & telegraph
targetUnit: projectile with element trail, interpolated cast-event → effect-event;
gap ≈ 0 → hitscan streak. self: radial burst from caster. groundAoE: telegraph decal
from cast event, fills at zone-spawn event. Lines/areas take shape+size from the
events where present (Law 2), band table otherwise.

## Bands → cosmetic intensity (cosmetic-only; events outrank)
share → effect scale ≈ 0.6 + 0.8×share. size tiny/small/medium/large → decal scale
tiers matched to sim radii. amplify minor/major/extreme → detonation flourish tier:
spark ring / shockwave / brief white-flash. castTime → windup animation length from
the cast event's own timing. interval → pulse cadence, from tick events when present.
duration → NEVER from bands: things end when their removal event says so.

## Statuses → sigil + body effect (full vocabulary, corpus or not)
burn flame-sigil+ember drip · chill snowflake+frost rim · freeze ice-block sheath ·
slow weight-glyph+dragging trail · root stone-clasp at feet · stun orbiting stars ·
blind dark band at eye-line · stealth 40% alpha + shimmer (replays are omniscient:
marked, not hidden) · haste speed lines · silence crossed rune · fear jagged violet
aura · poison green drip · bleed red drip · shock arc flicker · weaken sagging aura ·
invulnerable gold shell · unstoppable steady red glow · disarm falling-weapon glyph

## Verbless events
oom: blue fizzle + slump posture · death: grey-out, fall, element-tinted wisp ·
castFailed: fizzle puff + red X sigil · fightEnd: winner banner (per M1)

## Gate hooks
G1 regression unchanged. G2: every corpus token above has an implemented asset;
one screenshot per verb & delivery family; zero fallback-placeholder renders across
all 26 showcases. G3 per the registered protocol, fresh tester.