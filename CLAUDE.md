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

**Current state (2026-08-01):** greybox / art-assembly stage. There is one level (`Level.unity`), built out of asset-store packs; there is essentially no gameplay code yet (`Bootstrapper.cs` is still the empty Unity template), none of the prefabs carry scripts, and nothing uses AI Navigation. Nothing about the runtime architecture is settled — do not assume one exists, and do not invent one silently. In particular **no netcode package is installed** (`com.unity.multiplayer.center` is only the recommender tool), so the multiplayer stack is an open decision: ask before pulling one in.

## Layout

```
rnd-ta/
├── CLAUDE.md                   ← this file
├── Assets/
│   ├── _Game/                  ← everything WE author
│   │   ├── Code/               ← gameplay scripts
│   │   └── Content/
│   │       ├── Scenes/         ← Level.unity
│   │       └── Level/          ← our prefabs + materials (House, Environment, Lawn, UFO, Old Man, Dog, Kitty, Parrot)
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

- **URP.** Anything shader/material/lighting related must be URP-compatible. There are separate PC and Mobile pipeline assets, both bound in `ProjectSettings/QualitySettings.asset` — a change to rendering usually needs to be made in both. Note that `com.unity.postprocessing` (PPv2) is in the manifest but is Built-in-RP only and must **not** be used: URP post-processing goes through the Volume profiles in `Assets/Settings/`.
- **Input System package only** (the legacy `Input` API is disabled via `activeInputHandler` in `ProjectSettings/ProjectSettings.asset`, so `Input.GetKey` etc. will not work). Bindings are authored in the `.inputactions` asset. Whether a C# wrapper class is generated is controlled by `generateWrapperCode` in `Assets/InputSystem_Actions.inputactions.meta` — check it before assuming a generated class exists to import; with generation off, use `PlayerInput` / `InputActionAsset` references instead. A generated `.cs` is machine-owned: never hand-edit it, change the asset and regenerate.
- **AI Navigation (`com.unity.ai.navigation`) is installed** — a candidate for pet flee/hide movement and Old Man movement, not a decision that's been made. Consider it before hand-rolling pathfinding.
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
