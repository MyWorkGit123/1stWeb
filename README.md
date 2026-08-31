# Brinehold

*Working title:* **Brinehold: Tides of the Free Isles**

An original 3D **multiplayer real-time strategy game** for PC: deep pirate-settlement building,
physical production chains and vertical cliffside logistics, wrapped in Age-of-Empires-style
competitive RTS structure — 2 to 8 players, real time, server-authoritative.

**Engine:** Unity 6 LTS · C#
**Status:** 🟡 **M0 — architecture and design.** No gameplay code exists yet, deliberately.

---

## Read this first

| Document | What it covers |
|---|---|
| [`GAME_DESIGN.md`](GAME_DESIGN.md) | The design contract: pillars, setting, city building, population, vertical construction, military, naval, progression, victory conditions, UI |
| [`MULTIPLAYER_ARCHITECTURE.md`](MULTIPLAYER_ARCHITECTURE.md) | **Start here for the technical proposal.** Lockstep vs. snapshot vs. the recommended hybrid, authority, replication, anti-cheat, reconnection, replays, hosting |
| [`TECHNICAL_ARCHITECTURE.md`](TECHNICAL_ARCHITECTURE.md) | Module boundaries, the full folder structure, data layout, pathfinding, performance strategy, CI |
| [`ECONOMY_DESIGN.md`](ECONOMY_DESIGN.md) | The complete original production-chain system, logistics mathematics, trade, unrest |
| [`COMBAT_DESIGN.md`](COMBAT_DESIGN.md) | Damage model, morale, land units, ships, boarding, siege, amphibious warfare |
| [`DEVELOPMENT_ROADMAP.md`](DEVELOPMENT_ROADMAP.md) | M0–M16 with acceptance criteria and hard stop-gates |
| [`TESTING.md`](TESTING.md) | Test levels, determinism matrix, anti-cheat testing, and the prototype manual test script |
| [`CHANGELOG.md`](CHANGELOG.md) | What has changed, when |

---

## The architecture in one paragraph

The simulation is a **pure C# library with no `UnityEngine` dependency**, running deterministically
in fixed-point maths at a fixed 20 Hz on an **authoritative headless server**. Clients send commands;
the server validates every one of them and owns all resources, ownership, damage, production,
technology and combat outcomes. State comes back not as streamed transforms but as **intents**
("worker 8241 begins hauling 8 planks from A to B at tick 4120"), which each client extrapolates
locally — giving lockstep's bandwidth profile with a server's authority. **Fog of war is enforced by
not sending the data**, which makes map hacks structurally impossible rather than merely detectable.
Full reasoning, alternatives considered, and bandwidth numbers are in
[`MULTIPLAYER_ARCHITECTURE.md`](MULTIPLAYER_ARCHITECTURE.md).

---

## Originality

Brinehold is inspired by the *gameplay style* of settlement-builder pirate games and classic RTS
pacing. It is original IP. No art, audio, names, maps, story or assets are taken from any existing
game. The rules for contributors are in [`GAME_DESIGN.md` §0](GAME_DESIGN.md) and are not optional.

---

## Repository layout

```
packages/   shared C# code as local UPM packages (core, sim, content, protocol, net, ai)
src/        .NET executables and test projects (server, tools, tests)
unity/      the Unity client — view layer only
content/    authored source content (maps, balance)
tools/      build, CI and developer scripts
tests/      golden replays, test maps, fixtures
```

*The directories are scaffolded but empty — see each area's `README.md`.*

---

## Next step

Implementation is **paused at the M0 gate** pending review and sign-off of the ten architecture
decisions in [`MULTIPLAYER_ARCHITECTURE.md` §14](MULTIPLAYER_ARCHITECTURE.md).

---

<sub>Note: `index.html`, `css/` and `images/` are an unrelated static-site starter that predates this
project in the repository. They are left untouched.</sub>
