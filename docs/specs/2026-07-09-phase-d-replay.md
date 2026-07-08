# Phase D milestone 1 — the replay renderer

## The law (immutable, forever)

The C# simulation is the **single authority** on what happens in a fight. It is NEVER rewritten,
ported, or re-implemented in C++/Blueprint — not one formula. Unreal renders what the sim already
decided. If any step seems to need combat logic in Unreal, STOP: the design is wrong.

## The gate (pre-registered)

One canonical text format — the sim's **projection lines** (`GameEvent.Canonical()`, one line per
event) — with three producers that must agree **byte-for-byte**:

1. the live sim (the projection a `fight` run prints),
2. `replay-verify` re-rendering those lines from an exported replay file,
3. the Unreal renderer logging one line per event it renders, prefixed `REPLAY| ` (Phase 2).

Milestone 1 **passes** when, for the fixture fight AND one v4-corpus fight, `diff(1,2)` and
`diff(2,3)` are both empty. No visual polish counts for anything until then.

### How byte-identity is guaranteed (not hoped for)

The projection line for each event has exactly one home: `GameEvent.Canonical()` in
`src/Spellcraft` (immutable). `replay-verify` does **not** re-implement that format — it
**reconstructs the real `GameEvent` objects** from the file's structured payload and calls the
sim's own `Canonical()`. So `diff(1,2)` is empty iff the payload round-trips faithfully — which is
exactly what the round-trip test asserts. The replay also stores each event's `canonical` string so
Unreal (which cannot run C#) can **echo the sim's own text** verbatim — implementing zero formulas,
per the law — while rendering visuals from the payload. `replay-verify` cross-checks its
reconstructed line against the stored `canonical` and fails loudly on any mismatch (tamper/corruption
detector). Timestamps are **not** part of any projection line, so they never affect the gate; they
exist only so the renderer can play events on sim-time.

## Replay file format (schemaVersion "1")

A single JSON object:

```
{
  "schemaVersion": "1",
  "header": {
    "kitA": "<name>", "kitB": "<name>", "seed": <int>,
    "config": { "hpScale": <f>, "mana": <f>, "amplifyMajor": <f> },
    "durationSeconds": <f>, "winner": "<name|draw>", "endReason": "death|timeout|oom",
    "entities": [ { "id": <int>, "name": "<str>", "maxHp": <f>, "spawn": {"x":<f>,"y":<f>,"z":<f>} }, ... ]
  },
  "events": [
    { "t": <f>, "type": "<EventType>", "canonical": "<Canonical() line>", "payload": { <type-specific> } },
    ...
  ]
}
```

- **`t`** — sim-time (seconds) at which the event was observed, captured at the emitting quantum
  boundary (cast phase = the quantum's start; tick phase = the quantum's end). Playback only.
- **`type`** — the `GameEvent` subclass name (e.g. `DamageDealt`, `Displaced`, `ZoneSpawned`).
- **`canonical`** — the event's `Canonical()` string, captured live at export.
- **`payload`** — every field of the event, enough to reconstruct it exactly. Floats are stored as
  JSON numbers via `double` (float→double→number→double→float round-trips exactly). Enums by name.
  Positions (`Vec3`) are stored **wherever the sim produces them** — including `ZoneSpawned.center`
  and `Displaced.from`/`to`, which the projection line itself does not print but the renderer needs.

Per-type payloads mirror the event records in `src/Spellcraft/CombatModel.cs` (e.g. `Displaced` →
`subject, mode, from{x,y,z}, to{x,y,z}, blocked`; `ZoneSpawned` → `id, center{x,y,z}, shape, size,
duration`; `StatModified` → `target, statKind, statElement, amountPct, duration`).

## Producers in this milestone

- **`fight <A> <B> --seed N [--hp-scale k] [--mana N] [--amplify-major f] --replay-out <file>`** —
  runs the fight exactly as today (the replay recorder is additive and null-safe: with no
  `--replay-out`, the fight is byte-for-byte identical, pinned by the existing golden) and writes the
  replay JSON.
- **`replay-verify <file> [--diff-against-live]`** — re-renders the projection purely from the file
  (reconstruct events → `Canonical()`); prints the lines. `--diff-against-live` re-runs the same
  fight live and diffs the live projection against the file-rendered projection, exiting nonzero on
  any difference or on any reconstructed-vs-stored `canonical` mismatch.

## Demo set (`arena/replays/`)

1. **`frost-ember-seed1.json`** — `fixtures/kits/frost.json` vs `fixtures/kits/ember.json`, seed 1,
   defaults (legacy HP, mana 250).
2. **`warden-vs-duelist-t2-seed1.json`** — `a-defensive-earth-warden-1` vs `a-hit-and-run-wind-duelist-1`,
   seed 1, hpScale 8, amplifyMajor 1.75 (T2; the warden brings a **zone**, the duelist a **displace**).
3. **`shadow-duel-t3-seed1.json`** — `a-shadow-assassin-built-on-setup-and-burst-1` vs `-2`, seed 1,
   hpScale 8, amplifyMajor 1.75 (T3).

## Immutable

`src/Spellcraft/**` (Arena already sees the event stream — the recorder serializes from there, and
touches no combat logic), `prompts/`, `schema/`, the four thresholds, `tests/Spellcraft.Tests/**`.
Experiment docs are append-only.

## Phase boundaries

- **Phase 1 (this repo, offline, C# only):** export + verify + the byte-identity of producers 1 and 2.
- **Phase 2 (Unreal project, MCP session):** the renderer + producer 3's `REPLAY| ` log.
- **Phase 3 (Unreal project):** the gate run — `diff(1,2)` and `diff(2,3)` empty for the fixture and
  one v4 fight. Hard stop between phases.
