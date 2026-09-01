# unity/ — the Brinehold client

`BrineholdClient` is the Unity 6 LTS project for the **view layer**: rendering, input, camera, UI.
It decides nothing about the game. It sends commands to the authoritative server and draws the state
the server replicates back (`MULTIPLAYER_ARCHITECTURE.md` §2.3).

---

## ⚠️ Verification status — read this

**The C# in `Assets/` has not been compiled or run.** This project was developed in an environment
with the .NET SDK but no Unity editor, so:

| Layer | Status |
|---|---|
| `packages/com.brinehold.client` (selection, control groups, orders, camera, HUD, placement) | ✅ **Compiled and unit tested** — 38 tests, run against a real server |
| `unity/BrineholdClient/Assets/**` (MonoBehaviours) | ⚠️ **Written but never compiled.** Expect to fix compile errors on first open |

This split is deliberate. All the *logic* that could be tested without an engine was pushed down
into `com.brinehold.client`, so the untested surface is only the thin adapter that turns Unity input
into calls and Unity transforms into positions. Treat the first editor session as a bring-up task,
not as a finished feature.

---

## Opening it

1. Install **Unity 6 LTS** (6000.0.x) with **Universal RP**.
2. Open `unity/BrineholdClient` as a project. The local packages resolve through the `file:`
   references in `Packages/manifest.json`, which point at `../../packages`.
3. Unity will import and compile. Fix anything that fails — see the status note above.

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
