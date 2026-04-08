# VR Molecular Chemistry Lab

A VR application built in Unity 6 for Meta Quest where students can physically combine atomic elements to form valid molecules using hand tracking.
## THE PROJECT WAS MADE WITHOUT THE HELP OF ANY VR HARDWARE EQUIPMENT***

---

## Tech Stack

- Unity 6 (URP)
- XR Interaction Toolkit 3.x
- OpenXR Plugin + Meta Quest Support
- XR Hands Package
- TextMeshPro
- DOTween

---

## Setup Instructions

### Requirements
- Unity 6 (latest stable) with Universal Render Pipeline
- Android Build Support module installed in Unity Hub
- Meta Quest 2 / 3 / Pro headset (or Meta XR Simulator for PC testing)

### Steps

1. Clone the repository
   ```
   git clone https://github.com/eudiko/VrMolecule.git
   ```

2. Open the project in Unity 6 via Unity Hub

3. Install required packages via Window → Package Manager → Unity Registry
   - XR Interaction Toolkit 3.x (import Starter Assets sample)
   - OpenXR Plugin
   - XR Hands

4. Configure XR settings
   - Edit → Project Settings → XR Plug-in Management → Android tab
   - Tick OpenXR
   - OpenXR → Feature Groups → tick Meta Quest Support and Hand Tracking Subsystem

5. Configure build settings
   - File → Build Settings → switch platform to Android
   - Player Settings → Scripting Backend: IL2CPP
   - Target Architecture: ARM64 only
   - Minimum API Level: 29

6. Open and play the scene
   - Assets → Scenes → ChemLab.unity


---

## How to Play

- Pick up atom tokens (H, O, C, N) from the lab table using hand pinch gesture
- Bring atoms close together — a bond check triggers automatically on release
- Valid combinations form a 3D molecule with a sound and visual effect
- Check the Library panel (floating left of the table) to track discovered molecules
- The Inspector panel shows the molecule name, formula, and bond type briefly after each formation
- Use the Reset button on the table edge to break all molecules and reuse atoms

---

## Molecules Implemented

| Molecule | Formula | Atoms | Bond |
|---|---|---|---|
| Water | H₂O | 2H + 1O | Covalent |
| Hydrogen Gas | H₂ | 2H | Single Covalent |
| Oxygen Gas | O₂ | 2O | Double Covalent |
| Nitrogen Gas | N₂ | 2N | Triple Covalent |
| Ammonia | NH₃ | 1N + 3H | Covalent |
| Carbon Dioxide | CO₂ | 1C + 2O | Double Covalent |
| Methane | CH₄ | 1C + 4H | Covalent |

---

## Project Architecture

```
Assets/
  Scripts/
    Data/         MoleculeRecipe, MoleculeDatabase (ScriptableObject)
    Core/         AtomController, BondManager
    UI/           UIManager
    Audio/        AudioManager
  Prefabs/
    Atoms/        H, O, C, N prefabs
    Molecules/    One prefab per valid molecule
    UI/           LibraryEntry, panel prefabs
  ScriptableObjects/
    MoleculeDB.asset
```

**MoleculeDatabase** — ScriptableObject holding all valid recipes. Lookup is O(1) via a string key dictionary built at runtime.

**AtomController** — Attached to every atom. Handles XR grab events and proximity detection via OverlapSphere on release.

**BondManager** — Singleton. Receives atom groups from AtomController, validates against MoleculeDatabase, spawns molecule prefab, fires events.

**UIManager** — Listens to BondManager events. Updates world-space Library panel, Inspector popup, and Notification banner.

**AudioManager** — Centralised audio singleton. Plays grab, bond success, and reset sounds.

---

## AI Tools Used

1. **Claude (Anthropic)** — Used for full C# architecture design and code generation across all five core scripts (AtomController, BondManager, UIManager, AudioManager, MoleculeDatabase). Also used to debug XRI interaction layer configuration, fix NullReferenceExceptions in UIManager, and plan the ScriptableObject data layer structure.

2. **GitHub Copilot** — Used inside the Unity editor for inline code completion, auto-completing switch/case blocks in BondManager.TryBond, generating XML doc comments, and boilerplate event subscription patterns.

---

## Testing Without a Headset

- Window → XR → XR Device Simulator
- Right-click + drag to look around
- Left-click near an atom to simulate grab
- Release to trigger bond detection
- Full hand tracking simulation requires the Meta XR Simulator app or a physical Quest headset

---

## Known Limitations

- Hand tracking requires adequate room lighting on the Quest headset
- Molecule prefabs use simple sphere + cylinder geometry — not chemically accurate 3D models
- Bond formation triggers on atom proximity after release, not on a specific gesture

---

*Submitted for XR Developer Assessment — VR Molecular Chemistry Lab*
