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

### 7.1 Setup

```bash
# Terminal 1 — authoritative server
dotnet run --project src/Brinehold.Server -- \
    --map tests/maps/prototype_twin_coves \
    --players 2 --port 7777 --log-level info

# Machine A and Machine B — clients
BrineholdClient.exe --connect <server-ip>:7777 --name PlayerA
BrineholdClient.exe --connect <server-ip>:7777 --name PlayerB
```

Local single-machine alternative: `tools/dev/run-two-clients.sh` (launches the server in listen mode
plus two client windows).

### 7.2 Checklist

| # | Step | Expected |
|---|---|---|
| 1 | Both clients connect | Both see the lobby, then the match starts together |
| 2 | Each player has 10 workers, a Warehouse, and a separate starting area | Correct counts and ownership on both screens |
| 3 | Pan, edge-scroll, zoom, rotate the camera | Smooth, no clipping through terrain |
| 4 | Click a worker; drag-select several; shift-click to add | Selection ring and panel update; counts correct |
| 5 | Right-click ground | Workers path there, avoid obstacles, arrive |
| 6 | Right-click a forest | Workers harvest Wood, carry it visibly, deposit it at the Warehouse, repeat |
| 7 | Watch the resource bar | Wood rises only when a worker actually deposits, never during carrying |
| 8 | Place a House | Ghost shows legal/illegal placement; site appears; workers deliver materials; building completes; population cap rises |
| 9 | Try to place a building on water / on the enemy's area | Rejected with a visible reason; no resources deducted |
| 10 | Build a Dock, then a Cutter | Ship appears in water and is controllable |
| 11 | Train a Cutthroat | Trains only if resources and population allow; appears at the building |
| 12 | Move a Cutthroat into fog | The other player does **not** see it until it enters their vision |
| 13 | **On Player B's client, look at Player A's base** | Only explored terrain and last-seen buildings — **no live units, no resource numbers** |
| 14 | Attack a worker with a Cutthroat | Damage and death resolve identically on both screens |
| 15 | Attack a building | It takes damage, shows damage state, and can be destroyed |
| 16 | Destroy the enemy Warehouse | Match ends; **both** clients show the correct win/loss |
| 17 | Open the netgraph overlay (F3) during play | Per-tier bytes shown; **no per-frame transform traffic**; total ≤ 8 KB/s per client |
| 18 | Re-run the whole script with `--netsim 200ms,5%` | Identical outcomes; no desync, no stall, no rubber-banding beyond the correction threshold |
| 19 | Kill a client process mid-match and restart it | *(M6 requirement; M3 stretch)* Reconnects and resumes with correct state |
| 20 | Run the cheat client (`--cheat-mode`) | Requests free resources, moves enemy units, instant-builds — **all rejected, world unchanged, attempts logged server-side** |

**The prototype passes only when items 1–18 and 20 pass on two machines.**

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
