# BRINEHOLD — Game Design Document

**Working title:** *Brinehold: Tides of the Free Isles*
**Codename:** `brinehold`
**Genre:** Real-time strategy with deep settlement / logistics simulation
**Engine:** Unity 6 LTS · C#
**Platform:** PC — Windows client (primary), Linux client (later), Linux dedicated server (day one)
**Players:** 2 / 4 / 6 / 8
**Status:** Design phase — no gameplay implemented yet

> **Document status:** This is the design contract for the project. `MULTIPLAYER_ARCHITECTURE.md`,
> `TECHNICAL_ARCHITECTURE.md`, `ECONOMY_DESIGN.md` and `COMBAT_DESIGN.md` are subordinate to it and
> must not contradict it. Where they do, this document is amended first.

---

## 0. Originality policy (read first)

This project is **inspired by the gameplay style** of settlement-builder pirate games and by classic
real-time strategy pacing. It is an **original IP**. The following are hard rules for every
contributor, human or AI:

| Rule | Meaning |
|---|---|
| No asset reuse | No art, models, textures, icons, UI sprites, fonts, music, SFX, or voice lines from any existing game. All assets are commissioned, made in-house, or licensed with a commercial licence recorded in `LICENSES.md`. |
| No name reuse | No building, unit, resource, faction, age, map, or character names copied from an existing game. Real historical / public-domain nautical vocabulary (sloop, brig, capstan, fluyt) is fine — it is not owned by anyone. |
| No narrative reuse | No story, characters, dialogue, or lore copied or paraphrased from an existing game. |
| No map reuse | No recreated maps or level layouts from an existing game. |
| Mechanics are fair game | Game *systems* (production chains, fog of war, ages, control groups) are not copyrightable. We may study and learn from them freely. |
| No "reference build" in repo | Do not commit decompiled data, extracted assets, or ripped tables from any other game. |

Anything ambiguous goes to design review before it enters the repo.

---

## 1. Concept

You are a **Shipmaster**: a captain who has run out of sea and must put down roots. You land on a
storm-scarred tropical archipelago called the **Free Isles** with a longboat, a handful of survivors
and no charter from anybody. You will carve a settlement — a **brinehold** — out of a beach, climb it
up the cliffs behind you, feed it, arm it, and put a fleet in the water.

So will everybody else. There is not enough good harbour for all of you.

**One sentence:** *A real-time strategy game where your settlement is a physical machine of workers,
goods and cliffside infrastructure, and the sea between you and your rivals is the real battlefield.*

---

## 2. Design pillars

### P1 — The settlement is a machine, not a build order
Buildings do not emit resources from nothing. They consume physical goods that physical workers
carried there. A settlement is a *layout problem*: where the sawmill sits relative to the forest and
the shipyard decides how many hulls you launch this hour. A bad layout is a real, visible,
diagnosable defeat condition — you can watch the goods pile up at the wrong end of the island.

### P2 — Height is a strategic resource
The archipelago is vertical. Beaches, lowlands, hill terraces, cliffs and plateaus are distinct
build surfaces connected only by infrastructure you build: ramps, stairs, bridges, rope bridges and
cargo lifts. High ground gives vision, weapon range and defensive advantage. It also strangles your
logistics, because every crate has to be winched up there. **Every metre of altitude is a trade.**

### P3 — The sea decides the match
Land combat is real but bounded by geography. The map is an archipelago: expansion, trade, raiding,
reinforcement and most victory conditions run through water. A player who loses control of their sea
lanes loses the match slowly, even while their walls stand.

### P4 — The economy is a legitimate military target
You do not have to march on the enemy capital. Sinking their cargo runs, burning a plantation,
blockading their harbour, dropping a bridge on a cliff route, or taking the iron island they depend
on are all *winning moves*. Economic warfare is a first-class strategy, not a griefing tactic.

### P5 — Server truth
Competitive integrity is a feature. The authoritative simulation runs on a server the players do not
control. Clients render and predict; they never decide. Fog of war is enforced by *not sending* the
data, not by hiding it client-side. See `MULTIPLAYER_ARCHITECTURE.md`.

### P6 — Readable depth
The systems are deep; the *presentation* of them must be brutally legible. Every number a player
needs to make a decision is one hover away. If a system cannot be explained in one UI panel and one
tooltip, it is too complicated and gets cut or merged.

---

## 3. Setting (original)

The **Free Isles** are the wreckage of an empire's shipping lane. Two decades ago the Meridian
Company's convoy route through the archipelago collapsed — storms, mutiny, and a war it lost
somewhere else. The Company withdrew, leaving behind half-built harbour works, a lighthouse it never
lit, cane fields gone feral, and a lot of people who had been paid in promises.

Those people are still here. They have no flag and no law, and the Company is coming back.

**Tone:** working-class, salt-crusted, practical. Not cursed-treasure supernatural, not comic-opera
pirate. The fantasy is *building something of your own on a hostile coast and defending it*, with
gunpowder, rum, rope and rigging. Humour is dry and comes from the people, not from set dressing.

**Named world elements** (all original, owned by this project):

- **The Free Isles** — the archipelago.
- **The Meridian Company** — the departed colonial power; the "why" behind ruins and neutral ports. Not a playable faction in v1; a late-game pressure event and campaign hook.
- **A brinehold** — a fortified independent settlement. Also the player's home settlement.
- **The Ledger** — the informal reputation system the free ports keep on every captain.
- **Free ports** — neutral independent harbour towns (Section 16).

---

## 4. Core loop

```
   SCOUT  ──►  CLAIM  ──►  BUILD  ──►  PRODUCE  ──►  PROJECT POWER
     ▲                                                    │
     └──────────────── the map changes ◄──────────────────┘
```

**Moment to moment (seconds):** select, order, place, watch a job resolve.
**Short loop (1–3 min):** a production chain comes online; a building finishes; a ship launches.
**Medium loop (5–15 min):** a Charter Rank advance; an island claimed; a raid landed or repelled.
**Long loop (whole match):** the balance of sea control and the race to a victory condition.

---

## 5. Match shape

Target lengths (see Section 20 for settings):

| Preset | Target | Rank pacing | Starting stock |
|---|---|---|---|
| **Skirmish** | 25–45 min | Fast (0.6× rank costs) | Rank II start, 15 workers |
| **Standard** | 45–90 min | Normal | Rank I start, 10 workers |
| **Epic** | 90–180 min | Slow (1.4× rank costs), larger maps | Rank I start, 8 workers, harsher needs |

### Phase model (Standard, 8-player free-for-all)

**Early game — 0 to ~12 min.** Land, scout your island, cut wood, get food working, throw up the
first housing and a stockpile. Find your neighbours. Find your iron. Nobody fights yet; the fight is
against the terrain.

**Early-mid — ~12 to ~25 min.** Real chains come online (planks, salt, cloth). First dock, first
cutter, first proper scouting of the water. Territory claims start to overlap. First skirmishes are
over resource islands, not over settlements.

**Mid — ~25 to ~55 min.** Fleets exist. Rank III economies are running rum, powder and tools.
Players raid supply routes, fortify chokepoints, garrison neutral ports, and start signing (and
breaking) alliances. This is the longest and most interesting phase.

**Late — ~55 min+.** Heavy hulls, coastal batteries, mortar ketches, amphibious invasions,
blockades, and an open race for a victory condition that everyone can see on the objective bar.

---

## 6. Company Doctrines (light asymmetry)

v1 ships **one shared ruleset** — every player has access to every building and unit. Asymmetry
comes from a **Doctrine** chosen at match start, which is a small bonus tree, not a different
faction. This keeps balance tractable while the core is being tuned; full factions are a post-1.0
consideration.

| Doctrine | Identity | Example bonuses (values are placeholders for balance pass) |
|---|---|---|
| **Saltwake Traders** | Economy, trade, free ports | +15% trade income; free port relations decay 50% slower; fluyt cargo +20% |
| **Ironshore Pact** | Industry and fortification | Stone and iron chains +10% throughput; defensive structures −20% stone cost, +10% HP |
| **Tidewatch** | Naval supremacy | Warship build time −12%; ships repair at any friendly dock; +1 vision range at sea |
| **Sablewake** | Raiding and disruption | Raider units +10% move on enemy territory; sabotage actions cost −25%; ships leave no wake trail at long range |
| **The Freeholding** | Population and growth | Housing +1 capacity per tier; contentment decays 20% slower; migration events more frequent |

Doctrine choice is visible to all players in the lobby (no hidden information at match start).

---

## 7. Resources

Full chains are in `ECONOMY_DESIGN.md`. Summary of the resource classes:

| Class | Examples | Storage | Notes |
|---|---|---|---|
| **Primary** | Timber, Fish, Grain, Stone, Iron Ore, Hemp, Cane, Coal, Clay | Warehouse | Harvested from map nodes by workers |
| **Refined** | Planks, Salt Fish, Bread, Cloth, Rope, Rum, Bar Iron, Bricks, Powder, Tools, Weapons, Cannon | Warehouse | Produced by buildings from inputs |
| **Abstract** | Coin, Labour, Contentment, Notoriety | Global (no hauling) | Player-level, not physically carried |

**Coin** is the only universal liquid resource and is earned by *selling*, not by mining. This is a
deliberate design choice: it forces trade, free-port relations and prize-taking to matter.

**Prototype subset** (Milestone 3, see roadmap): Timber, Food, Stone, Coin only.

---

## 8. Population and society

The player manages a **crew-turned-town**, not generic villagers. Population is grouped into
**Stations** (original class names). Each Station is a distinct labour pool with distinct needs.

| # | Station | Fills | Needs (cumulative) | Notes |
|---|---|---|---|---|
| 0 | **Castaways** | Arrivals, refugees, freed prisoners | Food, Shelter | Unskilled. Work at 60% rate. Promote to Deckhands when housed and fed. |
| 1 | **Deckhands** | Hauling, harvesting, construction | + Water, Rum | The backbone. Most of your population. |
| 2 | **Freeholders** | Skilled production (sawmill, smithy, distillery) | + Clothing, Entertainment | Require Rank II housing. |
| 3 | **Artificers** | Advanced production (powder, cannon, precision tools) | + Safety, Luxury goods | Require Rank III housing. Slow to replace. |
| 4 | **Sea Officers** | Ship command, garrison command, free-port envoys | + Prestige goods, Standing | Cap is low; they are a strategic resource. |

**Promotion** requires: an open slot in higher-tier housing, all lower needs met for a sustained
period, and (from Freeholder up) a specific building unlocked. **Demotion** happens under sustained
unmet needs.

### Contentment

Each Station has a **Contentment** value (0–100) recomputed on a slow tick. It is driven by need
satisfaction, safety, employment, taxation, and recent events (a lost battle, a burned warehouse, a
successful raid you carried out).

| Contentment | Effect |
|---|---|
| 85–100 | **Thriving** — +15% work rate, faster promotion, migration bonus |
| 60–84 | **Content** — baseline |
| 35–59 | **Restless** — −15% work rate, crime events begin, promotion halted |
| 15–34 | **Unrest** — −35% work rate, theft from warehouses, desertion (population leaves) |
| 0–14 | **Mutiny** — production halts in affected districts; rioters damage buildings; a Mutiny must be put down (garrison) or bought off (rum, coin, food) |

Mutiny is **not** instant loss — it is a spiral you can recover from at real cost. It is also
something an enemy can *engineer* by destroying your rum supply. See `ECONOMY_DESIGN.md` §
"Unrest as a weapon."

---

## 9. City building

### 9.1 Placement rules

- Buildings occupy a **footprint** on a single terrain tier (they do not straddle a cliff edge).
- Buildings need a **connection** to the settlement's path network to function (Section 10).
- Some buildings have **terrain requirements**: docks and shipyards need adjacent deep-enough water; mines need an ore node; plantations need arable soil; wind-driven buildings prefer exposed ridges.
- Buildings have a **service radius** or a **haul relationship**, never both by accident — every building's supply model is explicit in its data.

### 9.2 Building families

| Family | Examples | Purpose |
|---|---|---|
| **Storage & logistics** | Stockpile, Warehouse, Granary, Cargo Lift, Crane, Depot | Hold goods; define haul distances |
| **Housing** | Shelter, Longhouse, Freehold Row, Artificer Quarters, Officers' House | Population capacity and Station tiers |
| **Food** | Fishing Wharf, Grain Plot, Orchard, Hunting Post, Bakery, Salt House | Feed the settlement |
| **Extraction** | Lumber Camp, Quarry, Ore Mine, Coal Pit, Clay Pit, Hemp Field, Cane Field | Primary resources |
| **Refining** | Sawmill, Ropewalk, Weavery, Kiln, Furnace, Distillery, Powder Mill, Toolworks | Refined goods |
| **Military production** | Weaponsmith, Foundry, Armoury, Barracks, Drill Yard, Gun Battery Works | Units and ordnance |
| **Naval** | Dock, Shipyard, Dry Dock, Chandlery, Careening Beach | Ships, repair, refit |
| **Civic & social** | Tavern, Market, Bathhouse, Chapel of the Lantern, Gaol, Council Hall | Needs, contentment, order |
| **Trade** | Trading Post, Harbour Office, Warehouse Quay | Coin, free-port relations |
| **Defence** | Palisade, Stone Wall, Gatehouse, Watchtower, Coastal Battery, Sea Chain, Bastion | Section 13 |
| **Infrastructure** | Path, Stone Road, Stair, Ramp, Bridge, Rope Bridge, Cargo Lift, Aqueduct | Section 10 |
| **Landmark** | The Great Beacon, The Vault, The Grand Careenage | Victory-condition structures (Section 18) |

Full data tables (cost, build time, workers, inputs, outputs, rates) live in `ECONOMY_DESIGN.md`.

### 9.3 Districts

Buildings within a connected path cluster form a **District** automatically (no manual zoning). A
District is the unit for:

- **Contentment** evaluation (a slum on the far headland can riot while your main town is fine)
- **Service coverage** (tavern, market, gaol radius)
- **Logistics LOD** (Section 21) — distant districts simulate hauling at lower fidelity
- **Damage reporting** ("Cliffside District under attack")

---

## 10. Vertical construction and logistics

This is the feature that most distinguishes Brinehold from a conventional RTS.

### 10.1 Terrain tiers

Terrain is a heightfield quantised into **tiers** — flat, buildable bands separated by impassable
cliff faces:

| Tier | Name | Typical use | Traits |
|---|---|---|---|
| 0 | **Tidal** | Docks, wharves, careening beach | Floodable in storms; vulnerable to naval bombardment |
| 1 | **Beach / Lowland** | Main settlement, warehouses, industry | Cheapest to build and haul on |
| 2 | **Terrace** | Housing, farms, secondary industry | Mild vision and range bonus |
| 3 | **Hill** | Watchtowers, batteries, defensible housing | Real defensive value; hauling penalty |
| 4 | **Plateau / Cliff** | Fortresses, long-range batteries, the Great Beacon | Best vision and range; brutal logistics |

Units and goods **cannot** move between tiers except through a **Connector**.

### 10.2 Connectors

| Connector | Cost class | Throughput | Traits |
|---|---|---|---|
| **Ramp** | Cheap (earth) | High (walking speed, no batching) | Only for a 1-tier rise on gentle slopes; wide footprint |
| **Stair** | Cheap (timber/stone) | Medium (movement penalty, single file at scale) | Any 1-tier rise; cheap to build, cheap to destroy |
| **Bridge** | Medium (planks/stone) | High | Spans gaps and rivers on the same tier |
| **Rope Bridge** | Very cheap (rope/planks) | Low, capacity-limited (N units at once) | Spans big gaps; **fragile** — a prime raid target |
| **Cargo Lift** | Expensive (timber, rope, iron) | Batched: N crates per cycle, cycle time T | Goods only, no units. The workhorse of vertical logistics |
| **Crane Head** | Expensive | Batched, dockside only | Loads/unloads ships in bulk; without one, ships load crate-by-crate |
| **Winch Tower** | Late, expensive (iron, tools) | High batched, both goods and units | Rank IV unlock; the "solved" vertical logistics answer |

**The core tension:** high ground is militarily excellent and logistically miserable. A cliff-top
battery with a rope bridge for a supply line is a battery that runs out of powder. A cliff-top
battery with a Winch Tower cost you a Rank IV economy to get there.

**Connectors are destructible and are legitimate targets.** Cutting a rope bridge can strand a
garrison, starve a district and halt a production chain simultaneously. Connector loss is announced
prominently to the owner.

### 10.3 Goods actually move

There is no teleporting resource pool. Every unit of every physical good is:

1. Produced at a building, placed in its **output buffer** (finite).
2. Picked up by a hauler (a Deckhand doing a haul job) with a **carry capacity**.
3. Carried along the path network, through connectors, at walking speed.
4. Deposited into a **storage node** (stockpile, warehouse, granary) or directly into a consumer's **input buffer**.

Consequences that are *features*:

- **Output buffer full ⇒ production stalls.** The building shows a clear "blocked: no hauler" state.
- **Distance is cost.** Doubling the haul distance halves that chain's effective throughput.
- **Connectors are bottlenecks.** A cargo lift with 6 crates/cycle is a hard throughput ceiling.
- **Bad layouts are visible.** Players can see the queue of workers waiting at a stair.

**Player tools to manage it:** stockpile placement, warehouse priorities, per-good storage filters,
haul priority per building, dedicated hauler assignment, road upgrades (movement speed), and a
**Logistics Overlay** that heat-maps traffic and flags starved buildings.

### 10.4 Roads

Paths are a buildable good. Dirt Path (free-ish, +10% move) → Gravel Road (+25%) → Stone Road (+40%,
allows Wagon carts at Rank III, which multiply carry capacity). Roads are cheap to lay and
**cheap to destroy** — raiders can crater a road.

---

## 11. Workers

Workers are **real simulated units on the map** with position, path, carried goods and a job. They
are not an abstract number.

### 11.1 Jobs

| Job | Description |
|---|---|
| **Harvest** | Go to a resource node, spend gather time, return with a load |
| **Haul** | Move goods from source (building output / storage) to destination (storage / building input) |
| **Construct** | Deliver materials to a construction site, then apply build labour |
| **Operate** | Occupy a production building slot and run it |
| **Repair** | Restore building HP, consuming materials |
| **Load / Unload** | Move goods between a dock/crane and a ship's hold |
| **Extinguish** | Put out fires (raid aftermath) |
| **Garrison work** | Man a wall, tower or battery (non-combat crew role) |

### 11.2 Job assignment

A deterministic **job market**: open jobs are scored (priority × urgency ÷ travel cost) and assigned
to idle workers in a strictly ordered pass. Ties break on entity ID so the result is identical on
every machine. Players influence it via building priorities, not by micromanaging individual
workers — but they *can* select a worker and give a direct order, which pins it.

### 11.3 Scale

Design target: **up to ~1,500 simulated workers per player** in a large late-game settlement, with
LOD (Section 21) reducing per-worker cost for distant districts. The prototype targets 10 per player.

---

## 12. Military (summary — full detail in `COMBAT_DESIGN.md`)

Land unit classes (original names):

| Unit | Role | Signature trait |
|---|---|---|
| **Cutthroat** | Cheap melee | Cheap, fast, terrible against armour |
| **Buccaneer** | Elite melee | High morale, strong charge, expensive |
| **Fusilier** | Line ranged | Volley fire, reload cycle, weak in melee |
| **Sharpshot** | Long-range skirmisher | Big high-ground range bonus, tiny magazine |
| **Grenadier** | Anti-formation / anti-structure | Splash damage, friendly-fire risk |
| **Gun Crew** | Field artillery | Slow, devastating vs buildings, needs Powder upkeep |
| **Marine** | Amphibious line infantry | No landing penalty, fights well off a ship |
| **Boarding Crew** | Anti-ship specialist | Enables ship capture instead of sinking |
| **Sapper** | Demolition | Destroys walls, bridges, connectors, roads fast |
| **Bosun** *(officer)* | Command | Morale aura, formation orders, one per group |

Every unit has: HP, Damage, Damage type, Range, Armour, Armour class, Move speed, **Morale**,
Training cost, Training time, Population cost, and Upkeep.

**Morale** is the systemic core of land combat: units break, rout, rally and can be broken by
flanking, artillery, officer loss and high-ground disadvantage — not just by HP loss.

**Terrain matters:** elevation grants range and accuracy; forest grants cover and breaks formation;
beaches are a penalty for the landing side; roads speed movement; a defended stair is a meat grinder.

---

## 13. Naval warfare

The sea is the main theatre. Ships are directly, manually controllable in real time — but the
control fantasy is **fleet command**, not arcade sailing.

Ship classes (original roster, historical-generic names):

| Class | Role | Notes |
|---|---|---|
| **Longboat** | Transport (tiny), landing craft | Rank I; cheap; how you first cross water |
| **Cutter** | Scout | Fast, 1 gun, huge vision, cannot fight |
| **Sloop** | Light raider | Fast, cheap, excellent at hunting cargo |
| **Fluyt** | Cargo hauler | Big hold, unarmed, the thing everyone wants to sink |
| **Brig** | Line workhorse | Balanced guns/speed/hull; the standard warship |
| **Bombard Ketch** | Shore bombardment | Mortars: huge range vs land, helpless vs ships |
| **Frigate** | Heavy warship | Rank IV; broadsides that decide engagements |
| **Razee** | Heavy raider | Frigate hull cut down: speed of a brig, guns of a frigate, no cargo |
| **Troopship** | Amphibious | Carries a landing force plus its guns |

Ship stats: Hull HP, Sail HP (mobility), Crew (boarding strength + reload rate), Cargo, Guns per
side, Gun range, Reload, Turn rate, Top speed, Draught (which water it can enter), Vision.

Core naval mechanics:

- **Broadsides and arcs.** Guns fire to port and starboard. Positioning is the skill.
- **Wind.** A single prevailing wind vector per map (rotating slowly, visible in UI) gives speed multipliers by heading. It is a readable strategic layer, not a sailing sim.
- **Damage separation.** Hull (sinks you), Sail (immobilises you), Crew (stops you reloading and defending a boarding). Chain shot targets sail; grape targets crew; round shot targets hull.
- **Boarding and prize-taking.** Grapple a weakened ship and win the crew fight to **capture** it. A captured ship is yours — the single most swingy play in the game, and the reason Boarding Crews exist.
- **Draught and shoals.** Heavy ships cannot enter shallow water. Shallows are a defensive asset and an ambush zone for sloops.
- **Blockade.** Ships parked in a harbour's mouth arc suppress its trade income and stop cargo movement.

---

## 14. Amphibious warfare

Transports carry land units and their artillery. Landing is a deliberate, punishable act:

- **Landing zones:** any beach tile (Tier 0/1). Cliffs cannot be landed on without a Rank IV **Grapple Assault** technology.
- **Landing penalty:** units that disembark are at reduced morale and cannot fire for a few seconds — except Marines.
- **Defender tools:** Coastal Batteries (long range, anti-ship), Watchtowers (vision), Sea Chains (block a harbour mouth), Beach Palisades, and pre-positioned Gun Crews.
- **Raid vs invasion:** a Sloop with 8 Cutthroats burning a plantation is a *raid* (cheap, fast, deniable). A Troopship convoy with Gun Crews and a Frigate escort is an *invasion* (expensive, slow, decisive). Both should be viable at their price point.

---

## 15. Economic warfare

Explicitly supported, explicitly rewarded. Targets and their effects:

| Target | Effect on victim |
|---|---|
| **Cargo ships** | Direct loss of goods; the raider *takes* part of the cargo (prize) |
| **Plantations / farms** | Food or cane chain stops; contentment falls; rum chain dies downstream |
| **Warehouses** | Stored goods destroyed (a share is looted if a unit reaches it) |
| **Cargo lifts / rope bridges / stairs** | A whole district is severed from the economy |
| **Roads** | Throughput drop across the settlement |
| **Docks / shipyards** | No new hulls, no repairs |
| **Resource islands** | Loss of an entire input at the source |
| **Harbour blockade** | Trade income suppressed, cargo movement halted |
| **Free-port relations** | Outbid or sabotage a rival's standing at a neutral port |

**Loot:** raiders that destroy a storage building recover a percentage of its contents as loot,
carried back by the raiding unit. Loot is lost if the raider dies before it reaches friendly
territory or a ship. This makes raids *profitable*, not merely spiteful.

---

## 16. Exploration and free ports

### 16.1 Fog of war

Three states, per player:
- **Unexplored** — black; no terrain, no shape.
- **Explored (fogged)** — terrain and static structures as last seen (a memory snapshot); no live units.
- **Visible** — live.

Vision comes from units, buildings, watchtowers and ships, and is modified by elevation (high ground
sees further and over obstacles) and by forest (blocks). Scouting is a real investment; the Cutter
exists for it.

### 16.2 Free ports (neutral settlements)

Free ports are AI-run neutral harbour towns scattered around the map. They are a **shared,
contested, non-military resource**.

Each free port has a **Relations** value with each player (−100 hostile … +100 sworn). Relations rise
with trade volume, completed contracts and gifts; they fall with hostile acts nearby, raiding their
shipping, or a rival's diplomacy.

What free ports offer, gated by Relations:

| Relations | Unlocks |
|---|---|
| ≥ 0 | Buy/sell goods at market rates; basic contracts |
| ≥ 25 | Better prices; **information** (reveals a map region, or a rival's last known fleet position) |
| ≥ 50 | **Contracts** (timed missions: deliver goods, sink a monster ship, escort a convoy) for Coin and Notoriety |
| ≥ 75 | **Mercenary recruitment** — unique units you cannot build yourself |
| ≥ 90 | **Exclusive charter** — a per-port strategic bonus, and rivals' relations there are capped |

Free ports can also be **taken by force**. This is expensive (they defend themselves), tanks your
Ledger standing with *every other* free port, and turns the port into a normal player-owned
settlement site. It is a real strategic option with a real cost.

### 16.3 Map features

**Ruins** (Meridian Company leftovers) give one-time caches or a technology discount when cleared.
**Treasure sites** require a specific find-and-dig interaction and yield Coin or a unique item.
**Dangerous regions** — reef fields, storm zones (periodic damage/slowdown), and a small number of
neutral hostile ships that make unescorted early expansion risky.

---

## 17. Charter Ranks (progression)

Five stages. Advancing costs resources *and* meeting a settlement requirement — you cannot rush ranks
purely on gold, you have to actually have built something.

| Rank | Name | Requirement (in addition to resource cost) | Headline unlocks |
|---|---|---|---|
| **I** | **Landfall** | — (start) | Shelter, Stockpile, Lumber Camp, Fishing Wharf, Grain Plot, Path, Longboat, Cutthroat |
| **II** | **Stockade** | 20 population, a Warehouse, a Stair or Ramp | Sawmill, Quarry, Palisade, Dock, Cutter/Sloop, Tavern, Barracks, Fusilier, Watchtower |
| **III** | **Free Port** | 60 population, 12 Freeholders, a Trading Post, a Shipyard | Furnace, Toolworks, Distillery, Ropewalk, Weavery, Market, Brig, Fluyt, Cargo Lift, Stone Wall, Gun Crew, trade routes |
| **IV** | **Marque** | 120 population, 20 Artificers, a Foundry, 2 claimed islands | Powder Mill, Cannon Foundry, Frigate, Bombard Ketch, Coastal Battery, Winch Tower, Grenadier, Sharpshot, Grapple Assault |
| **V** | **Admiralty** | 200 population, Sea Officers housed, a Council Hall | Razee, Bastion, Sea Chain, elite technologies, **Landmark structures** (victory buildings) |

Within each rank there are **technologies** (one-off purchases) in four lines: **Economy**,
**Logistics**, **Land**, **Sea**. Technologies are researched at specific buildings, take time, and
are visible to opponents through scouting (a lit Powder Mill tells you something).

Rank advance takes real time (a build-like timer) and is announced to all players — this is the
"they're going up, punish them now" tension that makes RTS pacing work.

---

## 18. Victory conditions

Configurable per match; multiple can be enabled simultaneously. All original.

| # | Name | Condition | Design intent |
|---|---|---|---|
| 1 | **Strike the Colours** *(domination)* | All rival Harbour Keeps destroyed or captured; last standing team wins | Classic fallback; always available |
| 2 | **The Long Watch** *(landmark)* | Build **The Great Beacon** on a Tier 4 plateau, then hold it lit for 12 min. Lighting it consumes Coal continuously — a supply line everyone can attack | Wonder-style, but it demands an *ongoing logistics defence*, not just a build |
| 3 | **Master of the Lanes** *(map control)* | Hold N of the map's Lane Beacons (fixed strategic sea points) for a cumulative 15 min | Rewards naval control and forces contact |
| 4 | **The Reckoning** *(economic)* | Accumulate and *keep* a target Coin total in **The Vault** for 5 min. The Vault is a physical, raidable building | Economic victory that can be robbed |
| 5 | **The Ledger** *(reputation)* | Reach a Notoriety threshold. Notoriety comes from prizes taken, raids landed, contracts completed and free ports won over | Rewards aggressive raiding play specifically |
| 6 | **Harbour Concord** *(diplomatic)* | Hold ≥ 90 Relations at a majority of free ports for 10 min | Non-military win route; contested by rivals' diplomacy |

Every timed condition shows a **public countdown to all players** the moment it starts. There are no
silent victories.

---

## 19. Diplomacy

Available in Free-for-all and Custom matches (locked in ranked team modes):

- **Alliance** — mutual, requires both to accept. Grants shared vision (optional flag) and prevents friendly fire.
- **Break alliance** — takes effect after a **60-second warning visible to the ally**. No instant backstabs; betrayal is possible but telegraphed.
- **Ceasefire** — timed non-aggression; auto-expires with a warning.
- **Tribute / trade** — send resources or Coin. Optionally taxed by match settings to prevent pure resource-dumping.
- **Shared vision** — toggleable independently of alliance.
- **Diplomacy settings in lobby:** locked teams, free diplomacy, no diplomacy, tribute tax %, betrayal warning length.

---

## 20. Modes and settings

**Modes:** 1v1, 2v2, 3v3, 4v4, Free-for-all (up to 8), Co-op vs AI, Custom.

**Lobby settings:** map, map size, player count, teams, doctrine, starting rank, starting resources,
match length preset, victory conditions enabled, diplomacy rules, AI difficulty, reveal map,
game speed (locked in ranked), free ports on/off, treasure density, storm frequency.

**Speed:** competitive multiplayer runs a **fixed, synchronised simulation rate**. Pause is available
in single-player and in custom lobbies where all players consent (any player can unpause). Ranked
multiplayer cannot pause; a disconnect triggers a bounded reconnection window instead
(`MULTIPLAYER_ARCHITECTURE.md` § Reconnection).

---

## 21. AI players

AI uses **exactly the same rules** as humans: the same simulation, the same commands, the same
resource costs, the same fog of war. No resource cheating at any difficulty — difficulty scales
*decision quality, reaction time and APM ceiling*, not the ruleset.

| Difficulty | Model |
|---|---|
| **Deckhand** | Slow reactions, poor layouts, no raiding, limited APM |
| **Mate** | Sound build order, basic defence, occasional scouting |
| **Captain** | Proper production chains, scouting, timed attacks, reacts to raids |
| **Commodore** | Efficient layouts, sea control, economic raiding, adapts to scouted intel |
| **Admiral** | High APM ceiling, multi-front pressure, punishes rank-up timings |

AI runs **server-side**, inside the authoritative simulation process, and issues the same command
messages a human client would. This means AI is automatically replay-safe and desync-safe.

---

## 22. User interface

Screen furniture:

- **Top bar** — resources (with rate-of-change arrows and hover breakdown), population by Station, Contentment summary, Coin, current Rank + advance progress, match timer, victory-condition progress.
- **Minimap** — terrain tiers shaded, fog, units, buildings, pings, camera frustum, alerts. Click to move camera, right-click to order.
- **Selection panel** — one unit (full stats), multi-select (grouped by type with counts), building (production, inputs/outputs buffers, staffing, queue).
- **Build menu** — categorised, hotkeyed, shows cost and greys out unaffordable/unavailable items with the reason.
- **Production queues** — per building, with cancel and reorder.
- **Technology panel** — the four lines and the rank track, with clear prerequisites.
- **Fleet panel** — ships grouped into fleets, with hull/sail/crew/cargo bars and stance.
- **Diplomacy panel** — per-player status, proposals, tribute.
- **Trade panel** — free-port prices, trade route setup, active routes and their profitability.
- **Notification feed** — attack alerts, building complete, chain starved, unrest, connector destroyed. Clickable to jump the camera. Aggressively deduplicated.
- **Objectives panel** — active victory conditions and everyone's progress.

**Overlays** (hotkey-toggled): Logistics (traffic heat + starved buildings), Contentment, Territory,
Elevation/tiers, Vision/range, Defence coverage.

### Selection and control

- Single click; **drag box**; double-click (select all of type on screen); shift-click add/remove; ctrl-click (all of type on screen).
- **Control groups** `Ctrl+0..9` to assign, `0..9` to select, double-tap to centre camera. Groups may contain units, ships or mixed fleets. `Shift+N` appends to a group.
- Idle-worker cycling, next-building-of-type cycling, camera bookmarks (`Ctrl+F1..F4`).
- Full **rebindable hotkeys** with grid-position-based build hotkeys (so the layout is learnable).
- Right-click contextual default action; modifier keys for attack-move, patrol, stance.

---

## 23. Accessibility

Colourblind-safe player colours with pattern/shape differentiation; scalable UI (100–150%); full
key rebinding; no reliance on colour alone for alerts; subtitles/captioning for all voice barks;
reduced-motion option; a "large text" tooltip mode. Committed from the first UI milestone, not
retrofitted.

---

## 24. Art and audio direction (original)

**Art:** stylised realism, readable at RTS camera distance. Chunky, hand-painted-feeling wood and
canvas; strong silhouette rules so unit and ship classes are identifiable at a glance and from
above. Terrain tiers are visually distinct (colour and material change with altitude). Deliberately
**not** photoreal — readability wins.

**Colour:** each player gets a saturated identity colour applied to sails, flags, and a unit-base
ring. Neutral free ports are a distinct desaturated palette.

**Audio:** diegetic settlement soundscape (saw, hammer, capstan shanty, gulls) that thins out when a
district is starved or unhappy — the settlement *sounds* wrong before you notice the number.
Original score: small-ensemble strings, percussion, accordion and voice. All music and SFX
commissioned or created in-house; no licensed-from-a-game audio, ever.

---

## 25. What v1.0 does *not* include

Explicitly deferred so the scope stays honest:

- Single-player campaign with narrative
- Full asymmetric factions (Doctrines only)
- Modding API and Steam Workshop
- Ranked ladder and matchmaking service (custom/lobby only at 1.0)
- Weather beyond a simple storm zone and prevailing wind
- Naval crew micro-management below the ship level
- Console or mobile

---

## 26. Open design questions

Tracked here rather than silently assumed:

1. **Direct ship control granularity** — full manual helm vs. waypoint-and-stance. Prototype both in Milestone 7; pick by playtest.
2. **Worker micromanagement ceiling** — how much direct worker control to allow before it becomes mandatory APM tax.
3. **Contentment tick length** — long enough to be strategic, short enough to feel responsive.
4. **Free port capture** — is the Ledger penalty severe enough to keep it a real choice rather than always-correct?
5. **Match length in FFA-8** — 8-player FFA may exceed the 120-minute target; may need a shorter victory-condition preset.
6. **Doctrine count at 1.0** — five may be too many to balance; three is likelier.

---

*Related documents:* `MULTIPLAYER_ARCHITECTURE.md` · `TECHNICAL_ARCHITECTURE.md` ·
`ECONOMY_DESIGN.md` · `COMBAT_DESIGN.md` · `DEVELOPMENT_ROADMAP.md` · `TESTING.md` · `CHANGELOG.md`
