# BRINEHOLD — Multiplayer Architecture

**Status:** Proposed. Requires sign-off before any gameplay code is written.
**Scope:** Networking model, authority, replication, determinism, anti-cheat, reconnection, replays,
spectating, hosting topology, and the technology evaluation behind those choices.

> This is the document the whole project hangs on. Multiplayer is not a layer added later — the
> simulation is built as a headless authoritative server from commit one, and the Unity client is a
> *view* onto it. There is no single-player-first path.

---

## 1. Requirements

| # | Requirement | Source |
|---|---|---|
| R1 | 2–8 players, real-time, competitive | Brief |
| R2 | Server authoritative over resources, ownership, damage, production, tech, combat results | Brief |
| R3 | Cheat-resistant: no client-side resource/ownership/damage decisions; **no map hacks** | Brief |
| R4 | No desynchronisation, or detectable-and-recoverable divergence | Brief |
| R5 | Thousands of workers + hundreds of military units + fleets, simultaneously | Brief |
| R6 | Do not network every transform every frame | Brief |
| R7 | Reconnection after network loss, with state restoration | Brief |
| R8 | Spectators and replays | Brief |
| R9 | Match length 45–120 min without drift, leak or degradation | Brief |
| R10 | AI players use the identical ruleset and command path | Brief |

**Derived budgets** (targets, validated in `TESTING.md`):

| Budget | Target |
|---|---|
| Simulation rate | 20 Hz fixed (50 ms per tick) |
| Server tick cost, 8 players late game | ≤ 25 ms p99 (50% headroom) |
| Client downstream, steady state | ≤ 25 KB/s (200 kbit/s) |
| Client downstream, worst-case burst | ≤ 80 KB/s |
| Client upstream | ≤ 2 KB/s |
| Order-to-visible-response latency | ≤ 120 ms perceived (local prediction), ≤ 1 RTT authoritative |
| Reconnect-to-playing | ≤ 15 s on a 60 s dropout |
| Total entities in sim, 8p late game | ~15,000 |

---

## 2. The three candidate models

### 2.1 Deterministic lockstep (the classic RTS model)

Every client runs the full simulation. Only *commands* cross the wire. Clients advance a tick only
when all players' commands for that tick have arrived.

- ✅ Bandwidth is O(commands), independent of entity count. This is why classic RTS games can move 1,500 units on a 56k modem.
- ✅ Perfect consistency by construction (when determinism holds).
- ❌ **Every client has the full world state in memory ⇒ map hacks are trivially possible.** Fog is a rendering filter. This directly violates **R3**.
- ❌ **No server authority.** A modified client can lie about its own commands' legality; validation is by consensus, which is weak with 2 players.
- ❌ A single determinism bug desyncs the match irrecoverably (**R4**).
- ❌ Slowest player gates everyone; a lagging client stalls all eight.
- ❌ Reconnection is painful (must replay the whole command log from t=0).

### 2.2 Full server-authoritative snapshot replication (the shooter model)

The server simulates; it streams per-entity state snapshots to each client; clients interpolate.

- ✅ Full authority and fog-secure by construction — you simply do not send what a player cannot see (**R2**, **R3**).
- ✅ Trivial reconnection: send a snapshot.
- ❌ **Bandwidth scales with entity count.** 1,500 workers × 8 bytes × 20 Hz = 240 KB/s *per client* before any enemy entities. Violates **R5**/**R6** at RTS scale.
- ❌ Server CPU spent on per-client delta encoding of huge entity sets.

### 2.3 Recommendation — **Authoritative Deterministic Intent Replication (ADIR)**

The hybrid, and the model this project adopts.

```
  ┌────────────────────────── AUTHORITATIVE SERVER ───────────────────────────┐
  │  Deterministic fixed-point simulation, 20 Hz, single source of truth      │
  │  · validates every command    · resolves all combat and production        │
  │  · owns all resources/ownership    · computes per-player visibility       │
  └───────┬──────────────────────────────────────────────────┬───────────────┘
          │ per-player, fog-filtered replication stream      │
          ▼                                                  ▼
  ┌───────────────────────┐                        ┌───────────────────────┐
  │ CLIENT A              │                        │ CLIENT B              │
  │ Partial replica sim   │  commands ───────────► │ Partial replica sim   │
  │ (only what A can see) │                        │ (only what B can see) │
  │ Unity = view + input  │                        │ Unity = view + input  │
  └───────────────────────┘                        └───────────────────────┘
```

**The three ideas that make it work:**

1. **The server is the only simulation that matters.** It decides everything in R2. Clients never compute an authoritative outcome — they *display* one.

2. **Replicate intent, not transforms.** A worker walking 40 m across a settlement is not 60 position updates; it is **one message**: *"entity 8241 begins HAUL job: path token P, from tick 4120, speed 1.4 m/s, carrying 8 Planks."* The client's replica then advances that entity locally, deterministically, for the next 30 seconds at zero further bandwidth. This is what satisfies **R5** and **R6** simultaneously. Position sync becomes an occasional *correction*, not a stream.

3. **Fog of war is enforced at the replication boundary.** A client is never sent an entity it cannot see. There is no hidden state in client memory to hack out. When an entity leaves a player's vision, the server sends a "last known" freeze and stops updating it. This is the structural answer to **R3** that lockstep cannot give.

**The client replica is a *presentation* simulation.** It must be reproducible and smooth; it does not
need to be bit-identical to the server, because the server continuously corrects it. This is a crucial
simplification: we get the bandwidth profile of lockstep without inheriting lockstep's brittleness,
because a client divergence is a cosmetic error that self-heals on the next correction rather than a
match-ending desync.

**Determinism is still mandatory on the server**, for replays (**R8**), for server-side reproduction
of bug reports, and for CI verification. See Section 6.

| Requirement | How ADIR satisfies it |
|---|---|
| R2 authority | All state mutation happens only in the server sim |
| R3 anti-cheat | Fog-filtered replication; server-side command validation |
| R4 no desync | Server is single truth; client drift is corrected, not fatal |
| R5/R6 scale | Intent replication + interest management + LOD |
| R7 reconnect | Server holds snapshots; resync is a first-class message |
| R8 replays | Deterministic sim + canonical command log |
| R10 AI parity | AI runs inside the server sim and emits ordinary commands |

---

## 3. Technology evaluation

### 3.1 Engine

**Unity 6 LTS + C# — recommended, no compelling reason to deviate.**

Rationale: mature 3D pipeline; strong RTS-relevant rendering tools (GPU instancing, BatchRendererGroup,
Entities Graphics) for drawing thousands of units; excellent tooling and hiring pool; C# gives us one
language for client, server, tools and tests; first-party dedicated-server build target and headless
mode.

Considered and rejected: **Unreal 5** (C++/Blueprint, replication model is strongly
actor/transform-oriented and would fight ADIR; team-cost of C++ for a simulation-heavy title);
**Godot 4** (thinner 3D and networking ecosystem for a commercial RTS at this scale); **custom engine**
(cost cannot be justified).

### 3.2 Networking stack — the actual decision

| Option | Model | Verdict |
|---|---|---|
| **Netcode for GameObjects (NGO)** | `NetworkObject` + `NetworkTransform` per entity, RPC/NetworkVariable | **Reject.** Designed for small-scale co-op/shooters (tens of objects). Per-object overhead and the GameObject coupling make 15,000 entities impossible. |
| **Netcode for Entities (DOTS)** | Ghost snapshot replication with importance scaling and relevancy sets | **Reject as the core, keep as prior art.** Closest first-party fit and its relevancy system could express fog. But: it welds the simulation to Unity + Burst (no plain-.NET headless server, no simulation unit tests outside the editor), it is float-based (cross-platform determinism is not contractually guaranteed — see 6.1), and it is architected around client-predicted physics for FPS-scale ghost counts. We would fight it on every axis. |
| **Mirror** | GameObject-centric, community-maintained | **Reject.** Same scaling ceiling as NGO with less first-party support. |
| **FishNet** | GameObject-centric, better perf than Mirror, good prediction | **Reject.** Genuinely good middleware, wrong shape: still object/transform replication, still Unity-coupled. |
| **Photon Fusion 2** | Shooter-oriented, hosted or client-host | **Reject.** Entity budgets and CCU-based pricing are wrong for a 90-minute 8-player RTS with 15,000 entities. |
| **Photon Quantum 3** | Deterministic ECS, fixed-point math, predict/rollback, server-verified | **Serious alternative — reject, with reasons recorded.** It is purpose-built for deterministic RTS/fighting games and would save months. Rejected because: (a) its model is *client-side simulation of the full world*, which reintroduces the map-hack surface we are specifically trying to close; (b) closed-source middleware on a per-CCU licence for a title expected to run 90-minute sessions; (c) vendor lock-in on the single most load-bearing system in the project; (d) our fog-filtered intent replication is not something the framework wants to do. **Revisit if** schedule pressure becomes existential — this is the pre-approved fallback. |
| **Custom sim + Unity Transport (UTP)** | Pure-C# deterministic sim; UTP for UDP, DTLS, reliable/unreliable pipelines | ✅ **RECOMMENDED.** |
| *(alt transport)* **LiteNetLib / ENet-CSharp** | Lean UDP with reliability channels | Kept behind `ITransport`; LiteNetLib is the designated fallback if UTP's pipeline model gets in the way. |

**Decision: custom simulation + custom replication protocol over Unity Transport, behind an
`ITransport` interface.**

The simulation is a **pure C# library with zero `UnityEngine` references**. This is the single most
important structural decision in the project, and it buys:

- a **real headless dedicated server** as a plain .NET 8 console app (cheap to containerise, ~50 MB RAM baseline, no graphics stack, no Unity licence on the fleet);
- **simulation unit tests that run in `dotnet test` in CI in seconds**, no Unity editor, no play mode;
- **fixed-point determinism** we control completely (Section 6);
- **replay verification as a CLI tool**;
- the ability to swap the renderer later without touching game rules.

### 3.3 Supporting services

Kept behind interfaces so they are replaceable and so the prototype needs none of them:

- **Lobby / matchmaking:** Unity Gaming Services Lobby + Matchmaker (evaluate), or a small custom service. Prototype uses direct IP + a local lobby.
- **NAT traversal:** UGS Relay as a fallback path for players who cannot reach a dedicated server; not the primary path.
- **Game server hosting:** UGS Multiplay / Game Server Hosting, or plain containers on any cloud. One **process per match**, not a shared world server.

---

## 4. Simulation and time model

### 4.1 Ticks

| Concept | Value |
|---|---|
| **Simulation tick** | Fixed 50 ms (20 Hz). Never variable, never frame-coupled. |
| **Tick number** | `uint` from 0; wraps after ~6.8 years of continuous play (non-issue). |
| **Render rate** | Uncapped/vsync on the client; the view **interpolates between the last two sim states**. |
| **Slow tick** | Every 20 ticks (1 s): contentment, needs, promotion, trade income, free-port relations, AI strategic layer. |
| **Very slow tick** | Every 200 ticks (10 s): migration, unrest events, weather, district recomputation. |

20 Hz is chosen deliberately: RTS units do not need 60 Hz physical fidelity; it quarters server CPU
and network volume versus 60 Hz; and 50 ms is below the perceptual threshold for command
responsiveness once the client interpolates and predicts order feedback.

### 4.2 Command pipeline

```
 player input ──► client validates locally (affordability, legality) — advisory only
                     │
                     ├──► optimistic UI feedback (order marker, "acknowledged" sound)
                     │
                     └──► Command{playerId, seq, issueTick, payload} ──► SERVER
                                                                            │
                        server: authenticate → validate → schedule at tick T ┤
                                                                            │
                     ◄──── CommandAccepted{seq,T} | CommandRejected{seq,reason} ◄┘
                                                                            │
                                                    executed in sim at T ───┤
                                                                            │
                     ◄──── resulting intents / events / state deltas ◄──────┘
```

- Commands carry a **per-player monotonic sequence number** (replay protection + ordering).
- The server schedules an accepted command for the **next tick boundary after arrival**, `T = max(currentTick + 1, arrivalTick + 1)`.
- **Client-side prediction is cosmetic only.** The client may immediately show a move order marker and start a unit's walk animation, but it never deducts a resource, never spawns a unit, and never applies damage. When the authoritative intent arrives, the client snaps or blends to it.
- **Rejections are explicit and surfaced** ("not enough Planks", "blocked terrain", "not your unit").

### 4.3 Latency and fairness

Because the server is authoritative (not lockstep), a player's command latency is their own one-way
trip — a laggy player does not stall anybody else. This is a major playability win over lockstep.

For **ranked** play, an optional **Fair Delay** mode buffers all players' commands to a common
execution delay (`max(RTT)/2` clamped to 50–250 ms, recomputed slowly) so that a player on a fibre
line has no mechanical advantage over one on DSL. Off by default in casual, on in ranked. This is a
lobby setting, not an architecture change.

### 4.4 Determinism-critical ordering

Within a tick, systems run in a **fixed, declared order**, each iterating entities in **dense-array
order** (stable, insertion-ordered ids). No hash-map iteration ever affects simulation state.

```
Tick(T):
  1. Ingest scheduled commands (sorted by playerId, then seq)
  2. Order/intent resolution      6. Combat resolution (land, then naval, then siege)
  3. Job market assignment        7. Damage/death application
  4. Movement & pathing           8. Construction & repair progress
  5. Production & consumption     9. Vision & fog recomputation
                                 10. Slow-tick systems (if T % 20 == 0)
                                 11. Replication pass (per player)
                                 12. State hash (if T % 200 == 0)
```

---

## 5. Replication design

### 5.1 Five replication tiers

| Tier | Content | Channel | Rate | Filter |
|---|---|---|---|---|
| **A — Lifecycle** | Entity spawn/despawn, ownership change, type, capture, building placement/completion/destruction, connector state | Reliable ordered | On change | Fog |
| **B — Intent** | "Entity E begins behaviour X at tick T with parameters P" (move along path token, harvest node N, haul goods G from A to B, attack target, produce item, sail route) | Reliable ordered | On change (avg ≈ every 8–15 s per worker) | Fog |
| **C — Correction** | Quantised position/heading/state for entities the client is *looking at* or that are in combat | Unreliable sequenced | 4 Hz, and only when divergence > threshold | Fog + camera/interest |
| **D — Private** | Own resources, storage contents, production queues, tech progress, contentment, population, alerts | Reliable ordered, delta | 5 Hz | Owner only |
| **E — Aggregate** | Distant/LOD districts as summary ("Cliffside: 47 workers, 3 buildings, activity 0.6"), enemy fleet contact markers at extreme range | Unreliable sequenced | 1 Hz | Fog + distance |

**Nothing is sent per-frame. Nothing streams transforms continuously.** Tier C exists to fix drift,
not to drive motion, and the server skips a correction entirely when it can prove the client's
extrapolation is within tolerance (it runs a cheap shadow-extrapolation of each replicated intent).

### 5.2 Path tokens

Sending a full path (dozens of waypoints) per move order would defeat the point. Instead:

- The server computes the path with the **deterministic pathfinder** (`TECHNICAL_ARCHITECTURE.md` § Pathfinding).
- It sends a **path token**: `(startCell, goalCell, flowFieldId | corridorId, startTick, speedClass)`.
- The client, holding an identical navigation graph (it is derived from the map, which every client has), **recomputes the same path deterministically** from the token.
- Typical cost: **12–16 bytes** instead of 200+.

Where a path cannot be reproduced from a token (dynamic re-route around a newly placed building the
client cannot see), the server falls back to sending an explicit short waypoint list, or simply lets
Tier C corrections carry it.

### 5.3 Interest management

Per client, per tick, the server maintains an **interest set**:

```
interest(player) =
      entities visible to player            (fog: authoritative visibility grid)
    ∪ entities in player's camera frustum   (client reports camera AABB at 2 Hz, advisory only)
    ∪ entities in an active combat with player
    ∪ own entities (always)
```

- Visibility is computed on a coarse **vision grid** (4 m cells) with per-player bitsets, updated in the vision system, not per-entity ray tests.
- **Entering interest** ⇒ Tier A spawn + Tier B current intent (a "catch-up" packet).
- **Leaving interest** ⇒ a `LostSight` message; the client keeps a greyed **last-known** ghost for buildings/structures, and *deletes* mobile units after a short fade. Servers never send updates for entities out of interest — this is the anti-map-hack guarantee.

### 5.4 Wire economy

- **Entity ids:** 24-bit index + 8-bit generation, packed to 3 bytes on the wire where a full id is needed; per-interest-set **local index remapping** to 2 bytes for hot messages.
- **Positions:** world quantised to 5 cm on X/Z (16-bit each within a 3.2 km map region), tier index (3 bits) + local height delta (5 bits) for Y. **6 bytes**, not 12.
- **Headings:** 8 bits (1.4° resolution).
- **Bit-packing** with a shared field-width schema; no JSON, no reflection-based serialisers, hand-written or source-generated codecs.
- **Delta compression** for Tier D against the last acknowledged baseline.
- **Batching:** one datagram per client per tick containing all tiers, target MTU 1200 bytes, fragmenting only for snapshots.
- **Compression:** none by default (bit-packing already dominates); optional LZ4 on snapshot payloads.

### 5.5 Bandwidth model (8 players, late game, worst case per client)

| Tier | Estimate | Working |
|---|---|---|
| A Lifecycle | ~0.6 KB/s | ~40 events/s × 14 B |
| B Intent | ~2.6 KB/s | ~1,700 own + ~300 visible enemy entities re-tasking every ~10 s ⇒ ~200/s × 13 B |
| C Correction | ~9.6 KB/s | ~400 entities in view/combat × 4 Hz × 6 B |
| D Private | ~0.5 KB/s | 5 Hz delta of economy state |
| E Aggregate | ~0.2 KB/s | 1 Hz district/fleet summaries |
| Command echo | ~0.3 KB/s | Other players' *visible* actions |
| **Total** | **≈ 14 KB/s (112 kbit/s)** | Inside the 25 KB/s budget with headroom |

Upstream per client: commands only, **≈ 0.3–1.5 KB/s** even at 300 APM.

For comparison, naïve full-transform replication of the same match would be **≈ 1.8 MB/s per client**.
That factor of ~130 is the whole justification for intent replication.

---

## 6. Determinism

The server simulation is deterministic. This is required for replays, CI verification, and
reproducing bug reports from a command log.

### 6.1 No floating point in the simulation, ever

IEEE-754 does not guarantee identical results across CPU architectures, compilers, JIT versions and
SIMD paths (FMA contraction, x87 80-bit intermediates, differing `Math` library implementations).
Burst improves this but does not contract for bit-identical cross-platform results.

**The simulation uses `Fix64`: a Q31.32 fixed-point type backed by `long`**, with deterministic
`Sqrt`, `Sin`, `Cos`, `Atan2` implemented as integer algorithms / lookup tables. A Roslyn analyser
(`BH0001`) **fails the build** if `float`, `double`, `System.Math`, `UnityEngine.Mathf`,
`Random`, `DateTime.Now`, or `Guid.NewGuid` appear anywhere in the simulation assembly.

Floats remain perfectly fine in the **view layer** (rendering, camera, VFX, interpolation) — nothing
there feeds back into the simulation.

### 6.2 Determinism rules

1. **No wall-clock time.** The sim's only clock is the tick counter.
2. **One PRNG per simulation**, seeded from the match seed, advanced in tick order. Never a per-system or thread-local RNG.
3. **No unordered iteration.** Entity storage is dense arrays; any lookup structure is used for *finding*, never for *iterating* in a way that affects state.
4. **Stable sorts only**, with entity id as the final tie-break in every comparator (job assignment, target selection, path priority).
5. **No parallelism inside a tick** unless the work is provably order-independent and results are merged in a fixed order (allowed: vision grid computation, path *pre*-computation; forbidden: damage application, job assignment).
6. **No `string` hashing** in simulation logic (`string.GetHashCode` is randomised per process by default).
7. **Content data is versioned and hashed**; the match refuses to start if clients disagree on the content hash.

### 6.3 Divergence detection

- Every 200 ticks (10 s), the server computes a **64-bit FNV-1a state hash** over all simulation-relevant fields and appends it to the replay log.
- CI runs the same replay on Windows/Linux/macOS and on x64/ARM64 and requires identical hashes at every checkpoint (`TESTING.md` § Determinism).
- Clients compute a hash of their **private** state (Tier D) and send it every 200 ticks; a mismatch triggers a targeted resync of that player's economy state and a telemetry event. This catches replication bugs early without being fatal to the match.

Note the asymmetry: because the server is authoritative, a client divergence is a **correctable
error**, not a match-ending desync. That is the whole reason we are not building lockstep.

---

## 7. Anti-cheat

Layered, with the structural layers first because they are the only ones that actually work.

### 7.1 Structural (unbypassable by design)

| Attack | Mitigation |
|---|---|
| **Map hack** | The server never sends what the player cannot see. There is nothing in client memory to reveal. |
| **Resource hack** | Resources exist only in server memory. The client's copy is display-only. |
| **Instant build / free units** | Costs are deducted and timers advanced only by the server. |
| **Damage / one-shot hacks** | All damage is computed server-side from server-owned stats. |
| **Unit theft** | Every command is checked against the server's ownership table. |
| **Tech unlock hack** | Tech state is server-owned; the client's tech UI is a mirror. |
| **Production hack** | Queues advance in the sim only. |
| **Teleport / speed hack** | The client cannot move entities; it can only request. Tier C corrections overwrite any local tampering within 250 ms. |

### 7.2 Command validation (every command, every time)

Ownership · entity liveness · affordability (at the execution tick, not the issue tick) · tech
prerequisite · placement legality (terrain, footprint, collision, connectivity, territory) ·
build-queue capacity · population cap · cooldowns · target validity and range · **command rate
limiting** (a hard cap of ~40 commands/second per player, plus a token bucket) · payload sanity
(bounds-checked ids and coordinates) · sequence-number replay protection.

An invalid command is **dropped and logged**, never clamped into something valid. Repeated invalid
commands from one client raise a telemetry flag and, past a threshold, kick.

### 7.3 Behavioural and operational

Server-side telemetry for statistically implausible play (perfect fog-edge dodges, reactions faster
than the player's own RTT, inhuman APM distributions), match logs retained for review, replay-based
investigation. Optional client integrity attestation for ranked play, understood to be a speed bump
rather than a wall.

**Explicit non-goal:** kernel anti-cheat. The architecture is designed so that the cheats that
matter in an RTS (map hack, resource hack) are impossible rather than detectable.

---

## 8. Reconnection and state restoration

The server keeps, per match:

- a **snapshot ring buffer**: a full serialised simulation state every 200 ticks (10 s), retaining the last 30 (5 minutes);
- the **complete command log** since t=0 (small — a whole match is typically < 2 MB).

```
Client drops
   │  server keeps simulating; the player's units keep executing standing orders,
   │  buildings keep producing. A "DISCONNECTED (2:41 remaining)" badge shows to everyone.
   │
   ├── reconnect within the grace window (default 180 s, lobby-configurable)
   │     1. Re-authenticate to the same match + player slot (session token)
   │     2. Server sends the newest snapshot, fog-filtered for that player  (~200–600 KB, LZ4, fragmented)
   │     3. Server fast-forwards the client from snapshot tick to current tick with the delta stream
   │     4. Client rebuilds its replica and resumes.  Target: < 15 s end to end
   │
   └── window expires ⇒ player is resigned; their settlement either goes neutral-derelict
         or is handed to an AI, per lobby setting.
```

Reconnection is a **first-class message flow implemented in Milestone 6**, not an afterthought —
snapshot serialisation is written at the same time as the state itself, because it is also what
saves, replays and spectators use.

---

## 9. Replays and spectating

### 9.1 Replays

A replay is `header + command log`, replayed through the deterministic simulation.

```
BRHR (magic) | format version | engine+content hash | map id + seed | match settings
             | player roster (name, slot, doctrine, colour)
             | [ tick, playerId, seq, commandPayload ] ...
             | [ tick, stateHash ] every 200 ticks
```

Typical size: **1–3 MB for a 90-minute 8-player match** — because it is commands, not state. This is
why determinism is worth its cost.

Replay features: full fog reveal or per-player fog, free camera, jump to tick (via the nearest
snapshot, then fast-simulate forward), variable speed, and an analysis overlay (resource graphs, APM,
production timelines). The replay tool is the **same simulation binary** the server runs — if a
replay diverges from its recorded hashes, that is a determinism bug and CI fails.

### 9.2 Spectators

Spectators connect as a distinct identity with no command rights. Two modes:

- **Observer** — sees everything (casting, LAN events, friendly games).
- **Delayed observer** — the same stream on a configurable delay (default 180 s) so that spectators cannot be used as a ghosting channel in competitive play. This is the default whenever any spectator slot is open on a ranked match.

Spectators are served from the same replication system with a synthetic "sees everything" visibility
set, so they cost the server one extra replication pass and nothing else.

---

## 10. Hosting topology

```
   ┌──────────┐   ┌────────────┐   ┌──────────────┐
   │  Client  │──►│  Lobby svc │──►│ Matchmaker / │        (all optional; behind interfaces)
   └──────────┘   └────────────┘   │  allocator   │
        │                          └──────┬───────┘
        │  direct UDP (DTLS)              │ spawns
        ▼                                 ▼
   ┌───────────────────────────────────────────────┐
   │  brinehold-server  (one process per match)    │
   │  .NET 8 · headless · no Unity · ~1 vCPU/match │
   └───────────────────────────────────────────────┘
```

- **Dedicated server** is the primary and default path for ranked/public play.
- **Listen mode** for LAN, custom games and development: the client launches the *same server binary* as a child process on `127.0.0.1` and connects to it as an ordinary client. There is no second code path and no "host advantage" — the host is a client like everyone else. This is also how the prototype runs.
- **Relay** (UGS or self-hosted) only as a NAT-traversal fallback.
- Server is stateless between matches; a crash loses one match, never a persistent world.
- Resource envelope target: **≤ 1 vCPU and ≤ 512 MB RAM per 8-player match**, so a modest fleet hosts hundreds of concurrent matches.

---

## 11. Protocol summary

Three channels over UDP (DTLS in production):

| Ch | Type | Carries |
|---|---|---|
| **0** | Reliable ordered | Handshake, lobby, match config, commands (C→S), Tier A/B/D, control |
| **1** | Unreliable sequenced | Tier C corrections, Tier E aggregates, pings, camera hints |
| **2** | Reliable fragmented | Snapshots (join / reconnect), map data, replay chunks |

**Client → Server:** `Hello`, `JoinMatch`, `Ready`, `Command`, `CameraHint`, `PrivateStateHash`,
`Ping`, `RequestResync`, `Chat`, `DiplomacyProposal`, `Resign`, `PauseVote`.

**Server → Client:** `Welcome`, `MatchConfig`, `MatchStart`, `TickHeader`, `CommandAccepted`,
`CommandRejected`, `SpawnEntity`, `DespawnEntity`, `SetIntent`, `Correction`, `PrivateDelta`,
`Aggregate`, `LostSight`, `Event` (combat/economy/alert), `Snapshot`, `PlayerStatus`,
`DiplomacyUpdate`, `MatchEnd`, `Pong`.

Every message begins with a 1-byte type tag; the codecs are source-generated from a schema in
`packages/com.brinehold.protocol/Schema/` so client and server can never drift. The protocol carries
a **version number checked at handshake**; mismatched builds are refused with a clear message.

---

## 12. Failure modes and how we handle them

| Failure | Handling |
|---|---|
| Player packet loss | Reliable channel retransmits; Tier C loss is harmless (next correction supersedes) |
| Player high latency | Only that player's own commands are delayed; nobody else is affected |
| Player disconnects | Grace window, then AI takeover or resignation (Section 8) |
| Server tick overrun | Server never drops ticks; it reports overrun telemetry and sheds LOD work first (Tier E, aggregate districts), then correction frequency |
| Server crash | Match lost. Mitigation: snapshot ring persisted to disk every 30 s; an operator can restore a match in dev/LAN. Not promised to players at 1.0 |
| Client replica drift | Detected by private-state hash and Tier C tolerance; corrected, telemetered |
| Content/build mismatch | Refused at handshake with an explicit version error |
| Malicious client | Section 7 |

---

## 13. What the prototype must prove (Milestone 3)

The prototype's job is to validate this architecture end to end at tiny scale, not to be fun:

1. Two clients + one authoritative headless server, on one machine and across a LAN.
2. Commands are validated server-side; a hacked client that sends illegal commands changes nothing.
3. 10 workers per player gather, haul and build using intent replication — with **zero per-frame transform traffic**, verified with a packet counter in the netgraph overlay.
4. Fog of war is enforced by replication: a packet capture must show **no data at all** about entities the player cannot see.
5. Resources, construction, combat and the win condition all resolve identically on both clients because both are reading the same server.
6. Deliberate 200 ms latency and 5% packet loss (via the built-in network simulator) do not desync, stall or corrupt state.
7. A client killed and restarted mid-match reconnects and resumes (a stretch goal for M3, required by M6).

---

## 14. Decisions register

| # | Decision | Status |
|---|---|---|
| D1 | Authoritative server, never peer-to-peer lockstep | **Proposed** |
| D2 | Intent replication + interest management, not transform streaming | **Proposed** |
| D3 | Simulation is pure C# with no `UnityEngine` dependency | **Proposed** |
| D4 | Fixed-point `Fix64` maths; floats banned in sim by analyser | **Proposed** |
| D5 | 20 Hz fixed simulation tick | **Proposed** |
| D6 | Unity 6 LTS as the client view layer | **Proposed** |
| D7 | Unity Transport behind an `ITransport` seam; LiteNetLib as fallback | **Proposed** |
| D8 | One server process per match; listen mode uses the same binary | **Proposed** |
| D9 | Replays are command logs; the replay player is the server sim | **Proposed** |
| D10 | Photon Quantum is the pre-approved fallback if D1–D3 prove too slow to build | **Proposed** |

**All ten need sign-off before Milestone 1 begins.**

---

*Related:* `TECHNICAL_ARCHITECTURE.md` (how the simulation is built) · `TESTING.md` (how each claim
here is verified) · `DEVELOPMENT_ROADMAP.md` (when each piece lands).
