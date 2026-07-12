# M2 Visual Grammar — v1.1 (LAW, ratified 2026-07-12)

## Ratified decisions (2026-07-12)
1. Art direction: clean and stylized — simple readable shapes, strong color. Impact
   is heavy (Law 6); gore is not part of this game.
2. Element palettes: ratified as tabled below, unchanged.
3. Floating damage numbers: kept — element-tinted, scaled with the amount, always
   facing the camera.
4. Camera: the three-quarter angled view is canonical everywhere — showcases, fights,
   and the eventual live game. The straight-down overhead is retired to a debug toggle.
5. Impact register: heavy and visceral — hit-stop, screen shake, crunch — governed by
   Law 6 below.

Amended to v1.1 (2026-07-12): added vulnerable and regen to the status table;
specified teleport treatment under displace. Gaps found by the M2-A corpus census.

## Law 6 — Juice is data
Impact feedback derives from event data and modulates ONLY the shared playback clock
and the camera — never an individual event. Hit-stop: pause the playback clock 40 ms
when a damage event removes ≥25% of the victim's max HP, 90 ms at ≥50%, none below.
Screen shake: amplitude proportional to the damage fraction, capped at a readable
maximum; non-damage events never shake. Crunch: three impact-sound tiers keyed to the
same fractions. New gate G1b: one full playback with juice enabled and one with juice
disabled must produce byte-identical REPLAY| logs, and the 1x/4x invariance test must
also pass with juice enabled.

## Camera law
Three-quarter angle, pitched down about 55 degrees, framing both fighters with
comfortable margin, smoothly tracking their midpoint without cuts. Overhead becomes
a debug toggle only.

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
| displace | Push and Dash: motion trail on the moved body, dust bursts at origin and landing; Teleport: no trail — an implosion flash at the origin and an appearance flash at the destination |
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
invulnerable gold shell · unstoppable steady red glow · disarm falling-weapon glyph ·
vulnerable downward-cracked-shield glyph, red tint (defense is broken) ·
regen green plus sigil, small green motes rise on each tick (healing rises, harm drips)

## Verbless events
oom: blue fizzle + slump posture · death: grey-out, fall, element-tinted wisp ·
castFailed: fizzle puff + red X sigil · fightEnd: winner banner (per M1)

## Gate hooks
G1 regression unchanged (juice on, per Law 6 / G1b). G2: every corpus token above has
an implemented asset; one screenshot per verb & delivery family; zero
fallback-placeholder renders across all 26 showcases. G3 per the registered protocol,
fresh tester.



## Direction addendum v2 — the beauty pass (banked 2026-07-12; NON-BINDING for M2 gates)
Ratified direction for a future visual round, not a requirement of the current build.
1. DIEGETIC SUBSTANCE. Effects are made of their element's physical substance doing
   physically readable things, not abstract glows. Projectiles especially: the flying
   body itself is elemental — a fire throw is a ball of fire, a water throw a coherent
   glob of water, an earth throw a hurled rock — the trail is a consequence of the
   substance, not a costume on a glowing dot. Shields likewise: a water shield is
   flowing water shaped into a shell around the target.
2. NO PERFECT PRIMITIVES. Real substances are irregular. Effect bodies avoid perfectly
   symmetric spheres and discs: noise in the silhouette, rotation, flicker, and
   surface motion. (All examples illustrative, not literal.)


3. CONCEPT MOTIFS (the third leg). A future round restores a visual field to the
   naming oracle, constrained to a CLOSED vocabulary of authored motif primitives
   (e.g. winged, tear, spiral, serpent, bloom, shatter — illustrative). The oracle
   composes a concept's look from that vocabulary exactly as it composes mechanics
   from verbs; each motif is authored once and inherited by every matching concept
   forever. Motifs DECORATE, verb signatures INFORM: a motif may never impersonate
   another verb's signature (Law 4 outranks). Vocabulary widens by data-vote, like
   verbs. This round carries its own prompt change, new sha, and a pre-registered
   blind concept-to-visual matching test.


4. Structure unchanged: richness lives in the element-motif × verb/delivery matrix,
   authored once per cell, inherited by every spell forever (Law 1). Laws 4 and 5 —
   legibility and restraint — outrank spectacle: irregularity must never cost
   readability. Law 2 truth is untouchable.
Scope: opens as its own milestone after M2's gates close.

5. THE TWIN TEST (banked gate for the beauty round): distinct concepts must be
   perceptually distinct. Protocol sketch: show a stranger paired playbacks of
   different-concept spells and ask same-or-different; threshold pre-registered
   when the round opens. Visual sameness between spells with near-identical
   mechanics is treated as a CANON question (convergence candidate), never
   masked by cosmetic differentiation.