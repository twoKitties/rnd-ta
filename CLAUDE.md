# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

`rnd-ta` is a co-op stealth game: a group of 2–4 players, aliens flying a saucer, land at a house and try to steal the pets living there. This repo **is** the Unity project (no sub-projects) — so this file is both the convention doc and the rulebook. Read it before touching code, scenes, or assets.

## The game

- **Hub** — the flying saucer the players start from and return to.
- **Level** — the house they raid.
- **Pets** — three of them, distinguished by size: **Dog** (big), **Kitty** (medium), **Parrot** (small). They try to run away and hide from the players.
- **Old Man** — an angry old man with a shotgun who lives in the house. If he sees a player, that player dies instantly.
- **Win condition** — carry every animal into the saucer and fly away.

Use these names in code and asset names. `Dog` / `Kitty` / `Parrot` / `Old Man` are already the prefab names in `Assets/_Game/Content/Level/`; the hub is `UFO.prefab` and the level is the scene `Assets/_Game/Content/Scenes/Level.unity`.

**Design docs (Russian).** `DD.md` holds the concept — treat it as the authoritative statement of the design. `MECHANICS.md` is the buildable breakdown: tunable values, per-mechanic authority for the coming multiplayer pass, and Must/Should/Defer tiers. Read `MECHANICS.md` before writing gameplay code, and take numbers from its tunables table rather than inventing them. `.claude/agents/gamemechanics-validator.md` is a read-only reviewer agent that audits mechanics docs against the concept, the 5-hour budget, and this project's Unity constraints.

**Current state (2026-08-03):** greybox stage, first gameplay code landed. There is one level (`Level.unity`), built out of asset-store packs. The house is `HouseOneFloor.prefab` (single storey; `HouseTwoFloor.prefab` also exists but is not in the scene) and it ships with the pack's modular colliders. **NavMesh is baked**: one `NavMeshSurface` on the scene object `Navigation` (`Collect Objects: All`, `Include Layers: WalkingArea + BlockedArea`, `Use Geometry: Physics Colliders`), data in `Assets/_Game/Content/Scenes/Level/NavMesh-Navigation.asset`. Spawn points live under the scene root `Spawns` (`PlayerSpawn` ×4 in the yard, `PetSpawn` ×8 and `OldmanSpawn` ×3 in the house). The house is furnished from `nappin/HouseInteriorPack` under the scene root `Interior`, grouped per room — the floor plan is 8 spaces on a 2.5 m grid (bathroom, entrance hall, living room, bedroom, kitchen, pantry, kids room, study) plus the corridor spine. Patrol points for Old Man live under the scene root `PatrolPoints` (9 of them). `Player.prefab` carries `PlayerController`, `PlayerInteractor`, `PlayerHands` and its own camera; the 8 door leaves inside `HouseOneFloor.prefab` carry `Door`, so doors open and shut (`Interact`, 90° away from whoever used them — see `MECHANICS.md` 3.8). The rest of the prefabs still carry no scripts, and the pets and Old Man are still parked in a row above the roof rather than on their spawns. Nothing else about the runtime architecture is settled — do not assume one exists, and do not invent one silently. In particular **no netcode package is installed** (`com.unity.multiplayer.center` is only the recommender tool), so the multiplayer stack is an open decision: ask before pulling one in.

## Layout

```
rnd-ta/
├── CLAUDE.md                   ← this file
├── Assets/
│   ├── _Game/                  ← everything WE author
│   │   ├── Code/               ← gameplay scripts, one folder per system
│   │   │   ├── Doors/          ← Door.cs
│   │   │   └── Player/         ← PlayerController.cs, PlayerInteractor.cs, PlayerHands.cs
│   │   └── Content/
│   │       ├── Scenes/         ← Level.unity
│   │       │   └── Level/      ← baked NavMeshData for that scene
│   │       └── Level/          ← Player.prefab, materials, volume profile
│   │           ├── Characters/ ← Old Man, Dog, Kitty, Parrot
│   │           ├── Spawns/     ← PlayerSpawn, PetSpawn, OldmanSpawn markers
│   │           └── Surroundings/ ← HouseOneFloor, HouseTwoFloor, Environment, Lawn, UFO
│   ├── Settings/               ← URP pipeline + renderer assets (PC_ and Mobile_ variants), volume profiles
│   ├── InputSystem_Actions.inputactions
│   └── <vendor folders>/       ← third-party packs, see below
├── Packages/manifest.json      ← installed packages
└── ProjectSettings/            ← Unity project config
```

- **`Assets/_Game/`, `Assets/Settings/` and `Assets/InputSystem_Actions.inputactions` are ours; every other *folder* directly under `Assets/` is an imported pack** — run `ls Assets/` for the current set, it grows with almost every art commit. Treat pack folders as read-only: don't edit or reorganize them, because a reimport wipes the changes. To adapt a pack asset, make a prefab variant or a copy inside `Assets/_Game/`.
- New gameplay code goes in `Assets/_Game/Code/`. New level content goes in `Assets/_Game/Content/`.

## Project facts

Pointers, so they can't go stale — read the file rather than trusting a number quoted in prose:

| Thing | Where it actually lives |
| --- | --- |
| Unity version | `ProjectSettings/ProjectVersion.txt` |
| Installed packages | `Packages/manifest.json` |
| Render pipeline settings | `Assets/Settings/` (`PC_RPAsset`/`PC_Renderer`, `Mobile_RPAsset`/`Mobile_Renderer`) |
| Input bindings | `Assets/InputSystem_Actions.inputactions` |
| Tags / layers | `ProjectSettings/TagManager.asset` |

Worth knowing:

- **URP.** Anything shader/material/lighting related must be URP-compatible. There are separate PC and Mobile pipeline assets, both bound in `ProjectSettings/QualitySettings.asset` — a change to rendering usually needs to be made in both. Note that `com.unity.postprocessing` (PPv2) is in the manifest but is Built-in-RP only and must **not** be used: URP post-processing goes through Volume profiles.
  **Grading is per-scene, not global:** the `Level` scene's Global Volume uses its own `Assets/_Game/Content/Level/NightVolume-Profile.asset`. `Assets/Settings/SampleSceneProfile.asset` is still the pipeline default in both RP assets — editing it changes the project-wide default, *not* the level. Edit the scene's own profile to grade the level.
- **Input System package only** (the legacy `Input` API is disabled via `activeInputHandler` in `ProjectSettings/ProjectSettings.asset`, so `Input.GetKey` etc. will not work). Bindings are authored in the `.inputactions` asset. `generateWrapperCode` is **on**, so the wrapper class `InputSystem_Actions` exists at `Assets/InputSystem_Actions.cs` (map `Player`: `Move`, `Look`, `Sprint`, `Crouch`, `Jump`, `Interact`, `Attack`, `Previous`, `Next`) — instantiate it directly, as `PlayerController` does, rather than adding `PlayerInput` components. That generated `.cs` is machine-owned: never hand-edit it, change the asset and let it regenerate.
  **Pointer `Look` delta is already a per-frame increment — never multiply it by `Time.deltaTime`**, or look sensitivity silently becomes frame-rate dependent.
- **`nappin/HouseInteriorPack` is not authored at real-world scale, and not uniformly so** — `Fridge` comes in 3.69 m tall, `Wardrobe` 3.78 m, `SingleBed` 2.81 m (taller than the 2.5 m walls), while `BedsideTable` is only 0.39 m and needs scaling *up*. There is no single pack-wide factor: each item is scaled individually against a real reference height, with Old Man (1.79 m) as the yardstick. Several prefabs also ship with doors modelled *open* (`Fridge`, `Wardrobe`, `KitchenSink`, `Storage1`, `Drawer2`), which inflates their footprint and eats navmesh — close them before placing.
- **`Player.prefab` has root scale `0.1`** (the alien is 0.5 m tall), so Unity's defaults, which assume a 2 m human, are wrong here — and components mix local and world units. `CharacterController.height`/`radius`/`center` and any `localPosition` scale with the transform; `stepOffset` and `skinWidth` are in world metres and do not. Before adding a component to a scaled prefab, check which space each field is in — the table in `MECHANICS.md` section 2 records the ones already resolved.
- **AI Navigation (`com.unity.ai.navigation`) is installed and in use** — the level's NavMesh is baked through it (see *Current state*). Use it for pet flee/hide and Old Man movement rather than hand-rolling pathfinding.
  **The agent type is a project-wide setting, not a surface setting.** A `NavMeshSurface` only references it by `agentTypeID`, so a bad agent type silently breaks every bake and reads as "the navmesh ignores walls". Values live in `ProjectSettings/NavMeshAreas.asset`, edited via `Window → AI → Navigation → Agents`; there is exactly one type, `Humanoid`, and its radius is sized to the house's narrowest doorway. Read the file before changing anything navigation-related.
  **Bakes are driven by `Include Layers`, not by static flags** — an object outside the mask does not exist for the bake, so it neither blocks nor creates walkable floor. Layer assignment for the house lives in `HouseOneFloor.prefab`: floor on `WalkingArea`, all structural pieces on `BlockedArea`, the 8 door leaves on `Door` (deliberately outside the mask so the navmesh runs through every doorway, while still blocking the player physically). Because of that, a shut door stops the player but **not** a `NavMeshAgent` — AI that must respect doors reads `Door.IsOpen` (`MECHANICS.md` 4.6), it cannot rely on the navmesh.
  Removing a `NavMeshSurface` component **deletes its baked `NavMeshData` asset** — the package does this in `NavMeshAssetManager`, without a prompt.
- **Almost the whole house is marked Static — anything that has to move must be cleared first.** 901 of `HouseOneFloor`'s 949 objects carry `Everything` static flags. A **Batching Static** renderer is merged into a combined mesh when Play mode starts and then ignores its transform: the object still moves for physics, but the visible geometry stays put. That is how `Corner_3B/Exterior_Door` behaved on 2026-08-03 — measured in Play mode, its collider swung `0.776 m` while its renderer moved `0.000 m`, so the door was open but looked shut. Clearing the flags on the leaf and its five children fixed it. Before animating anything inside the house, check `GameObjectUtility.GetStaticEditorFlags`.
- **Git LFS is active** (`.gitattributes` routes models, textures, audio and other binaries through it). Keep new binary assets under the existing LFS rules rather than adding patterns ad hoc.
- **Unity YAML merges are probably *not* protected.** `.gitattributes` asks for `merge=unityyamlmerge`, but the driver itself is a local git-config setting that isn't versioned — and it was absent in every scope on this machine as of 2026-08-01. Check with `git config --get merge.unityyamlmerge.driver`; empty means git falls back to a plain text merge on `.unity`/`.prefab`/`.asset`, which corrupts them. Avoid situations that merge scene/prefab changes; if one is unavoidable, say so and let the user resolve it in the editor.

## Working rules

These are load-bearing. Keep them in force even when the task is small.

- **Do exactly what's asked — then stop and report.** Not a partial version, not a souped-up version with extra "fixes", restarts, or refactors that weren't requested. If ambiguous, ask instead of assuming.
- **Never guess or speculate.** Don't theorize about what the code does or why something breaks — open the file and verify. For tuned systems, where the behaviour comes from the numbers rather than the structure, this is the whole job: read the actual values and the comments around them before changing a tunable, and change one at a time.
- **Never lie or invent.** If you don't know something, say so. When your tools or knowledge aren't enough to finish the task, stop and tell the user exactly what you're missing and what you need from them (a file, a credential, a decision, editor access). If the gap is factual or external (an API, a Unity/C# detail, a package behaviour), use web search to find a real source rather than answering from memory — and cite it.
- **Irreversible / external actions require explicit approval.** `git push`, publishing, deleting or overwriting files you didn't create, force operations — explain what you're about to do and wait for a "yes". Reading is always fine.
- **Data safety for assets.** Never delete or rename an asset without its `.meta` file (renaming the pair together preserves the GUID; losing the `.meta` breaks every reference to it). Don't hand-edit scene/prefab YAML unless you know the format — the scene and the `Level/` prefabs are hand-built in the editor and are the least recoverable thing in the repo. Don't edit anything inside a vendor pack folder. Explain and get approval before any bulk asset operation.
- **Commit after each logical unit of work.** Stage the relevant files by name — including the `.meta` files — and commit once a feature/fix/refactor is complete; split unrelated changes into separate commits. Don't commit mid-flight or mix unrelated edits.
- **Self-check before reporting "done".** At minimum: the change compiles against the real Unity/C# API; Unity lifecycle patterns are preserved — including the fake-null gotcha, where a destroyed `UnityEngine.Object` compares `== null` but is not a real null, so `?.` and `??` lie about it (use plain `== null` / `if (obj)` on Unity objects); and the change actually does what was asked. This editor can't be run from here — runtime behaviour has to be verified by the user in Play mode, so say that rather than claiming it "works".
- **Keep this doc current, in the same commit.** After any change to architecture, project structure, conventions, or the game design above, update `CLAUDE.md` alongside the code. It's the source of truth for the next conversation.
- **Write the minimum that works.** Before adding code, walk this ladder and stop at the first hit:
  1. Does this need to exist? → if no, skip it (YAGNI).
  2. Already in this codebase? → reuse it, don't rewrite.
  3. Does the C#/.NET standard library do it? → use it.
  4. Native Unity feature (engine API, package, editor tooling)? → use it.
  5. An already-installed package (`Packages/manifest.json`)? → use it.
  6. One line? → one line.
  7. Only then: write the minimum that works.

  Justify any new dependency, and modify as little existing code as possible.

## Doc hygiene (for this file)

- **Pointers, not values.** Don't restate anything that drifts — versions, package lists, tunable numbers. Point at the file that owns it.
- **Absolute dates, not relative.** "Greybox stage as of 2026-08-01", never "currently" or "recently".
- **Keep it thin.** This file is read every turn. Inline only the cross-cutting rules, the design vocabulary, and the routing facts above; if a system grows big enough to need real documentation, give it its own doc next to the code and link it from here instead of expanding this file.
