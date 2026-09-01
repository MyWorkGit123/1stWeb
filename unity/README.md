# unity/ — the Brinehold client

`BrineholdClient` is the Unity 6 LTS project for the **view layer**: rendering, input, camera, UI.
It decides nothing about the game. It sends commands to the authoritative server and draws the state
the server replicates back (`MULTIPLAYER_ARCHITECTURE.md` §2.3).

---

## Verification status — read this

The C# under `Assets/` **compiles cleanly** against a stub of the UnityEngine API
(`tools/unity-compile-check`), which runs in CI on every change. It has still **never been opened in
the Unity editor**.

| Layer | Status |
|---|---|
| `packages/com.brinehold.client` (selection, control groups, orders, camera, HUD, placement) | ✅ Compiled and unit tested — 38 tests against a real server |
| `unity/BrineholdClient/Assets/**` (MonoBehaviours) | 🟡 **Compiles against a UnityEngine stub, zero errors and zero warnings.** Never run in the editor |

What the stub check catches: typos, missing usings, wrong member names on our own types, signature
mismatches, and plain C# errors. What it cannot catch: a difference between the stub's signature and
Unity's real one, component wiring, and anything about runtime behaviour.

Five Unity-specific problems were found and fixed by writing that check and reading the code against
it, none of which a compiler alone would have reported:

1. **Pooled views were never activated.** Clones of an inactive prefab template are themselves
   inactive, so every newly visible entity would have existed, moved and fought while being
   completely invisible.
2. **The replica's entity enumerator shared one buffer**, so the renderer and the input layer
   walking it in the same frame could corrupt each other.
3. **Interpolation ran from the input controller's `Update`**, which only worked because of the
   order components happened to be added in. It is now `LateUpdate` on the owner.
4. **`??` was used on `Shader.Find`.** `UnityEngine.Object` overloads `==` but the null-coalescing
   operator does not use that overload — a well-known trap.
5. **Build output landed inside `packages/`.** Unity imports everything under a package, so a `bin/`
   of our own assemblies would have been imported as plugins *and* compiled from source, giving
   duplicate-type errors that are very hard to trace. Output now goes to `artifacts/`.

Expect the first editor session to still turn up something. When it does, the errors are worth
reporting back — most will be one-line fixes.

## Running the prototype

1. Create a new empty scene (`File → New Scene → Basic (Built-in)` or an empty URP scene).
2. Create an empty GameObject and add the **`PrototypeSceneSetup`** component.
3. Press **Play**.

`PrototypeSceneSetup` builds the whole scene from primitives at runtime — camera, lighting, terrain
mesh, unit prefabs, fog, HUD, minimap and input. There are no prefab assets or `.unity` scene files
to author, which also means there is nothing binary in this folder that a reviewer cannot read in a
pull request.

## Controls

| Input | Action |
|---|---|
| `W A S D`, arrow keys, screen edge | Pan |
| Mouse wheel | Zoom |
| `Q` / `E` | Rotate |
| Left click | Select |
| Left drag | Box select |
| Shift + left click | Add to / remove from selection |
| Double click | Select all of that type on screen |
| Right click | Contextual order — move, harvest, or attack |
| `Ctrl` + `0`–`9` | Assign control group |
| `Shift` + `0`–`9` | Append to control group |
| `0`–`9` | Recall control group |
| `H` | Cycle idle workers |
| `Space` | Centre on selection |
| `X` | Stop |
| `B` / `N` / `M` / `K` | Place house / lumber camp / fishing wharf / dock |
| `V` / `C` / `F` | Train worker / soldier / ship at the selected building |
| `Escape` | Cancel placement |
| `F3` | Toggle the network graph |
| Click the minimap | Jump the camera |

## What this build can and cannot do

**Can:** run a full match against the authoritative server in **listen mode** — the client starts the
server in-process and connects to it over the loopback transport as an ordinary client, with no
privileged access and no second code path.

**Cannot yet:** connect two machines. There is no socket transport (M4 in `DEVELOPMENT_ROADMAP.md`);
`LoopbackNetwork` is in-process only. Two-player play across a network is the next networking
milestone. The headless integration tests already run two real clients against one server, so the
replication path is proven — it simply has not crossed a network interface yet.

## Architecture reminders

- **The view never writes to the replica.** Data flows one way: server → replica → view.
- **Nothing in `Assets/` may contain a game rule.** If a behaviour decides an outcome, it belongs in
  `packages/com.brinehold.sim` where it can be tested and where the server can enforce it.
- **The HUD is IMGUI on purpose.** The prototype exists to prove networking and simulation; the
  production HUD (`GAME_DESIGN.md` §22) is a UI Toolkit rebuild in M5 that reads the same `HudModel`.
