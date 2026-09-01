# BRINEHOLD — Testing Strategy

**Status:** Proposed.
**Principle:** *no claim without evidence.* A feature is "working" when a test proves it or a
recorded demonstration shows it. Anything else is described as unverified, in the PR and in the
milestone report.

---

## 1. Why testing is unusually load-bearing here

Three properties of this project make normal manual QA insufficient:

1. **Determinism** must hold across platforms and forever. A regression is invisible until a replay or a match breaks hours later.
2. **The server is authoritative**, so a whole class of bugs (client trusting itself) is only detectable by *actively attacking our own server*.
3. **Simulation LOD** is allowed to change fidelity but never outcomes — an easy place for silent economic drift.

Each gets a dedicated test class below.

---

## 2. Test levels

| Level | Runner | Runs in | Speed |
|---|---|---|---|
| **L1 Unit** | xUnit, `dotnet test` | Every PR | seconds |
| **L2 Simulation scenario** | xUnit + fixture worlds | Every PR | seconds |
| **L3 Integration (networked)** | xUnit, server + N in-process clients over `LoopbackTransport` | Every PR | < 2 min |
| **L4 Determinism matrix** | Replay corpus × 3 OS × 2 CPU arch | Every PR | ~5 min |
| **L5 Load / soak** | `Brinehold.Tools.LoadTest`, headless bots | Nightly | 1–3 h |
| **L6 Client (Unity)** | Unity Test Framework, play-mode | Nightly + pre-release | minutes |
| **L7 Manual** | Scripted human test passes | Every milestone gate | hours |

Because the simulation has no Unity dependency, **L1–L5 all run in plain .NET** — fast, headless,
and cheap enough to run on every push. This is the single biggest practical payoff of the
architecture in `TECHNICAL_ARCHITECTURE.md` §1.

---

## 3. L1 — Unit tests

**Fixed-point maths** — the highest-risk primitive in the codebase:
- Exhaustive edge cases: min/max, overflow saturation, negative zero, rounding at halfway points
- `Sqrt`, `Sin`, `Cos`, `Atan2` compared against high-precision references within a declared error bound
- Round-trip conversion, and identities (`sin² + cos² = 1` within bound)
- **Property test:** for random operand pairs, `a op b` computed twice in different orders where the operation is commutative gives bit-identical results

**Collections:** `DenseArray` add/remove/compact preserves ordering guarantees; `IdTable` generation
counters correctly reject stale ids; `StableSort` is stable and total.

**Serialisation:** every quantiser round-trips within its declared precision; `BitWriter`/`BitReader`
round-trip every message type (property-based, generated payloads).

**Content:** every JSON definition loads; every reference resolves; content hash is stable across
runs and machines.

---

## 4. L2 — Simulation scenario tests

Hand-built worlds, run for N ticks, assert on the outcome. Fast, deterministic, and the main
regression net for game rules.

Examples of the required coverage:

| Area | Example assertions |
|---|---|
| **Economy** | A Lumber Camp + Sawmill + hauler at 60 m produces 8 Planks/min ± tolerance. A blocked output buffer stalls production and reports `Starved`. Spoilage outside a Granary is 1%/min |
| **Logistics** | Haul throughput matches the formula in `ECONOMY_DESIGN.md` §6.1 to within 5%. A destroyed connector re-routes or correctly strands a district |
| **Jobs** | The job market assigns identically given identical world state, regardless of insertion order |
| **Pathing** | A path exists iff the nav graph says so. A blocked path fails cleanly. Path budget overflow defers rather than corrupting |
| **Combat** | Every cell of the damage-type × armour-class table applies. Armour floor holds. Morale break/rout/rally transitions fire at the right thresholds |
| **Naval** | Broadside arcs; reload per side; chain shot hits sail not hull; boarding prerequisites; capture transfers ownership at 30% hull |
| **Vision** | Elevation increases vision radius by 25% per tier; forest blocks LOS; firing reveals for 3 s |
| **Population** | Contentment responds to the rum-supply-collapse scenario on the designed timeline; promotion requires housing + sustained needs |
| **Victory** | Each condition triggers exactly once, at the right moment, for the right player |

Rule: **every gameplay bug fixed gets an L2 test that fails before the fix and passes after.**

---

## 5. L3 — Networked integration tests

A real `MatchHost` plus N in-process clients over `LoopbackTransport`, driven by scripted command
streams, with `NetworkSimulator` injecting latency, jitter, loss and reorder.

Required tests:

| # | Test | Assertion |
|---|---|---|
| N1 | Join / handshake | Version and content-hash mismatch is refused with a specific error |
| N2 | Command validation | Illegal ownership, unaffordable cost, illegal placement, bad tech prereq, out-of-range target — each rejected, world unchanged, rejection reported to the sender |
| N3 | **Fog enforcement** | Record every byte sent to client A; assert **no message references any entity outside A's visibility set** at any tick. *This is the anti-map-hack regression test and it runs on every PR* |
| N4 | Intent replication | A 60-second worker haul cycle costs ≤ K bytes; assert **zero per-frame transform messages** |
| N5 | Correction bounds | Client replica position stays within 2 m of server truth for all visible entities under 200 ms latency |
| N6 | Rate limiting | A client sending 500 commands/second is throttled then kicked; the world is unaffected |
| N7 | Replay protection | Replayed/duplicated command sequence numbers are ignored |
| N8 | Reconnection | Client killed at tick T rejoins and matches server state within 15 s; repeated across combat, construction and mid-haul states |
| N9 | Loss/reorder | 10% loss + 50 ms jitter for 10 minutes: no stall, no divergence, reliable channel never deadlocks |
| N10 | Disconnect handling | Grace window, AI takeover / resignation, other players unaffected |
| N11 | Spectator isolation | A delayed spectator receives nothing newer than the configured delay |
| N12 | Interest transitions | Entering/leaving vision produces exactly one spawn/`LostSight`; no duplicate spawns; no leaked ghosts |

---

## 6. L4 — Determinism matrix

The most important pipeline in the project.

```
for os      in { ubuntu-x64, windows-x64, macos-arm64 }:
  for replay in tests/replays/*.brhr:
      re-simulate headlessly
      assert state hash matches the recorded hash at EVERY 200-tick checkpoint
      assert final world state hash matches exactly
```

- The replay corpus grows over time: one golden replay per milestone, plus one per determinism bug ever found.
- **A determinism failure blocks the merge.** No exceptions, no "fix it later".
- A deliberately seeded determinism bug is used quarterly to verify the pipeline still catches things (a fire drill for the safety net).

**Additional determinism checks**
- No-allocation test: 1,000 ticks with a populated world allocates 0 bytes on the sim thread
- Analyser tests: a file with `float` in the sim fails to compile; a `UnityEngine` reference outside the client fails to compile
- Iteration-order test: a world built by inserting entities in a different order, then normalised, produces the same hash

---

## 7. Manual test script — M3 prototype

This is the script the prototype gate is judged against. It must pass on **two physical machines over
a LAN**, not only on one machine.

### 7.1 What can be tested today

| Route | Status |
|---|---|
| **Headless, automated** — `dotnet test Brinehold.sln` | ✅ 185 tests. Covers everything in 7.2 except the human-in-the-loop steps |
| **Headless, measured** — `tools/dev/benchmark.sh` | ✅ Tick cost, per-tier bandwidth, state hash |
| **Unity, one machine, listen mode** | ⚠️ Written, never compiled — see `unity/README.md` |
| **Separate processes over UDP** — `tools/dev/run-networked-match.sh` | ✅ Server plus two clients, real sockets |
| **Two physical machines** | ⚠️ The same commands take `--host`, but this has not been run on two machines |

```bash
# The automated equivalent of most of the checklist below
dotnet test Brinehold.sln

# A ten-minute match, measured
tools/dev/benchmark.sh 12000

# A real-time headless match, printing a state hash every ten seconds
tools/dev/run-local-match.sh
```

To run the Unity client: open `unity/BrineholdClient`, add `PrototypeSceneSetup` to an empty scene,
press Play. Full instructions and its verification status are in `unity/README.md`.

### 7.2 Checklist

Each row names the automated test that covers it, where one exists. A row with no test is a
human-in-the-loop check that needs the Unity client.

| # | Step | Expected | Automated by |
|---|---|---|---|
| 1 | Two clients connect | Both see the same match start together | `MatchHarness` (all integration tests) |
| 2 | Each player has 10 workers, a warehouse, a separate starting area | Correct counts and ownership | `BothPlayersStartWithTenWorkersAndACore`, `StartingAreasAreSeparated` |
| 3 | Camera pan, edge-scroll, zoom, rotate | Smooth, clamped to the map | `CameraTests` (model only; feel needs Unity) |
| 4 | Click, drag-select, shift-click | Selection and counts correct | `SelectionTests` |
| 5 | Right-click ground | Units path there and arrive | `RightClickingGroundIsAMoveOrder`, `OrdersIssuedByTheClientAreExecutedByTheServer` |
| 6 | Right-click a forest | Workers harvest, carry, deposit, repeat | `WorkerHarvestsWoodAndDeliversItToTheWarehouse`, `WorkerKeepsCyclingAndDeliversRepeatedly` |
| 7 | Watch the resource bar | Rises only on deposit, never while carrying | `ResourcesOnlyRiseOnDepositNotWhileCarrying` |
| 8 | Place a house | Ghost shows legality; site appears; workers build it; population cap rises | `PlacingAHouseDeductsResourcesAndCreatesASite`, `WorkersCompleteTheHouseAndRaiseThePopulationCap` |
| 9 | Place on water or on a building | Rejected with a reason; no resources spent | `PlacingOnWaterIsRejectedAndCostsNothing`, `TheGhostRefusesWaterAndSaysWhy` |
| 10 | Build a dock, then a cutter | Ship appears in water and is controllable | `ADockBuildsAShipThatFloats` |
| 11 | Train a soldier | Trains only if resources and population allow | `TrainingAWorkerDeductsFoodAndSpawnsAfterTheTimer`, `TrainingIsBlockedByThePopulationCap` |
| 12 | Move a unit into fog | The opponent does not see it | `NoPacketEverMentionsAnUnseenEnemyUnit` |
| 13 | Look at the opponent's base | No live units, no resource numbers | `AClientNeverLearnsTheEnemyStartingArmyExists`, `PrivateEconomyIsNeverSentToTheOtherPlayer` |
| 14 | Attack a worker | Damage and death resolve identically for both | `ASoldierKillsAnEnemyWorker`, `BothClientsAgreeAboutAnEntityTheyCanBothSee` |
| 15 | Attack a building | Takes damage and can be destroyed | `BuildingsCanBeDestroyedBySoldiers` |
| 16 | Destroy the enemy warehouse | Match ends; both clients shown the right result | `DestroyingTheEnemyCoreEndsTheMatchForBothClients` |
| 17 | Netgraph (F3) | No per-frame transform traffic; within budget | `MovementCostsIntentsNotAStreamOfTransforms`, `ABusyMatchStaysInsideTheBandwidthBudget`, `AFullHarvestCycleNeedsAlmostNoCorrections` |
| 18 | 200 ms latency, 5% loss | No desync, stall or corruption | `AMatchRunsCorrectlyUnder200MillisecondsOfLatencyAndFivePercentLoss` |
| 19 | Kill and restart a client | Reconnects and resumes | ❌ Not built (M6) |
| 20 | Cheat client | Every illegal request refused, world unchanged | `AuthorityOverTheWireTests` (11 tests) |

**Measured on the current build** (2 players, 10 workers each, 10 minutes, Release, one core):
0.071 ms per tick · 34.6 B/s per client · 0 corrections · idle traffic ~25 B/s.

### 7.3 Cheat client

`--cheat-mode` is a **deliberate, maintained test harness**, not a leftover debug flag: a client
build that sends deliberately illegal commands. It is how we prove the authority model rather than
assuming it. It is excluded from release builds by a compile-time define, and its absence from
release artifacts is itself a CI check.

---

## 8. L5 — Load, soak and performance

| Test | Configuration | Pass criteria |
|---|---|---|
| **Tick budget** | 8 players, ~15,000 entities, 60 min | p99 server tick ≤ 25 ms; no tick > 45 ms |
| **Bandwidth** | Same | Per-client downstream ≤ 25 KB/s mean, ≤ 80 KB/s peak; upstream ≤ 2 KB/s |
| **Memory** | Same | Server ≤ 512 MB, flat across the match (no leak trend) |
| **Soak** | 3-hour match, bots | No leak, no drift, no error log entries, tick budget still met at hour 3 |
| **Client frame rate** | 3,000 visible entities, mid-range GPU | ≥ 60 FPS at standard camera height |
| **Pathfinding storm** | 500 units ordered to one destination simultaneously | Path budget respected; no tick spike > 45 ms; all units arrive |
| **Reconnect storm** | 4 of 8 clients drop and rejoin simultaneously | All rejoin ≤ 15 s; other players see no hitch |

Results are recorded per run and **trended**; a 10% regression on any budget opens a blocking issue.

---

## 9. LOD equivalence testing

Simulation LOD may change *fidelity*, never *outcome*.

```
Run scenario S for 10 game-minutes at LOD L0  → record all goods produced/consumed, population, coin
Run the identical scenario forced to LOD L2   → same record
Assert: every economic quantity matches within 1%
Assert: switching L0↔L2 mid-run does not create or destroy any goods
```

This runs for every district archetype (harvest-only, refine-only, mixed, cliff-top with a lift) and
on every PR that touches the economy or LOD systems.

---

## 10. Security and anti-cheat testing

| Attack | Test |
|---|---|
| Map hack | N3 (fog byte-level assertion) |
| Resource hack | Cheat client requests resources; assert unchanged |
| Instant build / free units | Cheat client sends completion messages; assert ignored |
| Unit theft | Commands targeting another player's entities; assert rejected |
| Damage hack | Client-sent damage messages; assert no such message exists in the protocol at all |
| Speed hack | Client tampers with its replica; assert corrected within 250 ms and telemetered |
| Command flood | N6 |
| Malformed packets | Fuzz every message type with random and adversarial payloads; assert no crash, no OOM, connection closed cleanly |
| Oversized payloads | Fragment-bomb the snapshot channel; assert bounded memory and clean rejection |

The fuzz suite runs nightly against the server binary.

---

## 11. Bug reporting workflow

Every bug report from playtest or production carries:

1. The **replay file** (`.brhr`) — usually a complete reproduction on its own
2. The tick number where the problem appeared
3. Server log excerpt and build/content hashes

Because the simulation is deterministic and the replay is a command log, most bugs reproduce exactly
on a developer machine with `Brinehold.Tools.ReplayCheck --replay X --break-at-tick N`. This is the
main reason determinism is worth its cost, beyond the feature itself.

---

## 12. Definition of done (per feature)

- [ ] L1/L2 tests for the new logic, including at least one failure-path test
- [ ] L3 test if it touches replication, commands or ownership
- [ ] Determinism corpus updated if it changes simulation state
- [ ] Performance impact measured if it runs per-tick or per-entity
- [ ] Docs updated (design doc + `CHANGELOG.md`)
- [ ] Manual test steps written for the milestone report
- [ ] No previously working functionality removed without a written reason

---

## 13. CI summary

| Workflow | Trigger | Gate |
|---|---|---|
| `ci.yml` | every PR | L1–L3, analysers, content validation. **< 5 min.** Blocking |
| `determinism.yml` | every PR + nightly | L4 across 3 platforms. Blocking |
| `unity-build.yml` | main + tags | Client and server artifacts. Blocking on main |
| `loadtest.yml` | nightly | L5 budgets. Non-blocking, but a regression opens a blocking issue |
| `fuzz.yml` | nightly | Protocol fuzzing. Blocking on crash |
