# Changelog — Brinehold

All notable changes to this project are recorded here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versioning will follow [Semantic Versioning](https://semver.org/) once there is a build to version.

**Project status: the prototype plays a real match across separate processes over UDP, and records
replays that reproduce it exactly, survives a client disconnecting and rejoining, and runs on data-driven content.**
231 tests pass headlessly. The Unity client exists as source but has never been compiled — see
`unity/README.md`.

---

## [Unreleased]

### Added — M1 foundations and the M3 prototype simulation and network spine

**Status: 185 tests passing** via `dotnet test` — no Unity editor required, because the simulation
has no engine dependency.

#### `Brinehold.Core` — deterministic primitives (pure C#, no `UnityEngine`)
- `Fix64`, Q31.32 fixed point: 64×64→128 multiply decomposed into 32-bit halves, restoring long
  division with saturation, digit-by-digit `Sqrt`, ninth-order Taylor `Sin`/`Cos`, minimax `Atan2`.
  All integer arithmetic, so results are bit-identical on every platform.
- `Fix2` with an exact `MoveTowards` that lands on the destination instead of oscillating past it.
- `DeterministicRandom` (xorshift128+, SplitMix64 seeding) with capturable state.
- `EntityId` (24-bit index + 8-bit generation), `StateHash` (FNV-1a 64), `BitWriter`/`BitReader`
  with bounds-checked reads, and position/angle/ratio quantisers.

#### `Brinehold.Sim` — the authoritative game rules (pure C#, no `UnityEngine`)
- Structure-of-arrays `EntityStore` with generation-checked slot recycling.
- `NavGrid` and a deterministic A* `PathFinder`: integer costs, index tie-breaking, no corner
  cutting, and a node budget so one bad order cannot stall a tick.
- Nine systems in a fixed declared order: command ingest, movement, harvest, construction,
  production, combat, death, vision, victory.
- Physical goods — workers carry loads and a player's resource count rises only on deposit, so haul
  distance is a real cost and a destroyed warehouse strands the load rather than voiding it.
- Per-player fog of war computed inside the simulation, consulted at the replication boundary.
- Complete server-side command validation: ownership, liveness, affordability at the execution
  tick, population room, placement legality, target validity, selection size. Invalid commands are
  dropped and reported, never clamped.
- "Twin Coves" prototype map: mirrored bases, a southern sea, a central ridge with one gap.

#### `Brinehold.Protocol` and `Brinehold.Net` — the network spine
- Bit-packed wire format with defensive decoders; version and content hash checked at handshake.
- `ReplicationServer`: fog-filtered, five-tier replication. A player is never sent a byte about an
  entity they cannot see, so a modified client has nothing to reveal.
- `IntentExtrapolator`: the same movement reproduction code runs on the client and as a server-side
  shadow, so movement replicates as one intent message rather than a stream of transforms.
- `ReplicaWorld`: the client's presentation replica, including its own navigation grid updated with
  the footprints of buildings it can see.
- `LoopbackNetwork` with deterministic latency, jitter and loss for testing.

#### `Brinehold.Server` — headless authoritative host
- `MatchHost`: 20 Hz tick loop, session management, token-bucket rate limiting (40 commands/second),
  sequence-based replay protection. The player id on an incoming command is ignored entirely and
  filled in from the authenticated session.
- Console entry point with real-time and benchmark modes.

#### Measured (2 players, 10 workers each, 10 minutes of match time, Release build)
- **0.071 ms per tick** — 705× real time on one core.
- **34.6 B/s per client**, against the 8 KB/s prototype budget.
- **0 corrections** across the whole match: the client's extrapolation matches the server exactly.
- Idle match: ~25 B/s, which is only the keepalive and the economy refresh.

#### Bugs found and fixed by these tests
- The position quantiser truncated instead of rounding, doubling worst-case error and biasing every
  quantised position toward the map origin.
- `NearestPassable` returned the first cell in a search ring rather than the nearest, leaving
  workers standing outside their own warehouse's delivery reach.
- Eighteen job transitions changed an entity's job without emitting an intent, so clients kept
  walking workers the server had already stopped. This cost **13,271 corrections and 330 B/s** in a
  ten-minute match; routing every transition through `SetJobIfChanged` took it to **0 corrections
  and 34.6 B/s**. A regression test now guards the ratio.
- The private-economy stream was delta-only, so a client whose HUD was wrong stayed wrong forever.
  It now refreshes once a second and is self-healing.

#### `Brinehold.Client` and the Unity view layer
- `com.brinehold.client` (pure C#, unit tested): `SelectionModel` (click, box, shift, double-click
  type-select, idle-worker cycling), `ControlGroups` (Ctrl/Shift/recall on 0–9), `OrderIssuer` (the
  contextual right-click: harvest a tree, attack an enemy, otherwise walk), `CameraRig` (pan speed
  scaled by zoom, clamping, rotation), `HudModel` and `PlacementPreview`. Deliberately
  engine-independent so the mechanics players notice immediately can be tested without an editor.
- `unity/BrineholdClient` (**written, never compiled**): bootstrap and fixed-tick accumulator,
  terrain mesh builder with run-length merging, pooled entity views with interpolation, fog texture,
  camera and selection controllers, IMGUI HUD with a netgraph, and a minimap. `PrototypeSceneSetup`
  builds the entire scene from primitives at runtime, so there are no binary assets and the client
  runs from a single component on an empty scene.

#### UDP transport (brought forward from M4, because without it nothing crosses a network)
- `ReliableChannel`: 16-bit sequence numbers with wrap-safe comparison, a 32-bit rolling
  acknowledgement field, retransmission on timeout, an in-order delivery buffer, and duplicate
  rejection.
- `UdpServerTransport` / `UdpClientTransport`: one socket serves every client, non-blocking and
  drained once per tick; connect handshake retried until acknowledged; fragmentation and reassembly
  for messages above the MTU; connection timeouts; malformed datagrams dropped without allocation.
- `MatchHost` and `ClientConnection` now speak to `IServerTransport` / `IClientTransport`, so the
  same host binary runs over the loopback in tests and over UDP in production with no second path.
- The handshake now happens over the wire: a client introduces itself with a Hello, and the server
  assigns a player slot only if the protocol version and content hash match.
- `Brinehold.Tools.TestClient`: a headless client that connects over the network and plays a
  scripted opening. Two of these against a server is the two-machine test.

**Verified by running three separate operating-system processes:** a server and two clients
connected over UDP, were given distinct player slots, issued orders, and gathered resources, with a
matching state hash progression on the server and correct fog isolation between the two clients.

#### Bugs found by the networked tests
- Handshake refusals were computed but never transmitted, so a client with the wrong build version
  saw a silent hang instead of a reason. Refusals are now sent.
- Clients acknowledged received data only every 500 ms, so the server retransmitted a large share of
  a stream that had arrived perfectly well: **227 needless retransmissions in a 40-second match**.
  Clients now acknowledge within 25 ms, taking that to **0**.

#### Replays and the determinism gate (M4)
- Replay format: a match configuration plus the command stream, with a state fingerprint every 200
  ticks. Because the simulation is deterministic, replaying the commands reproduces the match
  exactly — **a ten-minute two-player match is 953 bytes**.
- `ReplayWriter` records every accepted command against the tick it will execute on, not the tick it
  arrived, so network delay cannot re-order a replay. Recording is always on.
- `ReplayPlayer` re-simulates and reports divergence *with the tick it first appeared on*, which is
  what a developer bisecting a determinism bug actually needs.
- `Brinehold.Tools.ReplayCheck`: verifies one replay or a whole directory, and `--break-at-tick N`
  stops on a chosen tick and dumps the world state there.
- The replay codec is deliberately independent of the wire format, so a protocol change does not
  invalidate the replay archive.
- Golden corpus in `tests/replays/`, verified by both the tool and the test suite.
- `determinism.yml` now runs the corpus on **ubuntu, windows and macos** — the macOS runner is
  arm64, so it also covers the cross-architecture case.

Tests prove the round trip end to end: a scripted match is played, recorded, replayed, and required
to match at every checkpoint and in its final economy. Falsifying a checkpoint is detected; dropping
a single command is detected; a truncated replay still parses what it recorded; and random bytes are
rejected rather than throwing.

#### Reconnection (M6, brought forward)
- A dropped player keeps their slot for a grace window (default three minutes) while **their
  settlement keeps running** — buildings produce, standing orders continue, and an opponent can
  attack them. A disconnect costs you the time you were away, never a free pause for everyone else.
- Reconnection needs no world snapshot. Because a client's replica only ever held what it was
  allowed to see, restoring it is a matter of the server *forgetting* what it believed the client
  knew: the next replication pass then re-sends that player's whole visible world and their economy,
  exactly as on a fresh join.
- Slots are reclaimed with a **reconnect token** issued at first join, so a stranger cannot take a
  disconnected player's seat, and a fresh joiner is refused while all slots are still claimed.
- When the grace window expires the player is resigned. Handing the settlement to an AI instead is a
  lobby setting once there is an AI to hand it to (M14).

#### Architecture guards
The coding standards in `TECHNICAL_ARCHITECTURE.md` are now enforced by tests rather than by
convention, reporting the offending file and line:
- **BH0001** — no `float`, `double`, floating-point `Math` members, `Mathf`, `System.Random`,
  wall-clock time or `Guid.NewGuid` anywhere in the simulation. Integer `Math.Min`/`Max`/`Abs` remain
  allowed because they are deterministic.
- **BH0002** — no `UnityEngine` reference outside the Unity client, which is what keeps the headless
  server and the sub-second test suite possible.
- **BH0003** — the simulation never iterates a hash-based collection, so allocation history cannot
  leak into game outcomes.
- Assembly dependency direction: `Brinehold.Sim` references only `Brinehold.Core`; `Brinehold.Core`
  references nothing of ours.
- Every `ISimSystem` appears exactly once in the declared tick schedule — no dead systems, no
  double-applied ones.

Each guard was verified by planting a violation and confirming it fails with the file and line, then
reverting.

#### Data-driven content (retires an M1 deliverable)
- `ContentDatabase` replaces the static stat tables: every cost, rate, health value and starting
  stock the simulation reads is now an injectable object. Systems read `world.Content`; nothing in
  the simulation hard-codes a number any more.
- `com.brinehold.content` loads that database from authored JSON. Content is written in decimal —
  a worker walks at `1.4`, not at `6012954214` raw fixed-point units — and converted to fixed point
  at one point in the loader, which is the only place a decimal touches the pipeline.
- The JSON reader is written rather than taken from a package: content loads once, outside the tick,
  so parsing speed is irrelevant, and having no third-party dependency avoids reconciling one
  between the .NET server build and the Unity import. It reports the **line** a problem is on, and
  it accepts `//` comments so a balance file can explain itself.
- Partial content is legal: state one field of one unit and everything else keeps the shipped value.
- **The database hashes itself, and that hash is part of the handshake.** A client that edits its
  content to make its own units cheaper cannot join — the server will not recognise the build.
- `ContentDatabase.Validate()` catches rulesets that load but are unplayable: nothing can train a
  worker, no building accepts deliveries, the first house costs more than a player starts with.
- `Brinehold.Tools.ContentCheck` runs that validation in CI and, with `--compare-default`, asserts
  the JSON and the code defaults agree — they must, because the defaults are the fallback when no
  content files are present.
- `ReplayCheck` now warns when a replay was recorded against different content, so a stale corpus
  cannot quietly pass as evidence about the current build.

#### Testing and tooling
- 231 tests: 50 core, 68 simulation, 38 client, 54 networked integration, 21 content. Covers real
  UDP sockets (including 20% packet loss and a match played to victory), reconnection over real
  sockets, replay round-trips, the golden corpus, and the content pipeline.
- Integration tests decode the actual bytes on the wire to prove fog enforcement, and drive a cheat
  client that forges player ids, orders enemy units, tampers with local state, floods commands,
  replays sequence numbers and sends malformed packets — all with no effect on the world.
- `.github/workflows/ci.yml`: build, test and a server smoke run, plus a three-platform determinism
  matrix.
- `tools/dev/run-local-match.sh`, `tools/dev/benchmark.sh`, `tools/ci/run-tests.sh`.

#### Not yet built or not yet verified
- **The Unity client has never been compiled.** It was written in an environment with the .NET SDK
  but no Unity editor. The logic it depends on is tested; the MonoBehaviour adapter layer is not.
- **Socket transport.** `LoopbackNetwork` is real, deterministic and tested, but nothing has crossed
  a network interface, so two-machine play is not possible yet (M4).
- The `BH0001`–`BH0003` analysers. The no-float and no-Unity rules are currently convention.
- A separate content package with a JSON loader; prototype statistics still live in `Brinehold.Sim`.
- The allocation test and the `Fix64`-versus-`float` benchmark.
- Reconnection, replays and spectating (M4/M6).

### Added — M0 architecture and design phase

- **`GAME_DESIGN.md`** — the design contract: concept, six design pillars, original setting (the Free
  Isles), core loop, match phasing, Company Doctrines, population Stations and Contentment, city
  building, vertical construction and connectors, worker simulation, the Charter Rank progression
  (Landfall → Stockade → Free Port → Marque → Admiralty), military and naval overviews, amphibious
  and economic warfare, exploration, free ports, diplomacy, six original victory conditions, modes
  and settings, AI difficulty model, the UI and control specification, accessibility commitments, art
  and audio direction, explicit v1.0 exclusions, and an open-questions register.
- **`MULTIPLAYER_ARCHITECTURE.md`** — evaluation of deterministic lockstep vs. snapshot replication,
  and the recommended hybrid: **Authoritative Deterministic Intent Replication (ADIR)**. Covers the
  20 Hz tick model, command pipeline and validation, five replication tiers, path tokens, interest
  management, fog-of-war enforced at the replication boundary, wire economy with a bandwidth model,
  fixed-point determinism rules, divergence detection, layered anti-cheat, reconnection, replays,
  spectators, hosting topology, the protocol summary, failure modes, and a ten-item decisions
  register awaiting sign-off.
- **`TECHNICAL_ARCHITECTURE.md`** — the rule that the simulation is pure C# with no `UnityEngine`
  dependency, and everything that follows from it: module map, the complete project folder structure,
  data-oriented entity storage, `Fix64` fixed-point maths, three-level pathfinding (HPA* → flow
  fields → local avoidance) over a tiered multi-layer nav graph, simulation LOD, performance
  techniques, content pipeline, client architecture, build/CI pipelines, simulation coding standards,
  and a technical risk register.
- **`ECONOMY_DESIGN.md`** — the complete original production-chain system: primary, refined, imported
  and abstract goods; the shipbuilding, rum, ordnance, food and construction chains with rates,
  inputs, outputs and worker slots; storage and housing tables; population needs and the work-rate
  formula; hauler-throughput mathematics with a worked distance table; vertical-throughput figures
  per connector and the cliff-top battery worked example; the job-market scoring model; free-port
  trade, dynamic pricing, routes, contracts and taxation; unrest as an offensive weapon; Charter Rank
  costs; and the deliberately tiny prototype economy subset.
- **`COMBAT_DESIGN.md`** — damage model with a full damage-type × armour-class matrix; the morale
  system (steady → shaken → wavering → routed, with rally); ten original land unit classes with
  first-pass statistics, roles, stances and formations; terrain modifier table; nine original ship
  classes with hull/sail/crew damage pools, shot selection, broadside arcs, wind, draught and
  shoals; boarding and prize-taking; fleet control and blockades; defensive structures and siege,
  including fire and building capture; amphibious operations; the balance framework; and the
  prototype combat subset.
- **`DEVELOPMENT_ROADMAP.md`** — seventeen milestones (M0–M16) with deliverables and explicit,
  testable acceptance criteria; the first playable prototype frozen at M3 with hard stop-gates before
  M1 and after M3; working rules ("one system at a time", "no claim without evidence", "never break
  what worked"); and a parallelisation plan.
- **`TESTING.md`** — seven test levels; unit, simulation-scenario and networked-integration coverage;
  the three-platform determinism matrix; the byte-level fog-enforcement regression test; the LOD
  equivalence test; the deliberate cheat-client harness; load, soak and performance budgets; security
  and fuzz testing; the replay-driven bug-reporting workflow; the per-feature definition of done; and
  the full M3 prototype manual test script.
- **`CHANGELOG.md`** — this file.
- **Project folder structure scaffolded** under `packages/`, `src/`, `unity/`, `content/`, `tools/`,
  `tests/` and `.github/workflows/`, with `README.md` signposts in each area. Directories are
  placeholders only — they contain no code.

### Notes

- The existing `index.html`, `css/` and `images/` files in this repository (an unrelated static-site
  starter) are untouched.
- **No gameplay, engine, networking or simulation code has been written.** Per the project brief and
  `DEVELOPMENT_ROADMAP.md` M0, implementation stops here pending review and sign-off of the ten
  architecture decisions in `MULTIPLAYER_ARCHITECTURE.md` §14.
