# Changelog — Brinehold

All notable changes to this project are recorded here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versioning will follow [Semantic Versioning](https://semver.org/) once there is a build to version.

**Project status: M0 — architecture and design. No gameplay code exists yet, by design.**

---

## [Unreleased]

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
