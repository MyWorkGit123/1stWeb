# BRINEHOLD — Technical Architecture

**Status:** Proposed. Requires sign-off with `MULTIPLAYER_ARCHITECTURE.md` before Milestone 1.
**Scope:** Code architecture, module boundaries, folder structure, data layout, pathfinding,
performance strategy, content pipeline, build and CI.

---

## 1. The one structural rule

> **The simulation is a pure C# library. It has no reference to `UnityEngine`. Unity is a view.**

Everything else in this document follows from that. It gives us a headless .NET server, sub-second
unit tests without the Unity editor, cross-platform fixed-point determinism, a replay tool that *is*
the game, and the freedom to change renderers without touching game rules.

```
            ┌───────────────────────────────────────────────────────────┐
            │  Brinehold.Sim        (pure C#, netstandard2.1, no Unity) │
            │  · entities, systems, rules, pathfinding, AI, fixed-point │
            └──────────────┬─────────────────────────────┬──────────────┘
                           │ referenced by               │ referenced by
              ┌────────────▼───────────┐     ┌───────────▼────────────────┐
              │ Brinehold.Server       │     │ Unity client               │
              │ .NET 8 headless host   │     │ view · input · UI · audio  │
              │ authoritative sim      │◄───►│ replica sim (presentation) │
              └────────────────────────┘ net └────────────────────────────┘
```

**Dependency direction is strictly one-way.** `Sim` knows nothing about `Net`, `Server` or the
client. An assembly-reference test in CI fails the build if that is ever violated.

---

## 2. Module map

| Module | Kind | Depends on | Responsibility |
|---|---|---|---|
| `Brinehold.Core` | UPM pkg / netstandard2.1 | — | `Fix64`, fixed-point vectors, deterministic PRNG, dense id tables, bit readers/writers, pooled collections |
| `Brinehold.Sim` | UPM pkg / netstandard2.1 | Core | The game: world state, all systems, rules, pathfinding, economy, combat, vision, snapshots |
| `Brinehold.Content` | UPM pkg + data | Core | Content schema and loader (units, buildings, goods, techs, ships, maps) — *data, not behaviour* |
| `Brinehold.AI` | UPM pkg | Core, Sim | AI players. Emits ordinary commands; may only read what its fog allows |
| `Brinehold.Protocol` | UPM pkg | Core | Wire messages + source-generated codecs; shared by client and server |
| `Brinehold.Net` | UPM pkg | Core, Protocol | `ITransport`, reliability channels, replication tiers, interest management, snapshot transfer |
| `Brinehold.Server` | .NET 8 exe | all of the above | Match host: sim loop, command validation, per-player replication, lobby, admin |
| `Brinehold.Client` (Unity asmdefs) | Unity | Core, Sim, Content, Protocol, Net | Rendering, input, UI, audio, camera, replica sim, netgraph |
| `Brinehold.Tools.*` | .NET 8 exes | varies | Replay verifier, content validator, map compiler, load-test bot harness |
| `*.Tests` | .NET 8 test projects | varies | xUnit; run in `dotnet test`, no Unity |

**Why UPM packages:** a local package (`file:` reference in Unity's `manifest.json`) lets the *same
source folder* be compiled by both Unity (via `.asmdef`) and by plain `dotnet build` (via `.csproj`
globbing the same files). One copy of the code, two build systems, no DLL-copy step, no drift.

---

## 3. Full project folder structure

```
brinehold/
├── GAME_DESIGN.md                     ← design contract
├── MULTIPLAYER_ARCHITECTURE.md
├── TECHNICAL_ARCHITECTURE.md          ← this file
├── ECONOMY_DESIGN.md
├── COMBAT_DESIGN.md
├── DEVELOPMENT_ROADMAP.md
├── TESTING.md
├── CHANGELOG.md
├── CONTRIBUTING.md
├── LICENSES.md                        ← every third-party/commissioned asset licence
├── Brinehold.sln                      ← .NET solution (server, sim, tools, tests)
├── Directory.Build.props              ← shared C# settings: langversion, nullable, analysers
├── .editorconfig
├── .gitattributes                     ← LFS rules for binary assets
├── .gitignore
│
├── packages/                          ← shared code as local UPM packages (Unity + .NET both build these)
│   ├── com.brinehold.core/
│   │   ├── package.json
│   │   ├── Brinehold.Core.asmdef
│   │   ├── Brinehold.Core.csproj
│   │   └── Runtime/
│   │       ├── Math/                  Fix64.cs, Fix2.cs, Fix3.cs, FixMath.cs, FixTrigTables.cs
│   │       ├── Random/                DeterministicRandom.cs
│   │       ├── Collections/           DenseArray.cs, IdTable.cs, RingBuffer.cs, PooledList.cs,
│   │       │                          StableSort.cs, BitSet.cs
│   │       ├── Serialization/         BitWriter.cs, BitReader.cs, Quantize.cs
│   │       └── Diagnostics/           SimAssert.cs, TickProfiler.cs
│   │
│   ├── com.brinehold.sim/
│   │   ├── package.json
│   │   ├── Brinehold.Sim.asmdef
│   │   ├── Brinehold.Sim.csproj
│   │   └── Runtime/
│   │       ├── World/                 SimWorld.cs, SimClock.cs, EntityStore.cs, ComponentArrays.cs,
│   │       │                          PlayerState.cs, SimConfig.cs
│   │       ├── Commands/              ICommand.cs, CommandQueue.cs, CommandValidator.cs,
│   │       │                          commands/ (MoveCommand, BuildCommand, TrainCommand, …)
│   │       ├── Systems/               ISimSystem.cs, SystemSchedule.cs
│   │       │   ├── Movement/          PathFollowSystem, LocalAvoidanceSystem, ShipMovementSystem
│   │       │   ├── Jobs/              JobMarketSystem, HaulSystem, HarvestSystem, ConstructSystem
│   │       │   ├── Economy/           ProductionSystem, StorageSystem, ConsumptionSystem,
│   │       │   │                      PopulationSystem, ContentmentSystem, TradeSystem
│   │       │   ├── Combat/            TargetingSystem, LandCombatSystem, NavalCombatSystem,
│   │       │   │                      MoraleSystem, DamageSystem, SiegeSystem, BoardingSystem
│   │       │   ├── Construction/      BuildSiteSystem, RepairSystem, DemolitionSystem
│   │       │   ├── Vision/            VisionSystem, FogGrid.cs, VisibilitySets.cs
│   │       │   ├── Tech/              TechSystem.cs, RankAdvanceSystem.cs
│   │       │   ├── Diplomacy/         DiplomacySystem.cs
│   │       │   └── Victory/           VictorySystem.cs, conditions/
│   │       ├── Nav/                   NavGrid.cs, NavTiers.cs, Connectors.cs, HpaGraph.cs,
│   │       │                          FlowField.cs, PathCache.cs, WaterNav.cs, PathToken.cs
│   │       ├── Spatial/               SpatialHash.cs, QuadTree.cs, ProximityQueries.cs
│   │       ├── Lod/                   SimLod.cs, DistrictAggregator.cs
│   │       ├── Snapshot/              SnapshotWriter.cs, SnapshotReader.cs, StateHash.cs
│   │       └── Replay/                ReplayWriter.cs, ReplayReader.cs, ReplayHeader.cs
│   │
│   ├── com.brinehold.content/
│   │   ├── Brinehold.Content.asmdef / .csproj
│   │   ├── Runtime/                   ContentDatabase.cs, Definitions/*.cs, ContentHash.cs, Loader/
│   │   └── Data/                      ← the authored game data (JSON, human-diffable)
│   │       ├── goods.json             maps.json     techs.json     ranks.json
│   │       ├── buildings/*.json       units/*.json  ships/*.json   doctrines/*.json
│   │       └── balance/*.json         ← tunables split out so designers can edit without merge pain
│   │
│   ├── com.brinehold.protocol/
│   │   ├── Brinehold.Protocol.asmdef / .csproj
│   │   ├── Schema/                    messages.schema.json   ← single source of truth
│   │   └── Runtime/                   Generated/*.g.cs, MessageIds.cs, ProtocolVersion.cs
│   │
│   ├── com.brinehold.net/
│   │   ├── Brinehold.Net.asmdef / .csproj
│   │   └── Runtime/
│   │       ├── Transport/             ITransport.cs, UnityTransportAdapter.cs, LiteNetLibAdapter.cs,
│   │       │                          LoopbackTransport.cs, NetworkSimulator.cs
│   │       ├── Channels/              ReliableOrdered.cs, UnreliableSequenced.cs, Fragmenter.cs
│   │       ├── Replication/           ReplicationServer.cs, InterestManager.cs, IntentEncoder.cs,
│   │       │                          CorrectionEncoder.cs, PrivateStateDelta.cs, AggregateEncoder.cs
│   │       ├── Client/                ReplicationClient.cs, ReplicaWorld.cs, Reconciler.cs
│   │       └── Diagnostics/           NetStats.cs, PacketRecorder.cs
│   │
│   └── com.brinehold.ai/
│       ├── Brinehold.AI.asmdef / .csproj
│       └── Runtime/                   AiPlayer.cs, Strategic/, Economic/, Military/, Scouting/,
│                                      Difficulty/, BuildOrders/
│
├── src/                               ← .NET-only executables and test projects
│   ├── Brinehold.Server/
│   │   ├── Brinehold.Server.csproj
│   │   ├── Program.cs                 host entry, CLI args, graceful shutdown
│   │   ├── MatchHost.cs               the 20 Hz authoritative loop
│   │   ├── Lobby/                     LobbyService.cs, MatchConfigNegotiation.cs
│   │   ├── Sessions/                  PlayerSession.cs, Reconnection.cs, SpectatorSession.cs
│   │   ├── Validation/                ServerCommandGate.cs, RateLimiter.cs
│   │   ├── Persistence/               SnapshotRing.cs, ReplayRecorder.cs
│   │   └── Admin/                     HealthEndpoint.cs, MetricsExporter.cs, AdminConsole.cs
│   ├── Brinehold.Tools.ReplayCheck/   re-simulates a replay, verifies state hashes
│   ├── Brinehold.Tools.ContentCheck/  validates content data + chain closure + balance sanity
│   ├── Brinehold.Tools.MapCompiler/   authored map → compiled deterministic map binary
│   ├── Brinehold.Tools.LoadTest/      N headless bot clients against one server
│   ├── Brinehold.Core.Tests/
│   ├── Brinehold.Sim.Tests/
│   ├── Brinehold.Net.Tests/
│   ├── Brinehold.Content.Tests/
│   └── Brinehold.Integration.Tests/   server + N in-process clients over LoopbackTransport
│
├── unity/BrineholdClient/             ← the Unity project (view layer only)
│   ├── Packages/manifest.json         file: references into ../../packages/*
│   ├── ProjectSettings/
│   └── Assets/
│       ├── Brinehold/
│       │   ├── Scripts/
│       │   │   ├── Boot/              GameBootstrap.cs, AppState.cs, ServerLauncher.cs (listen mode)
│       │   │   ├── View/              EntityViewPool.cs, UnitView.cs, BuildingView.cs, ShipView.cs,
│       │   │   │                      InterpolationBuffer.cs, GoodsCarryVisual.cs, DeathFx.cs
│       │   │   ├── Rendering/         InstancedRenderer.cs, LodGroups.cs, TerrainTierRenderer.cs,
│       │   │   │                      FogOfWarRenderer.cs, SelectionRings.cs, MinimapRenderer.cs
│       │   │   ├── Input/             CameraRig.cs, SelectionController.cs, ControlGroups.cs,
│       │   │   │                      HotkeyMap.cs, BuildPlacementController.cs, OrderIssuer.cs
│       │   │   ├── UI/                ResourceBar/, SelectionPanel/, BuildMenu/, Minimap/,
│       │   │   │                      TechPanel/, FleetPanel/, DiplomacyPanel/, TradePanel/,
│       │   │   │                      Notifications/, Objectives/, Overlays/, Lobby/, Replay/
│       │   │   ├── Net/               ClientConnection.cs, ClientReplicaHost.cs, NetGraphOverlay.cs
│       │   │   ├── Audio/             SoundDirector.cs, SettlementAmbience.cs
│       │   │   └── Debug/             SimInspector.cs, PathDebugDraw.cs, DesyncOverlay.cs
│       │   ├── Art/                   Models/ Materials/ Textures/ VFX/ Shaders/   (git-lfs)
│       │   ├── Audio/                 Music/ SFX/ Voice/                            (git-lfs)
│       │   ├── Prefabs/               Units/ Buildings/ Ships/ UI/ Fx/
│       │   ├── Scenes/                Boot.unity, MainMenu.unity, Lobby.unity, Match.unity,
│       │   │                          Sandbox_Prototype.unity
│       │   └── Settings/              URP assets, quality, input actions
│       └── Plugins/                   third-party (licences recorded in LICENSES.md)
│
├── content/                           ← authored source content (pre-compile)
│   ├── maps/                          *.map.json + heightmaps
│   └── balance/                       spreadsheets exported to packages/.../Data
│
├── tools/                             ← dev scripts
│   ├── build/                         build-server.sh, build-client.sh, package.sh
│   ├── ci/                            determinism-matrix.sh, run-tests.sh
│   └── dev/                           run-local-match.sh, run-two-clients.sh, netsim.sh
│
├── tests/                             ← non-code test assets
│   ├── replays/                       golden replays used by determinism CI
│   ├── maps/                          tiny deterministic test maps
│   └── fixtures/                      hand-built world states
│
└── .github/workflows/                 ci.yml, determinism.yml, unity-build.yml
```

---

## 4. Simulation data layout

### 4.1 Entity model — data-oriented, not OOP

Not Unity DOTS (that would re-couple the sim to the engine and to floats). A small,
purpose-built **structure-of-arrays** store:

```csharp
// Entity id: 24-bit index + 8-bit generation → safe stale-reference detection, 3 bytes on the wire
public readonly struct EntityId { public readonly uint Raw; }

// Components live in dense parallel arrays, indexed by a per-archetype dense index.
// Iteration is always in dense order → deterministic, cache-friendly, SIMD-friendly.
sealed class ComponentArray<T> where T : struct { T[] _data; int _count; /* … */ }
```

Component families (illustrative, not exhaustive):

| Family | Components |
|---|---|
| Common | `Transform2D` (Fix2 pos, tier, heading), `Owner`, `Health`, `Kind` |
| Worker | `Job`, `Carry`, `PathState`, `WorkSkill`, `Station` |
| Military | `CombatStats`, `Morale`, `Target`, `Stance`, `Formation` |
| Ship | `Hull`, `Sail`, `Crew`, `Cargo`, `GunBattery`, `Draught`, `Wake` |
| Building | `Footprint`, `Production`, `InputBuffer`, `OutputBuffer`, `Staffing`, `ConstructionSite` |
| Storage | `StorageContents`, `StorageFilter`, `StoragePriority` |
| Spatial | `SpatialCell`, `VisionSource` |

Archetype churn is avoided: entity kind is fixed at spawn, so an entity never changes archetype
mid-life (a captured ship changes `Owner`, not its archetype).

### 4.2 World state and snapshots

`SimWorld` owns every mutable array. Snapshotting is a **flat memcpy-style write of the dense
arrays** plus the id tables — no reflection, no graph walking. This makes snapshots fast enough to
take every 10 seconds on the server without a hitch, which is what makes reconnection cheap.

### 4.3 Fixed-point maths

`Fix64` — Q31.32 in a `long`. Range ±2.1e9 with 2.3e-10 precision; a 4 km map has ~0.2 nm
positional resolution, which is absurd overkill and therefore safe. `Sqrt` by integer Newton
iteration; `Sin`/`Cos` from a 4096-entry integer table with linear interpolation; `Atan2` from a
CORDIC-style integer routine. All are exhaustively unit-tested against reference values and are
identical on every platform because they are pure integer arithmetic.

---

## 5. Pathfinding and navigation

Pathfinding is the classic RTS performance killer. The design is layered so that the common case is
nearly free.

### 5.1 The navigation graph

- **Tiered grid.** The map is a grid of 1 m cells; each cell belongs to exactly one **terrain tier** (0–4, per `GAME_DESIGN.md` §10.1) and carries flags: walkable, buildable, water, deep water, shallow, forest, road level, occupied-by-building.
- **Tiers are separate nav layers.** A unit on Tier 2 cannot step onto Tier 3; the layers are joined only by **connector edges** (ramp, stair, bridge, rope bridge, lift, winch), each with a traversal cost, a **capacity** (units-in-transit limit) and, for lifts, a **batch cycle**.
- **Water is its own layer** with draught classes: shallow (all ships), medium, deep. Ship pathing runs on a coarser 4 m water grid.

### 5.2 Three-level pathing

| Level | Technique | Used for | Cost |
|---|---|---|---|
| **Strategic** | HPA* over a cluster graph (16×16 cell clusters, portal nodes, connector edges) | "Can I get from here to there, roughly how, and how far?" — job scoring, ship routes, AI planning | Cheap; cached per (cluster, cluster) pair |
| **Group** | **Flow fields** per (destination cluster, tier, movement class) | Any move order involving ≥ 4 units, and *all* worker hauling to a shared destination (warehouses are hot destinations, so their flow fields are permanently cached) | One field serves unlimited units — this is what makes 1,500 workers affordable |
| **Local** | Deterministic grid-reservation avoidance + a fixed-point ORCA-lite for dense crowds | The last few metres; unit-vs-unit jostling | Bounded per-agent work; disabled entirely under LOD |

**Budgeting:** path requests go into a priority queue with a **per-tick time budget** (e.g. 3 ms).
Overflow waits for the next tick; a waiting unit keeps its previous heading. Because the budget is
expressed in *work units* (nodes expanded), not milliseconds, it stays deterministic.

**Caching:** flow fields to storage buildings, dock queues and rally points are cached and
invalidated only when the grid changes (building placed/destroyed, connector cut). Grid changes are
rare and batched at the end of a tick.

**Path tokens** (see `MULTIPLAYER_ARCHITECTURE.md` §5.2) are what get replicated; because the client
holds the identical nav grid, it regenerates the same flow field and the same path.

---

## 6. Performance strategy

Targets: **15,000 entities, 8 players, ≤ 25 ms p99 server tick, 60 FPS client on a mid-range GPU.**

### 6.1 Simulation LOD

Not every worker needs full fidelity every tick. Three levels, assigned per district:

| Level | When | Behaviour |
|---|---|---|
| **L0 Full** | In any player's view, or in combat, or in the owner's active district | Every system, every tick, full local avoidance |
| **L1 Coarse** | Owned but unwatched districts | Movement integrates on a 4-tick cadence; local avoidance off; production still exact |
| **L2 Statistical** | Distant, uncontested, stable districts | Individual workers stop being stepped; the district resolves as a throughput equation calibrated against its L0 behaviour. Workers are respawned into exact positions the moment anything (a camera, an enemy, a raid) touches the district |

**Critical invariant:** LOD must never change the *economic outcome*, only the fidelity of motion. L2
throughput is derived from the same rates and haul distances, so a player watching or not watching
their own town does not change how much rum it makes. This is verified by a dedicated test class
(`TESTING.md` § LOD equivalence).

### 6.2 Other techniques

| Technique | Where |
|---|---|
| **Spatial hashing** (32 m buckets) | Target acquisition, proximity, vision, area damage, selection |
| **Time-slicing** | Vision (¼ of the vision grid per tick), job market (round-robin over districts), AI (one strategic decision per second) |
| **Event-driven production** | A production building schedules a wake-up tick instead of polling every tick |
| **Bitset fog** | Per-player visibility as bitsets; union/intersection ops instead of per-entity checks |
| **Zero allocation in the tick** | Pooled buffers everywhere; a CI test asserts `GC.GetAllocatedBytesForCurrentThread()` stays flat across 1,000 ticks |
| **Struct-of-arrays** | All hot components; enables tight loops without pointer chasing |
| **Object pooling (client)** | Every view GameObject, VFX, decal, UI row is pooled; nothing is `Instantiate`d in steady state |
| **GPU instancing / BatchRendererGroup** | Workers and ships drawn in a handful of draw calls, with impostors at distance |
| **Animation LOD** | Full skeletal near camera → baked vertex animation mid → billboard impostor far |
| **Job System / Burst (client only)** | Interpolation, culling, minimap and fog texture updates. **Never** in the simulation |

### 6.3 Where parallelism is allowed

Only in provably order-independent stages, with fixed-order merge: vision grid computation, flow
field generation, spatial hash rebuild, and (client-side) view interpolation. Job assignment, damage
and production are strictly single-threaded and ordered. This costs some throughput and buys
determinism; that is the correct trade for this project.

---

## 7. Content pipeline

- Content is **JSON authored by designers**, in `packages/com.brinehold.content/Data/`, human-diffable and reviewable in PRs.
- `Brinehold.Tools.ContentCheck` validates on every build: schema conformance, no dangling references, **production-chain closure** (every input is produced or harvestable somewhere), tech-prerequisite acyclicity, rank-unlock reachability, and balance sanity bounds.
- Content is compiled to a compact binary at build time and **hashed**; the hash is part of the handshake so a client with edited content cannot join.
- Maps are authored as heightmap + feature layers and compiled by `MapCompiler` into a deterministic binary containing the tier grid, nav layers, resource nodes, spawn points and strategic points.
- **No `ScriptableObject` for simulation data.** ScriptableObjects are Unity types and would drag the sim back into the engine. The client *may* wrap content in ScriptableObjects for art/prefab binding, but the rules read from the plain-C# content database.

---

## 8. Client architecture

```
 ClientConnection ──► ReplicaWorld (a SimWorld running in "replica" mode)
                             │
                             ├─ applies Tier A/B/D messages authoritatively
                             ├─ extrapolates intents locally between messages
                             ├─ Reconciler blends Tier C corrections (snap if > 2 m, else smooth over 150 ms)
                             │
                             ▼
                     EntityViewPool ──► UnitView / ShipView / BuildingView (pooled GameObjects)
                             │
                             ▼
                  Interpolation at render rate between sim tick N and N+1
```

- **The view never writes to the replica.** One-way data flow, enforced by an analyser rule.
- **Input produces commands**, which go to the server. Local optimistic feedback is visual only.
- **UI reads from the replica + private state.** Every number in the HUD is server-provided.
- **Replay mode** swaps `ClientConnection` for a `ReplaySource` feeding a *full* `SimWorld` — the same view code renders live matches and replays.

---

## 9. Build, CI and tooling

| Pipeline | Trigger | Does |
|---|---|---|
| `ci.yml` | every PR | `dotnet build` + `dotnet test` (Core, Sim, Net, Content, Integration), analysers (including the no-float rule and the assembly-dependency rule), content validation. **Target: under 5 minutes.** |
| `determinism.yml` | every PR + nightly | Replays the golden replay set on `ubuntu-x64`, `windows-x64`, `macos-arm64`; requires byte-identical state hashes at every checkpoint |
| `unity-build.yml` | main + tags | Unity client build (Windows), server container build, artifact upload |
| `loadtest.yml` | nightly | 8 bot clients × 90-minute match, asserts tick time, bandwidth, memory ceiling and zero errors |

`Directory.Build.props` sets: C# 12, `nullable enable`, `TreatWarningsAsErrors`, deterministic
builds, and the Brinehold analyser package.

---

## 10. Coding standards (simulation-specific)

1. No `float`/`double`/`Math`/`Mathf`/`Random`/`DateTime` in `Brinehold.Sim` or `Brinehold.Core.Math` — analyser `BH0001`.
2. No `UnityEngine` reference in any package except `Brinehold.Client` — analyser `BH0002` + a CI assembly-reference test.
3. No `foreach` over `Dictionary`/`HashSet` where the result affects simulation state — analyser `BH0003`.
4. No allocation inside `Tick()` — pooled buffers only; verified by an allocation test.
5. Every comparator used in a sim sort ends with an entity-id tie-break — code review checklist item.
6. Every system declares its read/write component sets in its class doc comment, so the schedule is auditable.
7. Public simulation APIs take and return values, not references into internal arrays.

---

## 11. Risk register

| # | Risk | Severity | Mitigation |
|---|---|---|---|
| T1 | Intent replication proves insufficient — clients drift visibly under load | High | Milestone 3 measures drift directly; fallback is raising Tier C rate for combat entities only (bandwidth headroom exists) |
| T2 | Fixed-point maths is slower than expected | Medium | `long` ops are cheap; benchmark in M1 before anything depends on it. Fallback: widen tick budget or reduce tick rate to 15 Hz |
| T3 | 1,500 workers/player is unaffordable even with LOD | High | LOD L2 is the designed answer; if it fails, reduce the design target to ~600 workers/player and lean harder on carts/wagons for throughput. Decision point: Milestone 8 |
| T4 | Unity's overhead for 3,000 visible entities on screen | Medium | BatchRendererGroup + impostors; measured in M4 with a stress scene before art is committed |
| T5 | Determinism regressions creep in | High | The determinism CI matrix runs on every PR, not nightly-only |
| T6 | Scope — the design is very large | High | The roadmap is explicitly incremental with a tiny prototype first; nothing beyond M3 starts until M3 passes its acceptance tests |
| T7 | Vertical logistics is confusing to players | Medium | The Logistics Overlay is a Milestone-8 deliverable, not a polish item |

---

*Related:* `MULTIPLAYER_ARCHITECTURE.md` · `DEVELOPMENT_ROADMAP.md` · `TESTING.md`
