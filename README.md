# Inkform

> **A body of ink, swallowing objects to become abilities — sneaking and solving puzzles through the gaps in the scan, escaping to freedom.**

**Inkform** is a 3D, cinematic-atmosphere **stealth-puzzle** game. You control a creature made of ink escaping the laboratory that cultivated it. It can **swallow different objects to gain matching abilities** — swallow a remote to control giant machinery, swallow an anchor to sink underwater — using them to evade periodic scanning threats and solve the puzzles blocking its path.

🌐 **Game intro site:** https://pengzzzccc.github.io/Inkform_3D/
📖 **Full design doc (GDD):** [English](Inkform_3D/Docs/GDD_inkform_3D_Puzzle_EN.md) · [中文](Inkform_3D/Docs/GDD_inkform_3D_Puzzle.md)

---

## Design Pillars

1. **Swallow-to-Empower** — every ability is both a stealth tool and a puzzle key. One object, one solution.
2. **Silent Stealth** — threats are *environmental pressure*, not combat. The evasion rhythm creates tension.
3. **Environmental Storytelling** — no dialogue; the story is told through scenery, light/shadow, and sound (INSIDE / Somerville tone).
4. **One Object, One Thought** — puzzles revolve around *"what should I swallow right now,"* rewarding observation and experimentation.

## Core Loop

```
Find clues ──▶ Evade scanning threats ──▶ Solve puzzle ──▶ (proceed)
```

Evasion and puzzle segments are interleaved into a tension-and-release rhythm. Being touched by scan red-light / searchlight is **instant death**, returning you to the nearest distributed checkpoint — no health bar, no combat.

## Swallow → Ability Roster

| Object | Effect | Stealth use | Puzzle use |
|--------|--------|-------------|------------|
| 🎮 **Remote** | Control giant machinery (cranes, conveyors, gates, light towers) | Turn off / redirect scan-light towers; move giant cover | Move platforms to bridge gaps; align gears & belts |
| ⚓ **Anchor** | Body becomes heavy, can sink | Sink into underwater passages to dodge surface scans | Hold weight switches; sink to low passages |
| 📍 **Teleport Beacon** | Place a marker, teleport back to it anytime | Lure the Sweeper, then teleport back to safety | Cross one-way break-points; operate two distant mechanisms |
| 💡 **Bulb / Battery** | Emit light or discharge into mechanisms | Briefly overload nearby lights to create darkness | Power dead mechanisms; trigger light-sensitive switches |
| 🧲 **Magnet** | Magnetic; attracts metal | Drag a metal plate as movable cover | Pull metal doors; short / activate circuits |
| 🎈 **Balloon / Helium** | Become light, float upward briefly | Float over pressure-sensor zones; rise to high cover | Cross high walls; reach high switches |

The **core three** (Remote / Anchor / Teleport) carry the critical path; the rest each own a self-contained segment. See the [GDD](Inkform_3D/Docs/GDD_inkform_3D_Puzzle_EN.md) for the full single-level walkthrough.

---

## Tech & Repository Layout

- **Engine:** Unity **6000.4.10f1** (Unity 6), Universal Render Pipeline (URP).
- **Repo layout:** this repository root holds project-level files (`README`, `LICENSE`, the [`docs/`](docs/) intro site, CI workflows); the **Unity project itself lives in [`Inkform_3D/`](Inkform_3D/)**.

```
Inkform_3D/                 ← git root
├── README.md
├── LICENSE
├── .gitattributes         ← Git LFS + Unity YAML smart-merge rules
├── docs/                  ← GitHub Pages intro site
├── .github/workflows/     ← Pages deploy workflow
└── Inkform_3D/            ← the Unity project (open THIS in Unity Hub)
    ├── Assets/
    ├── Packages/
    ├── ProjectSettings/
    └── Docs/              ← Game Design Document (GDD)
```

### Getting started

1. **Install Git LFS once per machine** (required — binary assets are stored via LFS):
   ```bash
   git lfs install
   ```
2. **Clone** the repo (LFS files are pulled automatically):
   ```bash
   git clone https://github.com/pengzzzccc/Inkform_3D.git
   ```
3. **Open the project**: in Unity Hub, *Add* the **`Inkform_3D/`** subfolder (not the repo root) and open with Unity **6000.4.10f1**.

### Unity YAML smart-merge (recommended)

Scenes/prefabs are kept as text and merged with Unity's `UnityYAMLMerge` tool. Enable it once locally so merges don't corrupt assets:

```bash
git config merge.unityyamlmerge.driver \
  "'<UnityEditorPath>/Data/Tools/UnityYAMLMerge.exe' merge -p %O %B %A %A"
```

(On Windows the editor path is typically `C:\Program Files\Unity\Hub\Editor\6000.4.10f1\Editor`.)

---

## License

[MIT](LICENSE) © 2026 pengzzzccc
