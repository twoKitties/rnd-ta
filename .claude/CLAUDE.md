# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

`rnd-ta` is a co-op stealth game: a group of 2–4 players, aliens flying a saucer, land at a house and try to steal the pets living there. This repo **is** the Unity project (no sub-projects) — so this file is both the convention doc and the rulebook. Read it before touching code, scenes, or assets.

## The Game

The description of game lies in **GAME.md** file.

## Layout

```
rnd-ta/
├── CLAUDE.md                   ← this file
├── Assets/
│   ├── _Game/                  ← everything WE author
│   │   ├── Code/               ← gameplay scripts, one folder per system
│   │   │   ├── LevelBootstrapper.cs ← the level's entry point
│   │   │   ├── App/            ← Bootstrapper.cs (the app's entry point), RaidSession.cs, LobbyRoster.cs, ServerSimulated.cs
│   │   │   ├── AI/             ← Sight.cs, Hearing.cs, DoorGate.cs, SensedPlayer.cs — shared by every brain
│   │   │   ├── Audio/          ← FootstepAudio.cs, FootstepBank.cs, FootstepSurface.cs
│   │   │   ├── Doors/          ← Door.cs, DoorState.cs
│   │   │   ├── Level/          ← BeamZone.cs, LevelGoal.cs, UfoDrift.cs
│   │   │   ├── Noise/          ← NoiseEmitter.cs
│   │   │   ├── OldMan/         ← OldManBrain.cs, ShotFlash.cs, RifleRig.cs, ShotPellet.cs
│   │   │   ├── Pets/           ← Pet.cs, PetBrain.cs, PetVoice.cs
│   │   │   ├── Player/         ← PlayerController.cs, PlayerInteractor.cs, PlayerHands.cs, PlayerAnimator.cs, PlayerNoise.cs, PlayerLife.cs, FirstPersonBody.cs, LocalAvatar.cs, SpectatorCamera.cs, PlayerMotion.cs
│   │   │   ├── Spawning/       ← SpawnPoint.cs, ActorSpawner.cs
│   │   │   └── UI/             ← InteractionPromptUI.cs, LevelStatusUI.cs, EndScreenUI.cs, LobbyUI.cs, PlayerLobbyUI.cs, LoadingScreen.cs
│   │   └── Content/
│   │       ├── Shared/         ← Session.prefab, LobbyRoster.prefab, RaidState.prefab (spawned, not scene objects) + LoadingScreen.prefab and its images
│   │       ├── Lobby/          ← PlayerLobby.prefab (one roster row) and its image
│   │       ├── Scenes/         ← Loading.unity, Lobby.unity, Hub.unity, Level.unity (build order)
│   │       │   └── Level/      ← baked NavMeshData for that scene
│   │       └── Level/          ← folders only, nothing loose in the root
│   │           ├── Anchors/    ← PlayerSpawn, PetSpawn, OldmanSpawn, PatrolPoint markers
│   │           ├── Animations/ ← retargeted clips, OldMan-Controller
│   │           ├── Characters/ ← Old Man, Dog, Kitty, Parrot, ShotPellet
│   │           ├── Moon/       ← moon textures and material
│   │           ├── Player/     ← Player.prefab, Player.controller, Player-Avatar, UpperBody-Mask
│   │           ├── Sounds/     ← clips + Footsteps-Bank
│   │           └── Surroundings/ ← HouseOneFloor, HouseTwoFloor, Environment, Lawn, UFO, materials, NightVolume-Profile
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
  **Grading is per-scene, not global:** the `Level` scene's Global Volume uses its own `Assets/_Game/Content/Level/Surroundings/NightVolume-Profile.asset`. `Assets/Settings/SampleSceneProfile.asset` is still the pipeline default in both RP assets — editing it changes the project-wide default, *not* the level. Edit the scene's own profile to grade the level.
- **Input System package only** (the legacy `Input` API is disabled via `activeInputHandler` in `ProjectSettings/ProjectSettings.asset`, so `Input.GetKey` etc. will not work). Bindings are authored in the `.inputactions` asset. `generateWrapperCode` is **on**, so the wrapper class `InputSystem_Actions` exists at `Assets/InputSystem_Actions.cs` (map `Player`: `Move`, `Look`, `Sprint`, `Crouch`, `Jump`, `Interact`, `Attack`, `Previous`, `Next`) — instantiate it directly, as `PlayerController` does, rather than adding `PlayerInput` components. That generated `.cs` is machine-owned: never hand-edit it, change the asset and let it regenerate.
  **Pointer `Look` delta is already a per-frame increment — never multiply it by `Time.deltaTime`**, or look sensitivity silently becomes frame-rate dependent. The mirror of that rule holds everywhere else, UI included: anything that moves per frame is multiplied by `Time.unscaledDeltaTime` — `LoadingScreen` had a fade and a spinner counted in per-frame steps until 2026-08-05, and both drifted with the frame rate exactly where it is least stable. When such a fix changes a serialized field's **unit**, retune the asset in the same commit or the old number silently means something else.
- **`nappin/HouseInteriorPack` is not authored at real-world scale, and not uniformly so** — `Fridge` comes in 3.69 m tall, `Wardrobe` 3.78 m, `SingleBed` 2.81 m (taller than the 2.5 m walls), while `BedsideTable` is only 0.39 m and needs scaling *up*. There is no single pack-wide factor: each item is scaled individually against a real reference height, with Old Man (1.79 m) as the yardstick. Several prefabs also ship with doors modelled *open* (`Fridge`, `Wardrobe`, `KitchenSink`, `Storage1`, `Drawer2`), which inflates their footprint and eats navmesh — close them before placing. **Every item also ships as one `BoxCollider` running from the floor to its very top**, so nothing in this house has a space under it until somebody cuts one: on 2026-08-11 the coffee table, the two dining chairs, the study chair and the dining table inside `HouseOneFloor.prefab` had that box shrunk to the tabletop (the seat, on chairs) with the legs and backrests added back as **extra `BoxCollider`s on the same GameObject** — no new objects, and the pack itself is untouched. The gaps that opened are measured in `MECHANICS.md` 3.1; the `Desk` was left alone because its drawer pedestal is full height.
- **`Player.prefab` has root scale `0.1`** (the alien is 0.5 m tall), so Unity's defaults, which assume a 2 m human, are wrong here — and components mix local and world units. `CharacterController.height`/`radius`/`center` and any `localPosition` scale with the transform; `stepOffset` and `skinWidth` are in world metres and do not. Before adding a component to a scaled prefab, check which space each field is in — the table in `MECHANICS.md` section 2 records the ones already resolved. **Crouch is a real size since 2026-08-11**: `PlayerController` shrinks the capsule from `0.5 m` to `0.26 m` and lowers the camera with it, refuses to stand up when `OverlapCapsule` finds a ceiling, and reads the button rather than `State` (which is `Idle` whenever the player is not moving). The standing height, centre and eye are captured from the prefab in `Awake` — only the crouched height is a tunable. **Crouching and carrying exclude each other, both ways** (2026-08-11): full hands ignore the crouch button and `Pet.CanBeTakenBy` refuses a crouched player, which is what makes "crouched with an animal" unreachable — the two carry anchors are plain children of the root beside `CameraRoot` and do not drop, so a crouched carrier would hold the Dog inside the tabletop they crawled under. That ceiling test also skips colliders whose root has a `PlayerController`: avatars share the `Default` layer with the house, so without it a teammate standing against you silently forbade standing up. `PlayerMotion` carries the flag to the other peers, because `SensedPlayer.AimPoint` is the capsule's centre and a remote avatar's controller is switched off.
- **AI Navigation (`com.unity.ai.navigation`) is installed and in use** — the level's NavMesh is baked through it (see *Current state*). Use it for pet flee/hide and Old Man movement rather than hand-rolling pathfinding.
  **The agent type is a project-wide setting, not a surface setting.** A `NavMeshSurface` only references it by `agentTypeID`, so a bad agent type silently breaks every bake and reads as "the navmesh ignores walls". Values live in `ProjectSettings/NavMeshAreas.asset`, edited via `Window → AI → Navigation → Agents`; there is exactly one type, `Humanoid`, and its radius is sized to the house's narrowest doorway. Read the file before changing anything navigation-related.
  **In this house the visible floor and the collidable floor are different objects.** Every floor module carries `Interior_Floor` (mesh **and** `BoxCollider`, layer `WalkingArea`, `y −0.10…0.10`) plus `Interior_Floor_Up` — a zero-thickness mesh with no collider that just draws the top surface. One tile in the entrance hall, `Floor_0/Floor/Floor2 (5)/Interior_Floor_Up (1)`, shipped without its collidered twin: the floor was visible but not solid, so the player walked in and dropped 10 cm onto the lawn plane that runs under the whole house. Fixed 2026-08-03 by giving that tile the same `BoxCollider` and the `WalkingArea` layer. The way to find this class of bug is a grid of downward raycasts compared against the floor renderers — "visible but not solid" cells; it is invisible in the scene view.
  **Bakes are driven by `Include Layers`, not by static flags** — an object outside the mask does not exist for the bake, so it neither blocks nor creates walkable floor. The three pets sit on their own `Pet` layer, added 2026-08-03 so the interaction search can run every frame with a mask: unmasked, that query returns up to 59 colliders in a furnished room, masked it returns at most three. Layer assignment for the house lives in `HouseOneFloor.prefab`: floor on `WalkingArea`, all structural pieces on `BlockedArea`, the 8 door leaves on `Door` (deliberately outside the mask so the navmesh runs through every doorway, while still blocking the player physically). Because of that, a shut door stops the player but **not** a `NavMeshAgent` — AI that must respect doors reads `Door.IsOpen` (`MECHANICS.md` 4.6), it cannot rely on the navmesh.
  Removing a `NavMeshSurface` component **deletes its baked `NavMeshData` asset** — the package does this in `NavMeshAssetManager`, without a prompt.
- **The alien's own avatar does not fit the alien's own prefab — we build our own.** `Model.fbx` names its bones `mixamorig:Hips`, `mixamorig:LeftUpLeg`, … and `ModelAvatar` is built for those names, but the vendor prefab `CuteAlien.prefab` (the ancestor of our `Player.prefab`) renames them to `Hips`, `LeftUpLeg`, … — so the humanoid avatar binds to nothing and **no animation is applied at all**, silently. Measured 2026-08-03: `Animator.GetBoneTransform(Hips)` returned null even with `AlwaysAnimate`. The fix in this project is `Assets/_Game/Content/Level/Player/Player-Avatar.asset`, built with `AvatarBuilder.BuildHumanAvatar` against the renamed skeleton and assigned to `Player.prefab`. Three traps came with it. A humanoid bone's name **must be unique in the whole hierarchy** (the mesh object `Head` had to be renamed to `HeadMesh`, or the build fails with "Ambiguous Transform"). `AvatarBuilder` reports failure only through `avatar.isValid`, with the real reason in the console. And **the skeleton must be described unscaled**: the first `SkeletonBone` entry is the root, and putting the prefab's own `0.1` scale in it makes Unity mis-measure the character — every retargeted pose then sat `0.10 m` too low and the model walked knee-deep in the floor (measured 2026-08-03: feet at `−0.09` instead of `+0.011`). Build the avatar from a probe whose root scale is `1`; the prefab keeps its `0.1` on top. If bones are ever renamed or the rig re-proportioned, rebuild that asset and re-measure the feet.
- **Almost the whole house is marked Static — anything that has to move must be cleared first.** 901 of `HouseOneFloor`'s 949 objects carry `Everything` static flags. A **Batching Static** renderer is merged into a combined mesh when Play mode starts and then ignores its transform: the object still moves for physics, but the visible geometry stays put. That is how `Corner_3B/Exterior_Door` behaved on 2026-08-03 — measured in Play mode, its collider swung `0.776 m` while its renderer moved `0.000 m`, so the door was open but looked shut. Clearing the flags on the leaf and its five children fixed it. Before animating anything inside the house, check `GameObjectUtility.GetStaticEditorFlags`.
- **An unfocused editor does not simulate.** With the OS focus elsewhere (e.g. on a chat window driving the editor over MCP), play mode freezes entirely — `Time.time` stands still and nothing moves, which reads as impossible teleports and vanished states between two tool calls. For any MCP-driven play-mode measurement, set `Application.runInBackground = true` first (runtime-only, resets with play mode); discovered at the cost of half an hour on 2026-08-06.
- **Git LFS is active** (`.gitattributes` routes models, textures, audio and other binaries through it). Keep new binary assets under the existing LFS rules rather than adding patterns ad hoc.
- **Unity YAML merges are probably *not* protected.** `.gitattributes` asks for `merge=unityyamlmerge`, but the driver itself is a local git-config setting that isn't versioned — and it was absent in every scope on this machine as of 2026-08-01. Check with `git config --get merge.unityyamlmerge.driver`; empty means git falls back to a plain text merge on `.unity`/`.prefab`/`.asset`, which corrupts them. Avoid situations that merge scene/prefab changes; if one is unavoidable, say so and let the user resolve it in the editor.

## Working rules

These are load-bearing. Keep them in force even when the task is small.
- **For codestyle use `CODESTYLE.md`
- **Don't commit stuff to git.** User can make a commit on his own after they check out your work.
- **Don't write extensive comments in code.** Use short technical vocabulary and use it only in places where those commentaries are absolutly necessary for understanding code. if a method is called GetPlayersCount and returns players count - it's obvious what it does. Only some hardcoded complicated logic requires descriptions on how it works. This **overrides** "match the surrounding style": much of the existing code carries long narrative comments and XML docs written before this rule — do not copy that density when editing those files, and don't rewrite the old ones either unless the code under them changes.
  - **One or two lines.** A comment states the non-obvious constraint or the measurement, not the story around it. No dates, no bug retellings, no "this used to be X" — that history belongs in `CLAUDE.md` or the design docs.
  - **Only where the code cannot say it**: a magic number's origin, an ordering that looks arbitrary, an API trap (Unity fake-null, undefined `RaycastNonAlloc` order). Never a paraphrase of the next line.
  - **Same for `[Tooltip]` and XML docs.** A tooltip is a field label with its unit — one sentence, two at most. `<summary>` says what the type is for, not why it was written.
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
