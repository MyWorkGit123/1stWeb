# BRINEHOLD — Development Roadmap

**Status:** Proposed.
**Rule:** every milestone has **acceptance criteria**. A milestone is not "done" because the code
exists — it is done when its criteria are demonstrably met and its tests pass in CI. **Nothing from
milestone N+1 starts before milestone N passes its gate.**

Durations are rough estimates for a small team (2–4 engineers, 1 designer, art contracted from M13)
and are for sequencing, not commitments.

---

## Milestone map

```
M0  Architecture ──► M1 Foundations ──► M2 Network spine ──► ★ M3 PROTOTYPE ★ ──► M4 Hardening
                                                                                     │
        ┌────────────────────────────────────────────────────────────────────────────┘
        ▼
  M5 Client shell ──► M6 Rejoin/Replay/Spectate ──► M7 Naval ──► M8 Economy depth
        │
        ▼
  M9 Vertical ──► M10 Population ──► M11 Combat depth ──► M12 Tech & victory
        │
        ▼
  M13 Free ports/trade/diplomacy ──► M14 AI ──► M15 Scale ──► M16 Content & 1.0
```

---

## ★ M0 — Architecture and design (current)

**Goal:** agree the architecture on paper before a line of gameplay code exists.

**Deliverables**
- `GAME_DESIGN.md`, `MULTIPLAYER_ARCHITECTURE.md`, `TECHNICAL_ARCHITECTURE.md`, `ECONOMY_DESIGN.md`, `COMBAT_DESIGN.md`, `DEVELOPMENT_ROADMAP.md`, `TESTING.md`, `CHANGELOG.md`
- The full project folder structure, scaffolded in the repository
- The decisions register (`MULTIPLAYER_ARCHITECTURE.md` §14)

**Acceptance**
- [ ] Decisions D1–D10 signed off, or explicitly amended
- [ ] The prototype scope (M3) is agreed and frozen
- [ ] No gameplay code has been written

**Gate:** ✋ **STOP. Human review and sign-off required before M1.**

*Estimate: complete (this deliverable).*

---

## M1 — Deterministic foundations 🟡 **MOSTLY COMPLETE**

> Built and tested: `Brinehold.Core` and the `Brinehold.Sim` skeleton, the solution, build props and
> CI. **Outstanding:** the `BH0001`–`BH0003` analysers, a separate `Brinehold.Content` package with a
> JSON loader (prototype statistics currently live in `Brinehold.Sim/Content`), the allocation test,
> the `Fix64`-versus-`float` benchmark, and an actual run of the cross-platform hash matrix (the
> workflow is written but has not executed).

**Goal:** the bedrock the simulation stands on, with its determinism guarantees provable.

**Deliverables**
- `Brinehold.Core`: `Fix64` (Q31.32) with `Sqrt`/`Sin`/`Cos`/`Atan2`; `Fix2`/`Fix3`; `DeterministicRandom`; `DenseArray`, `IdTable`, `BitSet`, `RingBuffer`, `StableSort`; `BitWriter`/`BitReader` + quantisation
- `Brinehold.Sim` skeleton: `SimWorld`, `EntityStore`, `SimClock`, `ISimSystem`, `SystemSchedule`, the fixed tick loop
- `Brinehold.Content`: schema, JSON loader, content hashing, `ContentCheck` tool
- Analysers `BH0001` (no float in sim), `BH0002` (no `UnityEngine` outside the client), `BH0003` (no unordered iteration affecting state)
- `.sln`, `Directory.Build.props`, CI (`ci.yml`) running `dotnet test` in under 5 minutes

**Acceptance**
- [x] `Fix64` unit tests pass, including edge cases and reference-value comparison — 50 tests
- [ ] A 100,000-tick empty-world run produces an identical state hash on Windows, Linux and macOS-arm64 in CI — *workflow written, never run*
- [ ] Introducing a `float` into `Brinehold.Sim` **fails the build** — *analyser not built; the rule is currently convention only*
- [ ] `Fix64` arithmetic benchmark within 2× of `float` — *not measured. The full match benchmark at 0.071 ms/tick suggests it is not a problem, but risk T2 stays open until measured directly*
- [ ] Zero allocations across 1,000 ticks — *not tested*

---

## M2 — Network spine ✅ **COMPLETE**

> Every acceptance criterion below passes, over both the loopback and real UDP sockets. The socket
> transport was brought forward from M4 because without it nothing crossed a network interface.
> A dedicated Unity Transport adapter is still worth adding for the Unity client, but it is now an
> optimisation rather than a blocker: `IServerTransport` / `IClientTransport` are the seam, and the
> UDP implementation behind them is tested at 20% packet loss.

**Goal:** an authoritative server and a connected client exchanging validated commands and
replicated state — with no gameplay in it yet.

**Deliverables**
- `Brinehold.Protocol`: message schema + source-generated codecs + version handshake
- `Brinehold.Net`: `ITransport` (Unity Transport adapter, `LoopbackTransport`, `NetworkSimulator` for latency/jitter/loss), reliable-ordered / unreliable-sequenced / fragmented channels
- `Brinehold.Server`: `MatchHost` 20 Hz loop, session management, `ServerCommandGate` (validation + rate limiting), replication tiers A/B/D, `InterestManager` with a fog stub
- Client side: `ReplicationClient`, `ReplicaWorld`, `Reconciler`, netgraph overlay
- `Brinehold.Integration.Tests`: server + N in-process clients over loopback

**Acceptance**
- [x] Two clients connect to a headless server, exchange commands, and observe identical replicated state
- [x] An invalid command (bad ownership, bad id, out-of-bounds coordinate) is rejected and changes nothing
- [x] Command rate limiting stops a flooding client — 500 orders in one tick, over 400 dropped. *It throttles rather than disconnecting; kicking a persistent flooder is an M4 item*
- [x] Under 200 ms latency + 5% loss, state stays consistent and no channel stalls
- [x] Version and content mismatch are refused at handshake with a specific reason
- [x] Per-tier byte counters exist and are what the measurements in this document come from

---

## ★ M3 — First playable prototype 🟡 **SIMULATION AND NETWORKING COMPLETE; UNITY CLIENT UNVERIFIED** ★

**This is the scope frozen at M0. Nothing may be added to it.**

**Goal:** prove networking, ownership, worker simulation, economy, pathfinding, construction, combat
and synchronisation — at the smallest scale that can prove them.

**Content**
- 2 players, 1 small handcrafted map, separate starting areas
- 4 resources: **Wood, Food, Stone, Coin**
- 10 workers per player
- 5 buildings: **Warehouse, House, Lumber Camp, Fishing Wharf, Dock**
- 1 land unit: **Cutthroat**. 1 ship: **Cutter**
- Win condition: **destroy the opponent's Warehouse**

**Systems**
- RTS camera (pan, edge-scroll, zoom, rotate); single/drag/shift selection; right-click orders
- Fog of war, enforced by replication (not a client-side filter)
- Grid navigation + flow-field pathing, land and water
- Worker jobs: harvest, haul, construct
- Building placement with legality validation, construction sites, material delivery
- Resource storage in the Warehouse; population cap from Houses
- Basic combat: units attack units and buildings; server-resolved
- Minimal HUD: resource bar, population, selection panel, build menu, minimap

**Acceptance — the prototype is done when all of these are demonstrated**
- [x] Two clients + one headless server; both see an identical match from their own fog perspective
- [x] All resources, ownership, construction, damage and the win condition are decided **only** by the server
- [x] A packet capture proves **no data is sent about entities outside a player's vision** — asserted at the byte level on every commit
- [x] **Zero per-frame transform replication** — measured at 0 corrections and 34.6 B/s per client over ten minutes
- [x] Workers physically carry goods; destroying a Warehouse strands them
- [x] 200 ms latency + 5% packet loss: no desync, stall or state corruption
- [x] Tick cost far inside budget — 0.071 ms measured against a 5 ms target
- [x] A modified client sending illegal commands achieves **nothing** — 11 cheat-client tests over the real wire
- [x] A match runs between separate operating-system processes over real UDP — server plus two
      clients, verified by `tools/dev/run-networked-match.sh` and by 10 socket-level tests
- [ ] The UI says a stranded worker is stranded — needs the Unity client
- [ ] The manual test script passes on **two physical machines** — the process-level test passes on
      one machine; the same commands take a `--host` argument, but this has not been run on two
      machines

**Gate:** ✋ **STOP. Full playtest and review before any expansion. Expansion is one system at a time.**

---

## M4 — Hardening and confidence 🟢 **MOSTLY COMPLETE**

> Done: replay recording and playback, `ReplayCheck`, the golden corpus, and the three-platform
> determinism workflow. **Outstanding:** the workflow has not yet been observed running on GitHub,
> world snapshots for server-crash recovery, a dedicated load-test harness (`TestClient` is the
> seed), and structured logging and metrics.

**Goal:** make the prototype's guarantees permanent and machine-checked.

- Replay recording and playback (command log + header + state hashes)
- `Brinehold.Tools.ReplayCheck`; golden replay corpus
- `determinism.yml`: three-platform matrix on every PR
- Snapshot serialisation + the server snapshot ring
- `Brinehold.Tools.LoadTest`: N headless bot clients — *`Brinehold.Tools.TestClient` is the seed*
- Structured server logging, metrics, crash dumps
- Kick a persistently flooding client rather than only throttling it
- ~~UDP transport~~ — *done early, in M2*

**Acceptance**
- [ ] A recorded prototype match replays to identical state hashes on all three CI platforms
- [ ] A determinism regression introduced deliberately is caught by CI
- [ ] 8 bot clients sustain a 60-minute match within tick and memory budgets

---

## M5 — Client shell *(≈ 4 weeks)*

Proper camera (bookmarks, follow, minimap-drag), full selection model (double-click type-select,
ctrl-click, subgroup cycling), **control groups `Ctrl+0..9`**, idle-worker cycling, rebindable
hotkeys, notification feed, alert pings, the UI framework and layout system, settings.

**Acceptance:** every control listed in `GAME_DESIGN.md` §22 works and is rebindable; a player can
run the prototype without touching the mouse for orders they have hotkeys for.

---

## M6 — Reconnection, spectators, replays 🟡 **RECONNECTION AND REPLAYS DONE; SPECTATORS OUTSTANDING**

Full reconnection flow (snapshot + fast-forward, ≤ 15 s), disconnect grace window and AI takeover,
spectator sessions with the delayed-observer mode, the replay viewer UI (scrub, speed, fog toggle,
analysis overlays).

**Acceptance**
- [x] A client killed mid-match reconnects over real sockets and resumes with correct state
- [x] The match keeps running while the player is away, and their economy is intact on return
- [x] A forged or unknown token is refused; a stranger cannot take a disconnected player's slot
- [x] An expired grace window resigns the player
- [ ] Verified across twenty randomised drop points, including mid-combat and mid-construction
- [ ] Spectator sessions with the delayed-observer mode
- [ ] The replay viewer UI (scrub, speed, fog toggle) — needs the Unity client

---

## M7 — Naval core *(≈ 5 weeks)*

Water navigation layer with draught classes, ship movement with turn rates and wind, direct fleet
control, broadside arcs and reload, the three shot types, hull/sail/crew damage pools, boarding and
capture, fleet formations and stances, transport and disembarkation, the Fleet Panel.

**Acceptance:** a 6v6 ship engagement resolves identically on both clients; capture works and the
captured ship is fully controllable; ship replication stays within budget.

---

## M8 — Economy depth *(≈ 6 weeks)*

The full production-chain system, storage with filters and priorities, the job market, haul
throughput, Depots, roads and wagons, spoilage, building priorities, the **Logistics Overlay**, and
**simulation LOD (L0/L1/L2)**.

**Acceptance:** all chains in `ECONOMY_DESIGN.md` §3 run end to end; **LOD equivalence test passes**
(a district's output is identical whether simulated at L0 or L2); 1,000 workers per player within
tick budget.

---

## M9 — Vertical construction *(≈ 5 weeks)*

Terrain tiers, multi-layer navigation, all connectors (ramp, stair, bridge, rope bridge, cargo lift,
crane, winch tower) with capacity and batching, connector destruction and its consequences,
elevation effects on vision and range, the Elevation Overlay.

**Acceptance:** a settlement spanning four tiers functions; cutting a connector visibly and correctly
severs a district's economy; pathing across tiers is stable under load.

---

## M10 — Population and society *(≈ 4 weeks)*

Stations, housing tiers, needs, service coverage, Contentment, promotion and demotion, crime,
desertion, unrest and mutiny, taxation, the Contentment Overlay.

**Acceptance:** the rum-chain-collapse scenario produces unrest on the intended timeline; mutiny is
recoverable by both the garrison route and the supply route.

---

## M11 — Combat depth *(≈ 5 weeks)*

Full land roster, damage-type × armour-class table, morale (break, rout, rally), formations, stances,
terrain modifiers, fire and fire-spread, repair under fire, siege, building capture, Sappers vs
infrastructure, defensive structures, amphibious landing penalties.

**Acceptance:** the balance framework targets in `COMBAT_DESIGN.md` §10 are measurable in an
automated combat-sim harness; no matchup exceeds the 3× cap.

---

## M12 — Progression and victory *(≈ 4 weeks)*

Charter Ranks I–V with structural requirements, the four technology lines, research at buildings,
rank-advance announcements, all six victory conditions with public countdowns, the Objectives panel,
match settings for enabling conditions.

**Acceptance:** every victory condition can be achieved and correctly ends the match for all
players, spectators and the replay.

---

## M13 — Free ports, trade, diplomacy *(≈ 5 weeks)*

Neutral free ports with Relations, dynamic pricing, trade routes and interception, contracts,
mercenary recruitment, free-port capture and its Ledger penalty, Notoriety, the full diplomacy system
with telegraphed betrayal, tribute and shared vision.

**Acceptance:** a trade route earns, is interceptable, and its loss is correctly attributed; alliance
and betrayal flows work with all eight players.

---

## M14 — AI players *(≈ 6 weeks)*

`Brinehold.AI` running server-side, emitting ordinary commands, restricted to its own fog. Economic
planner, layout planner, build orders, scouting, military planner, naval planner, raid planner, the
five difficulty levels.

**Acceptance:** AI at "Captain" beats a competent human's opening and is beaten by a good player;
**no AI difficulty cheats on resources or vision** — verified by an automated audit of the AI's
command stream and information access.

---

## M15 — Scale and performance *(≈ 5 weeks)*

Meet the full performance targets: 8 players, ~15,000 entities, ≤ 25 ms p99 server tick, ≤ 25 KB/s
client bandwidth, 60 FPS client. Interest-management tuning, network compression, batched rendering
and impostors, memory ceilings, a 3-hour soak test.

**Acceptance:** all budgets in `MULTIPLAYER_ARCHITECTURE.md` §1 met on reference hardware, sustained
over a 3-hour soak with no leak and no drift.

---

## M16 — Content, art, audio and 1.0 *(open-ended)*

Full original art and audio, the map set, tutorial, lobby and matchmaking, accessibility pass,
localisation, telemetry, balance passes from playtest data, release engineering.

**Acceptance:** the 1.0 checklist (to be written at M15) is fully green.

---

## Working rules

1. **One system at a time.** After M3, each milestone adds one system and is playtested before the next begins.
2. **Never break what worked.** Removing or degrading working functionality requires a written reason in the PR and an entry in `CHANGELOG.md`.
3. **No claim without evidence.** "It works" means there is a passing test or a recorded demonstration. Anything unverified is described as unverified.
4. **The docs are the contract.** Behaviour that contradicts a design doc is a bug in one or the other; fix the doc first, then the code.
5. **Determinism is not negotiable.** A PR that breaks the determinism matrix does not merge, whatever it fixes.
6. **Every milestone ends with a report:** what was built, every file added or changed, and exact steps to test it.

---

## Parallelisation

With more than two engineers, these tracks are safely concurrent after M2:

| Track | Milestones |
|---|---|
| Simulation & economy | M8, M9, M10 |
| Networking & infrastructure | M4, M6, M15 |
| Client, UI & input | M5, and each milestone's UI surface |
| Combat & naval | M7, M11 |
| AI | M14 (needs M8 and M11 stable) |
| Content & tools | M12, M13, M16 |

M1–M3 are deliberately **not** parallelised: they are the load-bearing spine and are built by the
whole team, together, in order.
