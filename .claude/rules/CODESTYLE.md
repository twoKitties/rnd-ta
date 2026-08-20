# CODESTYLE.md

How code in this repo is planned and written. `CLAUDE.md` owns the project's facts, its vocabulary
and the rules of the turn; this file owns the code. Read both before writing anything.

Formatting is not here — it is in `.editorconfig` at the repo root and the editor applies it. What
follows is what a formatter cannot check.

## Planning: C4

Every plan is stated at a **named C4 level**, and one plan does not mix levels. The mapping to this
repo is fixed:

| Level | What it is here | Which doc already owns it |
| --- | --- | --- |
| 1 — Context | The raid, the 2–4 people playing it, and everything outside the process: the other peers, the transport, the OS. | `DD.md` |
| 2 — Container | One build, two roles — **host** (server plus a local client, decides) and **client** (receives). The authority seam between them is the only boundary at this level. | `NETCODE.md` |
| 3 — Component | The folders of `Code/`: `App`, `AI`, `Audio`, `Doors`, `Level`, `Noise`, `OldMan`, `Pets`, `Player`, `Spawning`, `UI`. One folder is one component with one responsibility. | `MECHANICS.md` + the Layout section of `CLAUDE.md` |
| 4 — Code | Classes and the calls between them. | the code |

- **Start at level 3.** Name the components the change touches and the arrows that change between
  them. If a change touches more than three, say so before writing — that is a design change
  wearing a task's clothes.
- **Drop to level 4 only for the classes actually being edited.** A class diagram of code nobody is
  touching is ceremony.
- **Go up to level 2 only when authority moves** — when the answer to "which peer decides this"
  changes. Then `NETCODE.md` is updated in the same commit, as `CLAUDE.md` already requires.
- **Level 1 is written once.** A plan that seems to need it means the design moved, and `DD.md` is
  the conversation to have first.
- **Every box carries a name, what it is, and one line of responsibility. Every arrow carries a
  direction and an intent** — "client sends `RequestUse(index)`, host re-checks the distance", not
  a bare line between two rectangles. An unlabelled arrow is not a plan.
- **Diagrams are Mermaid inside a markdown file**, so they diff and review like text. No binary
  diagram files in the repo.

## OOP, where it is necessary

Unity is already a composition engine: a prefab is an object graph and a `MonoBehaviour` is a role
it plays. The default answer to "should this be a hierarchy" is therefore no. These rules are about
the cases where the answer is yes.

**The abstraction ladder** — before adding an interface, a base class or a `virtual`, walk it and
stop at the first hit. Same shape as the "write the minimum that works" ladder in `CLAUDE.md`:

1. Is there a **second implementation today**? A hypothetical second one is not one — stop here.
2. Do the cases differ in **data only**? Then it is one class and a serialized field. The three
   pets differ by `CarryPose` and agent size, not by type.
3. Do they differ in **one decision**? Then it is a `switch` over an enum. An exhaustive switch in
   one place reads better than three files and allocates nothing.
4. Do they carry **their own state and their own lifecycle**? Now polymorphism has earned it.
5. Is the seam there so a peer, a test or a standalone level can **swap the implementation**?
   That is the other legitimate reason, and it is the one the netcode uses.

- **`sealed` by default.** Unsealing is a design decision with a reason; it also lets IL2CPP
  devirtualize. `virtual` is a contract, and the base states what an override must preserve.
- **Inheritance shares state; an interface shares a role.** A base class with no fields wanted to
  be an interface.
- **Never inherit to reuse a method.** Extract a plain class and hold it. A `MonoBehaviour` that
  derives from another `MonoBehaviour` inherits its inspector, its lifecycle and its bugs as well.
- **An override that does nothing means the hierarchy is wrong.** Split it rather than teach the
  base to tolerate it.
- **Deep modules, narrow surfaces.** A class whose public members outnumber its private ones is a
  data bag with extra steps. Hide the mechanism, expose the decision.
- **New systems put the rule in a plain method with explicit inputs**; the `MonoBehaviour` supplies
  them from the engine. Anything decidable without `UnityEngine` should be. Do not retrofit this
  into tuned code that works — the brains are the most expensive code in the project to disturb.
- **Dependency direction is fixed, not preferred.** `App/` knows nothing about the level; the level
  tells the session, never the reverse. Call down, raise up: a parent calls its children directly,
  a child raises an event or calls through a registered interface rather than naming its parent's
  type.
- **The three bootstrappers are the composition roots** — `Bootstrapper` (the app),
  `HubBootstrapper`, `LevelBootstrapper`. **Each lives in its own scene's folder** — `App/`,
  `Hub/`, `Level/` — never filed together by role, which is what keeps `App/` holding only the
  things that outlive every scene. Wiring is a serialized reference or the entry point's
  properties. There is no DI container and no service locator in this project — do
  not add one without saying so first.
- **Static `Current` / `Active` are lookups, not services.** Get-only, set in `Awake` or
  `OnStartClient`, cleared in `OnDestroy`, and null means "playing alone" rather than failure.
  Never hang behaviour off one.
- **The netcode split is a component list on the prefab, not an abstraction.** `LocalAvatar` and
  `ServerSimulated` are the decided answer; an `IAuthority` interface is not an improvement on it.

## Naming and shape

- **Private fields `_camelCase`, serialized fields plain `camelCase`.** The underscore means "no
  one outside this file"; a serialized field has none because its name is the label the inspector
  shows. Types, methods, properties, events: PascalCase. No `m_`, no `this.`.
- **`[SerializeField] private`, never a public field.** Public means another script writes it, and
  almost nothing should. Expose reads as a get-only property.
- **One type per file, named after the type.** Nest a type only when it cannot exist without the
  outer one.
- **Member order, always the same:** serialized tunables → serialized references → private state →
  properties → Unity lifecycle in call order (`Awake`, `OnEnable`, `Start`, `Update`, `LateUpdate`,
  `OnDisable`, `OnDestroy`) → public API → private helpers. No `#region`.
- **An enum, not a pair of bools** (`PlayerController.State` is the pattern). Two bools admit a
  state that cannot happen, and something will eventually reach it.
- **A namespace mirrors the folder path: `_Game.Code.<Folder>`.** One per folder, on every file in
  `Code/`, no exceptions — `Code/Player/PlayerController.cs` is `_Game.Code.Player`. Nothing is
  loose at the root of `Code/`, so a bare `_Game.Code` is a file that has not been filed yet.
  There are still no asmdefs, so the namespace *names* the dependency direction above without
  enforcing it: the compiler will not catch `App/` reaching into the level.
  - **`_Game.Code.Editor` shadows `UnityEditor.Editor`.** Inside it an unqualified `Editor` binds
    to the namespace, so a custom inspector must spell out `UnityEditor.Editor` or the build fails
    with `CS0118: 'Editor' is a namespace but is used like a type`.

## Unity API

- **Fake null.** A destroyed `UnityEngine.Object` compares `== null` but is not null, so `?.` and
  `??` lie. Use `== null` or `if (obj)`. This one is repeated in `CLAUDE.md` on purpose.
- **Nothing searches the scene at runtime.** No `FindObjectOfType`, no `GameObject.Find`, no
  `GetComponent` in `Update`. References come from a serialized field, from caching in `Awake`, or
  from the entry point's properties (`MECHANICS.md` 7.6). Cache `transform` too if it is read every
  frame.
- **`CompareTag`, never `go.tag == "x"`** — the property allocates a string per call.
- **Per-frame physics queries use the `NonAlloc` overload with a preallocated buffer and a layer
  mask.** The mask is not an optimisation here: unmasked, the interaction search returns up to 59
  colliders in a furnished room and masked it returns three. Results are unordered and the returned
  count can exceed the buffer — never index past it, never assume `[0]` is nearest.
- **No LINQ, no closures, no `foreach` through an interface reference, no string building in code
  that runs per frame.** All four are fine in `Awake`, in setup, in UI events and in editor code.
  Know which side a method is on before deciding.
- **Delete empty lifecycle methods.** An empty `Update` still costs the call, on four avatars plus
  three pets plus Old Man.
- **The lifecycle split is load-bearing.** `Awake` wires only this object; `Start` and
  `OnStartClient` are the first moment another object may be touched; `OnEnable` / `OnDisable`
  subscribe and unsubscribe, always paired and symmetric; anything registered in `Awake` is
  released in `OnDestroy`.
- **Anything moving per frame is multiplied by `Time.unscaledDeltaTime`** — except pointer `Look`,
  which is already a per-frame increment. Both halves are in `CLAUDE.md` with the measurement.
- **Coroutines only for things measured in frames.** No `async void` anywhere.

## Failure

- **Guard clause, early return.** No `catch` that swallows, and no defensive branch against a state
  that cannot happen — that hides the bug instead of the crash.
- **A missing serialized reference is a bug and must read as one.** `[RequireComponent]` where it
  applies; otherwise one `Debug.LogError` naming the object, then return — not a
  `NullReferenceException` every frame.
- **A null static accessor means standalone, not failure.** `RaidSession.Active`,
  `RaidState.Current`, `DoorState.Current`: every caller takes the alone branch, which is what
  keeps pressing Play straight into `Level` working.

## Comments

- **Don't write extensive comments in code.** Use short technical vocabulary and use it only in
  places where those commentaries are absolutely necessary for understanding code. If a method is
  called `GetPlayersCount` and returns players count — it's obvious what it does. Only some
  hardcoded complicated logic requires descriptions on how it works. This **overrides** "match the
  surrounding style": much of the existing code carries long narrative comments and XML docs
  written before this rule — do not copy that density when editing those files, and don't rewrite
  the old ones either unless the code under them changes.
- **One or two lines.** A comment states the non-obvious constraint or the measurement, not the
  story around it. No dates, no bug retellings, no "this used to be X" — that history belongs in
  `CLAUDE.md` or the design docs.
- **Only where the code cannot say it**: a magic number's origin, an ordering that looks arbitrary,
  an API trap (Unity fake-null, undefined `RaycastNonAlloc` order). Never a paraphrase of the next
  line.
- **Same for `[Tooltip]` and XML docs.** A tooltip is a field label with its unit — one sentence,
  two at most. `<summary>` says what the type is for, not why it was written.

## Tunables

- **A number a designer touches is a serialized field with a `[Tooltip]` naming its unit** — never
  a `const`, never inline. `MECHANICS.md`'s table is its authority.
- **Clamp in `OnValidate`,** so an impossible value is caught in the inspector rather than in a
  build.
- **Changing a field's unit means retuning every asset carrying it in the same commit,** or the old
  number silently means something else.

## Idioms already in force

Settled, each paid for by a measured bug, each written up in `CLAUDE.md`. Reuse them rather than
inventing a parallel mechanism.

- **The object names its own kind** — a marker component found with `GetComponentInParent`
  (`SpawnPoint`, `FootstepSurface`), so new content wires itself with no code change.
- **Gate by a list of components on the prefab, not a branch inside the code** — `LocalAvatar` for
  "mine", `ServerSimulated` for "the server drives this". The tuned code stays untouched.
- **`Apply*` runs on every peer, the authority included.** One path to the picture; only the
  trigger differs.
- **Replicated state lives on its own server-spawned prefab and the scene object holds a plain
  reference** — `RaidState`, `LobbyRoster`. Never promote a scene object to `NetworkBehaviour`.
- **One writer per piece of state, and every writer is named.** `Cursor.lockState` has exactly
  three — `LocalAvatar`, `EndScreenUI`, `HubMenuUI` — and the outcome text has exactly one. An
  unnamed writer is a bug; adding a named one is a documented decision, not a free action.
- **Whatever switches something off is what switches it back on, and restores only what it
  changed** — `EndScreenUI.suspendWhileOpen`, `FirstPersonBody`.
