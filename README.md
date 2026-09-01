# Brinehold

*Working title:* **Brinehold: Tides of the Free Isles**

An original 3D **multiplayer real-time strategy game** for PC: deep pirate-settlement building,
physical production chains and vertical cliffside logistics, wrapped in Age-of-Empires-style
competitive RTS structure — 2 to 8 players, real time, server-authoritative.

**Engine:** Unity 6 LTS · C#
**Status:** 🟢 The prototype **plays a real match across separate processes over UDP** — server plus
two clients, with **185 tests** passing headlessly (`dotnet test`, no Unity needed).
🟡 The **Unity client is written but has never been compiled**. See [`unity/README.md`](unity/README.md).

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

*`unity/` is still a scaffold; everything else contains working, tested code.*

---

## Try it

```bash
dotnet test Brinehold.sln                 # 185 tests: maths, game rules, networking, client, anti-cheat
tools/dev/run-networked-match.sh          # server + two clients, three processes, real UDP sockets
tools/dev/benchmark.sh                    # a ten-minute match measured for tick cost and bandwidth
tools/dev/run-local-match.sh              # a real-time headless match
```

Across two machines:

```bash
# on the server machine
dotnet run -c Release --project src/Brinehold.Server -- --port 7777 --players 2
# on each client machine
dotnet run -c Release --project src/Brinehold.Tools.TestClient -- --host <server-ip> --port 7777 --name Alice
```

Measured on one core, two players, ten minutes of match time:
**0.071 ms per tick** (705× real time), **34.6 B/s per client**, **0 position corrections**.

## What works today

Server-authoritative match loop at 20 Hz · deterministic fixed-point simulation · workers that
physically harvest, haul and build · construction and unit training · combat and a win condition ·
deterministic A* pathfinding on land and water · per-player fog of war enforced at the replication
boundary · intent-based replication · command validation and anti-cheat · a cheat-client test
harness that proves the authority model.

Client-side: selection (click, box, shift, double-click), ten control groups, contextual
right-click orders, a camera model and a build-placement preview that agrees with the server —
all engine-independent and unit tested.

## What is not verified

The **Unity client** is written but **has never been compiled** — it was built in an environment
with the .NET SDK and no Unity editor. Expect a bring-up session. The logic it leans on is tested;
the MonoBehaviour adapter layer is not. See [`unity/README.md`](unity/README.md).

Networking: a UDP transport with sequence numbers, a rolling acknowledgement field,
retransmission, fragmentation and timeouts — verified by a match played between three separate
operating-system processes, and by a run at 20% simulated packet loss.

## What does not exist yet

The no-float and no-Unity analysers, a JSON content package, reconnection, replays and spectating
(M4–M6), and every system beyond the prototype's scope (production chains, vertical building,
population, the full combat and naval rosters, technology, diplomacy, AI).

## Next step

Bring up the Unity client — it is the only part of the prototype that has never been compiled.

---

<sub>Note: `index.html`, `css/` and `images/` are an unrelated static-site starter that predates this
project in the repository. They are left untouched.</sub>
