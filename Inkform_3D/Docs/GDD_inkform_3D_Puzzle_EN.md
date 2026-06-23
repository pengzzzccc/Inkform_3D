# Inkform — 3D Stealth-Puzzle GDD (Vertical Slice / Single-Level Demo)

> Document scope: This file describes Inkform's **single-level vertical-slice demo**, used to validate the core gameplay. The narrative here is a streamlined version, kept small in scope. The full chapter narrative lives in `Project_Prompt/` (the legacy 2D direction — reference only, not the basis for this demo).

## 1. High Concept

Inkform is a **3D, cinematic-atmosphere stealth-puzzle game**. The player controls a creature made of ink, escaping from the laboratory that cultivated it. It can **swallow different objects to gain corresponding abilities** (swallow a remote to control giant machinery, swallow an anchor to sink underwater…), using them to evade periodic scanning threats and solve the puzzles blocking its path.

**One line**: A body of ink, swallowing objects to become abilities, sneaking and solving puzzles through the gaps in the scan — escaping to freedom.

### Design Pillars
1. **Swallow-to-Empower**: Every ability is both a stealth tool and a puzzle key — one object, one solution.
2. **Silent Stealth**: Threats are "environmental pressure," not combat — evasion rhythm creates tension.
3. **Environmental Storytelling**: No dialogue; the story is told through scenery, light/shadow, and sound (INSIDE / Somerville tone).
4. **One Object, One Thought**: Puzzles revolve around "what should I swallow right now," encouraging observation and experimentation.

## 2. Story & Tone (Streamlined)

- **Setting**: An experimental being made of ink awakens deep within a laboratory and develops the will to escape. The lab is still operational; its automated scanning and "sweeper" systems treat any anomaly as a target to be cleansed.
- **Demo Narrative Arc**: Awakening (learn movement & swallowing) → traverse the scan-locked zones → solve the puzzle leading to the exit → at the exit, gaze toward the light beyond the lab.
- **Tone**: Oppressive, lonely, curious all at once; cold palette, strong light/shadow; no dialogue — emotion carried by rhythm and sound.
- **Protagonist Presentation**: A malleable ink-textured body. After swallowing, it transforms by being "stuffed full / bulged out by the object" (referencing the cute, clumsy charm of *Kirby and the Forgotten Land*'s Mouthful Mode), contrasting against the cold environment.

## 3. Core Gameplay Loop

```
Find clues ──▶ Evade scanning threats ──▶ Solve puzzle ──▶ (proceed to next segment)
```

- **Evasion segment**: When the large "Sweeper" entity periodically passes by / fixed scanning devices scan on a rhythm, the player must hide in cover or evade with an ability. Touched by the light = instant death. High pressure, silent.
- **Puzzle segment**: Once the threat passes or is evaded, the player explores the scene, finds clues, and picks the right swallow ability to break the obstacle. Calm, thoughtful.
- The two segment types are **interleaved**, forming a tension-and-release rhythm.

## 4. Controls & Camera

### Camera
- **Cinematic camera**: Primarily fixed / rails cameras, switching by scene to emphasize composition and atmosphere (referencing INSIDE's 2.5D, Somerville's fixed angles).
- **Space**: 2.5D depth — the player mainly moves within a single plane, with local front-to-back depth passages.
- The camera serves "reading the scan-light direction and safe cover" — readability is guaranteed during key threat segments.

### Base Controls (always available)
| Input | Action |
|-------|--------|
| Move | Move in all directions (base mobility, retained no matter what is swallowed) |
| Interact | Trigger mechanisms, read clues, swallow/spit out objects |
| Use Ability | Trigger the ability granted by the currently swallowed object |
| Take Cover | Press against / tuck into scene cover to evade scans |

- **Swallow rule**: The player can swallow only one type of object at a time; you must swallow at a swallow point, and can spit out the current object before swallowing another. Switching has a spatial cost (you must return to the corresponding object).

## 5. Swallow–Ability System (Core System)

### General Rules
- Hold only one ability at a time; swallow = gain ability and transform, spit = return to base ink form.
- Objects are placed at fixed positions in the scene → this creates an "ability reachability" spatial constraint, a key lever for level and puzzle design.
- Each transformation has a clear visual/silhouette difference, so players and the camera can read the current state.

### Ability Roster (6)

**Core Three (span the critical path)**

| # | Swallowed Object | Effect | Stealth Use | Puzzle Use | Limitation |
|---|------------------|--------|-------------|------------|------------|
| 1 | **Remote** | Remotely control giant machinery in the scene (cranes, conveyors, gates, searchlight towers) | Switch off / turn scan-light towers, move giant cover to block sightlines | Move platforms to bridge gaps, align gears / convey objects on belts | Must stay still while controlling; easily exposed during it |
| 2 | **Anchor** | Body becomes heavy, can sink | Sink into underwater passages to dodge surface scans | Hold down weight switches, resist airflow/water currents, sink to low passages | Slow movement while heavy; cannot jump / float up |
| 3 | **Teleport Beacon** | Place a marker, then teleport back to it at any time | Deliberately expose yourself to lure the Sweeper to one side, then teleport back to a safe marker | Cross one-way break-points, operate mechanisms in two separate locations | Only one marker at a time; teleport has a cooldown |

**Segment-Specific (introduced in specific segments to enrich puzzles)**

| # | Swallowed Object | Effect | Stealth Use | Puzzle Use | Limitation |
|---|------------------|--------|-------------|------------|------------|
| 4 | **Bulb / Battery** | Emit light yourself, or discharge into mechanisms | Light up dark segments (exposure risk) / briefly overload nearby lights to create darkness for evading scans | Power unpowered mechanisms, activate light-sensitive switches | Emitting light draws the scanning system's attention; limited charge/duration |
| 5 | **Magnet / Electromagnet** | Magnetic, attracts metal | Attach to a metal plate to use as **movable cover** blocking scan-light | Pull metal doors, grab metal keys, short / activate circuits | Only one piece at a time; slow movement when loaded |
| 6 | **Balloon / Helium** | Become light, can float upward briefly | Float over ground pressure-sensor zones, rise to high cover | Cross high walls, reach high switches | Limited float time, easily blown by airflow, delayed landing |

> **Scope note**: In the single-level demo, Remote/Anchor/Teleport carry the critical path and climax; Bulb·Battery/Magnet/Balloon each own a single self-contained segment, to avoid cramming all 6 tutorials into one level. If production is tight, prioritize the core three + any one segment ability as a minimum playable version.

## 6. Threat & Stealth System

### Death Rule
- **Touched by scan red-light / searchlight = instant death**, immediately returning to the nearest distributed checkpoint.
- No health bar, no combat HP — the threat is binary (safe / dead), reinforcing stealth tension.

### Hybrid Threat Sources
1. **Fixed Scanning Devices**: Rotating searchlights, sweeping laser grids, fixed cameras. These have predictable scan cycles; the player must time their passage or evade with an ability.
2. **Large "Sweeper" Entity**: A colossus that periodically passes through the area (referencing the Journey tunnel dragons, the Somerville alien ship).
   - Behavior loop: **approach from afar → enter and scan the area → leave**.
   - During scanning the player must hide in cover / evade with abilities (magnet cover, anchor dive, teleport lure, balloon to high blind spots).
   - The signature high-pressure source and climax segment of the demo.
3. **(Optional) Mobile Patrollers**: Drone / guard types. **Red light in their facing direction = instant death**; their **actual perception range is larger than their red-light range** (red light is the death check, perception triggers alertness / turning).

### Evasion Methods
- **Scene cover**: Crates, pillars, shadow zones, tuck-in pipes.
- **Ability evasion**: Drag a metal plate with the magnet as movable cover; dive underwater with the anchor; rise high with the balloon; lure away then teleport back with the beacon.
- **Rhythm evasion**: Observe scan cycles, move in the gaps.

### Checkpoints
- **Distributed checkpoints** (à la INSIDE): Multiple implicit checkpoints within the level; death returns you to the nearest one — rhythm-friendly and encourages trial and error.

## 7. Puzzle Design

### Puzzle Types & Ability Hooks
| Puzzle Type | Ability | Example |
|-------------|---------|---------|
| Machinery control | Remote | Control a crane to bridge containers; have a conveyor carry a key into reach |
| Weight / water level | Anchor | Sink underwater to open a valve; hold a switch down with weight to keep a door open |
| Spatial displacement | Teleport Beacon | Trigger a mechanism at A, teleport back to B to slip through a one-way door |
| Circuit / power | Bulb·Battery, Magnet | Power an unpowered door; short two metal contacts to close a circuit |
| Light-sensitive / darkness | Bulb·Battery | Illuminate a light-sensitive switch; create darkness to disable a light sensor scan |
| Height / reach | Balloon | Float up high to press a separated switch |

### Demo Core Puzzles (3 suggested)
1. **Sluice puzzle (Anchor)**: A flooded passage blocks the way; swallow the anchor, sink to the bottom, open the drain valve; once the water level drops, a new path opens.
2. **Crane-bridge puzzle (Remote + fixed scan)**: Use the remote to move containers with a crane to form a bridge, while dodging a rotating searchlight — you stay still while controlling, so you must time the light's scan gap.
3. **Break-point displacement puzzle (Teleport Beacon + Sweeper climax)**: In a hall the Sweeper periodically sweeps, first place a marker in a safe blind spot, then dash to the far end during a gap to trigger the exit mechanism, teleporting back to the marker just before the scan nears.

## 8. Single-Level Walkthrough (Demo)

| Segment | Content | Ability Introduced | Main Threat | Purpose |
|---------|---------|--------------------|-------------|---------|
| A. Awakening | Learn movement, swallow/spit, take cover | (base) | None / light | Tutorial, establish tone |
| B. Scan Corridor | Time fixed searchlights to pass through | — | Fixed scanning devices | Teach "observe cycle, use cover" |
| C. Water Area | Sluice puzzle | **Anchor** | Surface scan | Teach the swallow-puzzle loop |
| D. Machinery Hall | Crane-bridge puzzle; magnet movable-cover bit | **Remote**, **Magnet** | Fixed scan + rotating tower | Compound ability use |
| E. Dark Power Segment | Power the pre-exit door / light-sensitive switch | **Bulb·Battery** | Localized scan in darkness | Risk-reward decision |
| F. Sweeper Climax | Break-point displacement puzzle in the hall | **Teleport Beacon** (+ Balloon for high blind spots) | **Large Sweeper passes** | High-pressure synthesis |
| G. Exit | Solve the final mechanism, door opens | Combined | Wrap-up | Release tension |
| H. Closing Shot | The ink creature walks out, gazing toward the light beyond | — | — | Emotional close, leave a hook |

> Order is adjustable; the Balloon bit can merge into F or stand alone (e.g. reaching a high teleport object).

## 9. Level Element Checklist (Production List)
- **Swallowable object points** ×6 (Remote, Anchor, Teleport Beacon, Bulb/Battery, Magnet, Balloon) — fixed respawn positions.
- **Cover**: Static cover, tuck-in pipes, shadow zones.
- **Threats**: Fixed searchlights / laser grids / cameras, the Sweeper entity, (optional) patrol drones.
- **Mechanisms**: Sluice valves, cranes/conveyors, weight plates, power points / light-sensitive switches, one-way doors, exit door.
- **Distributed checkpoints** ×N.
- **Clue objects**: Environmental hints (signage, marks, light guidance) — no text or minimal text.

## 10. UI / HUD

- **Minimal, near zero-HUD** (à la INSIDE).
- Keep only: **current ability icon** (small corner icon / or expressed purely by transformation silhouette), **interaction prompt** (faint highlight when near an interactable), **death fade-in/out**.
- No health bar, no minimap, no quest text; guidance comes from level design and light/shadow.

## 11. Art & Audio Direction

### Art
- Low-saturation cold palette, strong light/shadow contrast, silhouette-driven composition; scan-light / searchlight are among the few highlights on screen, naturally guiding attention.
- Character: Semi-transparent ink texture, malleable; when transforming, the body is "bulged out" by the object while retaining ink-flow.

### Audio
- Ambient sound dominant, strong sense of space; almost no score or a minimal score.
- The **Sweeper** has a signature audio cue (low-frequency approach sound) acting as the player's "auditory radar."
- The moment of being spotted: sharp, piercing feedback + camera/screen impact.

## 12. Tech & Scope (Unity)

- **Engine**: Unity (following the project's existing version).
- **Architecture reuse**: The existing project (though 2D) already has a set of reusable **architectural patterns**; when going 3D, follow their ideas rather than rewriting:
  - Event bus `S_GameEvent` (decouples threat/mechanism/checkpoint broadcasts).
  - `ManagerRoot` cross-scene persistence, input-lock pattern.
  - Level Sections, level config `S_LevelConfig`.
  - NPC Sensors / patrol-alert state machine (for the 3D adaptation of patrollers and scan checks).
  - Checkpoint / Progression system.
  > Note: The above implementations currently target 2D; the cost of 3D adaptation must be assessed. This demo is treated as a technical validation of the new direction.
- **Demo Scope / Milestones (suggested)**:
  1. M1: Base movement + swallow/spit + one fixed scanning threat + death/checkpoint (segments A, B).
  2. M2: Core three abilities (Remote/Anchor/Teleport) + their puzzles (segments C, D, F).
  3. M3: Segment-specific abilities (Bulb·Battery/Magnet/Balloon) + art & audio atmosphere + closing shot (segments E, G, H).

## 13. TBD
- Exact ability values (movement speed, teleport cooldown, balloon float duration, battery charge).
- The Sweeper's precise scan cycle and coverage.
- Whether to include mobile patrollers (optional threat).
- Final art style and concrete transformation presentation.
- Exact level scale and camera scripting.
