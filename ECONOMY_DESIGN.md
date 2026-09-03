# BRINEHOLD — Economy Design

**Status:** Proposed. Values are **first-pass targets for implementation and tuning**, not final
balance. Every number here lives in `packages/com.brinehold.content/Data/` and is changed by
designers without a code change.

**Unit conventions:** rates are **per game-minute at normal speed**. Distances are metres. A
"worker slot" is one Deckhand/Freeholder/Artificer occupying a building. Times are seconds.

---

## 1. Principles

1. **Nothing appears from nothing.** Every good is harvested from a map node or refined from other goods by a building staffed by real workers.
2. **Goods are physical.** Everything except Coin, Contentment and Notoriety is carried by a worker or a ship, along a path, through connectors. There is no global resource pool.
3. **Chains have depth, not busywork.** Two to four steps from raw to finished. Any chain longer than four steps must justify itself.
4. **Coin is earned, not mined.** The only sources of Coin are trade, contracts, prizes and loot. This forces engagement with free ports and with the sea.
5. **Every chain has a chokepoint an enemy can hit.** Deliberately: a single field, a single lift, a single cargo run.
6. **Layout is the skill.** Two players with identical build orders and different layouts should get measurably different output.

---

## 2. Goods

### 2.1 Primary goods (harvested from map nodes)

| Good | Source node | Base yield | Notes |
|---|---|---|---|
| **Timber** | Forest | 12 /min (3 workers) | Trees deplete and regrow slowly; forests thin visibly |
| **Stone** | Rock outcrop | 8 /min (4 workers) | Finite-but-large deposit |
| **Iron Ore** | Ore vein | 8 /min (4 workers) | Often on secondary islands — a claim target |
| **Coal** | Coal seam | 8 /min (4 workers) | The universal fuel; scarcity here throttles everything |
| **Clay** | Clay bank | 6 /min (3 workers) | River banks and lowlands |
| **Nitre** | Nitre cave / guano isle | 5 /min (3 workers) | **Rare, island-bound.** Gates all Powder. The single most contested resource |
| **Fish** | Coastal water density | 10 /min (3 workers) | Local depletion + regrowth; over-fishing a bay is real |
| **Game** | Wildlife | 6 /min (2 workers) | Early food, depletes permanently |
| **Grain** | Arable soil (built plot) | 8 /min (4 workers) | Needs flat, fertile Tier 1–2 land |
| **Fruit** | Arable soil (orchard) | 5 /min (2 workers) | Food + a cheap Entertainment contribution |
| **Hemp** | Arable soil (field) | 8 /min (3 workers) | Feeds rope and cloth — i.e. feeds your navy |
| **Cane** | Arable soil (plantation) | 10 /min (4 workers) | Feeds rum — i.e. feeds your population's morale |
| **Salt** | Tidal flat (salt pan) | 6 /min (2 workers) | Tier 0 only. Vulnerable to naval bombardment |

### 2.2 Refined goods

| Good | Made by | Inputs | Output | Workers |
|---|---|---|---|---|
| **Planks** | Sawmill | 12 Timber | 8 /min | 2 |
| **Bricks** | Kiln | 6 Clay + 2 Coal | 5 /min | 2 |
| **Rope** | Ropewalk | 6 Hemp | 5 /min | 2 |
| **Cloth** | Weavery | 8 Hemp | 5 /min | 3 |
| **Rigging** | Sail Loft | 4 Cloth + 3 Rope | 3 /min | 2 |
| **Bread** | Bakery | 8 Grain + 2 Coal | 10 /min | 2 |
| **Salt Fish** | Salt House | 8 Fish + 2 Salt | 8 /min | 2 |
| **Molasses** | Sugar Mill | 10 Cane | 6 /min | 2 |
| **Rum** | Distillery | 6 Molasses + 2 Coal | 5 /min | 3 |
| **Bar Iron** | Furnace | 6 Iron Ore + 4 Coal | 4 /min | 3 |
| **Tools** | Toolworks | 3 Bar Iron + 2 Planks | 3 /min | 2 |
| **Weapons** | Weaponsmith | 3 Bar Iron + 1 Planks | 2 /min | 2 |
| **Powder** | Powder Mill | 5 Nitre + 3 Coal | 4 /min | 3 |
| **Cannon** | Cannon Foundry | 6 Bar Iron + 2 Tools + 4 Coal | 1 /min | 4 |
| **Fine Cloth** | Weavery *(Rank IV upgrade)* | 4 Cloth + 1 Curios | 2 /min | 3 |

### 2.3 Imported goods

| Good | Source | Purpose |
|---|---|---|
| **Curios** | **Free ports only — cannot be produced** | Satisfies the Artificer and Sea Officer *Luxury* need; input to Fine Cloth |

This is deliberate. A high-tier settlement **cannot be fully self-sufficient**. To keep Artificers
happy you must either trade with free ports or take Curios by force. It guarantees that even a
turtling player has to care about the sea.

### 2.4 Abstract resources

| Resource | Source | Sink |
|---|---|---|
| **Coin** | Selling goods, contracts, prize money, loot, taxation | Construction, technology, mercenaries, tribute, free-port purchases |
| **Contentment** | Need satisfaction (per Station, per District) | Decays under unmet needs, danger, over-taxation, recent losses |
| **Notoriety** | Prizes taken, raids landed, contracts, free-port standing | Victory condition 5; unlocks some mercenaries; raises free-port prices against you |
| **Labour** | Population in worker slots | Consumed continuously by every staffed building |

---

## 3. The production chains

### 3.1 Shipbuilding — the spine of the game

```
 Forest ──► Lumber Camp ──► Timber ──► Sawmill ──► Planks ─┐
 Hemp   ──► Hemp Field  ──► Hemp   ──► Ropewalk ──► Rope ──┤
                                   └─► Weavery  ──► Cloth ─┴─► Sail Loft ──► Rigging ─┐
 Ore    ──► Ore Mine    ──► Iron Ore ┐                                                │
 Coal   ──► Coal Pit    ──► Coal ────┴─► Furnace ──► Bar Iron ────────────────────────┤
                                                                                      ▼
                                                                            Shipyard ──► SHIP
```

Per-hull requirements (see `COMBAT_DESIGN.md` for stats):

| Ship | Planks | Rope | Rigging | Bar Iron | Cannon | Coin | Build time |
|---|---|---|---|---|---|---|---|
| Longboat | 15 | 4 | — | — | — | — | 25 s |
| Cutter | 30 | 10 | 4 | 2 | — | 40 | 45 s |
| Sloop | 50 | 16 | 8 | 6 | 4 | 90 | 70 s |
| Fluyt | 70 | 20 | 10 | 8 | — | 120 | 85 s |
| Brig | 110 | 30 | 18 | 20 | 12 | 220 | 110 s |
| Bombard Ketch | 90 | 22 | 12 | 30 | 4 mortars | 260 | 120 s |
| Troopship | 120 | 34 | 20 | 14 | 6 | 240 | 115 s |
| Frigate | 200 | 55 | 34 | 45 | 28 | 480 | 170 s |
| Razee | 175 | 50 | 34 | 40 | 22 | 520 | 160 s |

### 3.2 Rum and contentment — the spine of your population

```
 Arable ──► Cane Field ──► Cane ──► Sugar Mill ──► Molasses ──┐
 Coal   ─────────────────────────────────────────────────────┴─► Distillery ──► Rum ──► Tavern ──► Contentment
```

One Tavern serves **40 population** and consumes **1 Rum/min**. A settlement of 200 needs 5 Taverns
and 5 Rum/min — exactly one Distillery running at full rate, which needs one Sugar Mill, which needs
one Cane Field. **Break any link and morale falls across the whole settlement within minutes.** This
is the intended shape: a visible, attackable, single-thread dependency that a player can choose to
make redundant at real cost.

### 3.3 Ordnance — the spine of your army

```
 Ore  ──► Ore Mine ──► Iron Ore ─┐
 Coal ──► Coal Pit ──► Coal ─────┴─► Furnace ──► Bar Iron ─┬─► Weaponsmith ──► Weapons ──► Barracks ──► SOLDIERS
                                                           ├─► Toolworks   ──► Tools   ──► (buildings, Cannon)
                                                           └─► Cannon Foundry ──► Cannon ──► Ships, Batteries, Gun Crews
 Nitre ──► Nitre Works ──► Nitre ─┬─► Powder Mill ──► Powder ──► ammunition upkeep for all gunpowder units
 Coal ────────────────────────────┘
```

**Powder is an upkeep, not a one-off.** Fusiliers, Grenadiers, Gun Crews, coastal batteries and
every gun-armed ship consume Powder while engaged. Run out and your gunpowder units revert to melee
at heavy penalty. Because Nitre is island-bound and rare, **the Powder chain is the most attackable
strategic dependency in the game** — and taking a rival's nitre island is often better than fighting
their fleet.

### 3.4 Food

```
 Water   ──► Fishing Wharf ──► Fish  ──► Salt House ──► Salt Fish   (keeps, ships well)
 Tidal   ──► Salt Pan      ──► Salt  ──┘
 Arable  ──► Grain Plot    ──► Grain ──► Bakery     ──► Bread       (highest food value)
 Arable  ──► Orchard       ──► Fruit ──────────────────► direct     (food + Entertainment)
 Wild    ──► Hunting Post  ──► Game  ──────────────────► direct     (early only, depletes)
```

Food value per unit: Game 1.0 · Fish 1.0 · Fruit 1.0 · Grain 1.0 · Salt Fish 1.8 · Bread 2.2.
**Variety bonus:** a district with ≥ 3 distinct food types in its granary gets **+10% Contentment**
on the Food need. Monoculture feeds you but does not make you happy.

### 3.5 Construction materials

```
 Timber ──► Sawmill ──► Planks ─┐
 Stone  ──► Quarry  ──► Stone ──┼──► construction sites (delivered by haulers, applied by builders)
 Clay   ──► Clay Pit ──► Bricks ┘        Bricks and Tools are required for Rank III+ buildings
```

---

## 4. Buildings

### 4.1 Storage

| Building | Capacity | Types | Cost | Notes |
|---|---|---|---|---|
| **Stockpile** | 200 | 4 filtered | 20 Planks | Open-air; contents visible to scouts; burns easily |
| **Warehouse** | 800 | all, filterable, priority-able | 60 Planks, 40 Stone | The logistics hub. Flow fields to warehouses are permanently cached |
| **Granary** | 600 | food only, +25% spoilage resistance | 45 Planks, 20 Bricks | Food spoils at 1%/min outside a Granary |
| **Depot** | 300 | all | 40 Planks, 20 Stone | A forward relay: put one at the top of a cliff to halve lift traffic |

### 4.2 Selected production buildings

| Building | Rank | Cost | Slots | Footprint | Notes |
|---|---|---|---|---|---|
| Lumber Camp | I | 25 Planks | 3 | 3×3 | Rate falls as nearby forest thins |
| Fishing Wharf | I | 30 Planks | 3 | 3×4, water-adjacent | Depletes local fish density |
| Grain Plot | I | 20 Planks | 4 | 6×6 arable | Seasonal? No — continuous, for RTS legibility |
| Sawmill | II | 45 Planks, 15 Stone | 2 | 4×4 | |
| Quarry | II | 40 Planks | 4 | 4×4 on rock | |
| Ore Mine | II | 60 Planks, 20 Stone | 4 | 4×4 on vein | |
| Coal Pit | II | 55 Planks, 20 Stone | 4 | 4×4 on seam | |
| Ropewalk | III | 70 Planks, 20 Stone | 2 | 3×8 (long, awkward — a real layout constraint) | |
| Weavery | III | 70 Planks, 30 Bricks | 3 | 4×4 | |
| Furnace | III | 80 Planks, 60 Stone, 10 Tools | 3 | 5×5 | Emits smoke — visible from far away when running |
| Distillery | III | 90 Planks, 40 Bricks | 3 | 4×5 | |
| Toolworks | III | 90 Planks, 40 Bricks, 10 Bar Iron | 2 | 4×4 | |
| Powder Mill | IV | 120 Planks, 80 Bricks, 20 Tools | 3 | 5×5 | **Explodes if destroyed** — area damage. Site it away from your town |
| Cannon Foundry | IV | 150 Planks, 100 Stone, 30 Tools | 4 | 6×6 | |
| Shipyard | III | 140 Planks, 60 Stone, 15 Tools | 4 | 8×8 water-adjacent | Builds Brig and above |
| Dock | II | 60 Planks, 20 Stone | 2 | 4×6 water-adjacent | Builds up to Fluyt; loads/unloads cargo |

Full tables live in content data; this is the shape, not the whole list.

---

## 5. Population and labour

### 5.1 Housing

| Housing | Rank | Station | Capacity | Cost | Needs to keep occupied |
|---|---|---|---|---|---|
| **Shelter** | I | Castaway | 5 | 10 Planks | Food |
| **Longhouse** | I | Deckhand | 10 | 25 Planks | Food, Water, Rum |
| **Freehold Row** | II | Freeholder | 8 | 50 Planks, 20 Bricks | + Clothing, Entertainment |
| **Artificer Quarters** | III | Artificer | 6 | 80 Planks, 50 Bricks, 10 Tools | + Safety, Luxury |
| **Officers' House** | IV | Sea Officer | 4 | 120 Planks, 80 Bricks, 20 Tools, 10 Fine Cloth | + Prestige |

### 5.2 Needs

| Need | Satisfied by | Consumption |
|---|---|---|
| **Food** | Any food good via Granary + district coverage | 0.20 /person/min (weighted by food value) |
| **Water** | Well or Cistern service radius | Service coverage, not a hauled good |
| **Rum** | Tavern service radius | 1 Rum/min per 40 people |
| **Clothing** | Cloth delivered to a Market | 0.02 Cloth/person/min |
| **Entertainment** | Tavern, Bathhouse, Fruit availability, festivals | Coverage + optional Coin spend |
| **Safety** | Garrison presence, walls, no recent attacks, Gaol coverage | Situational |
| **Employment** | Ratio of filled worker slots to working-age population | Situational |
| **Luxury** | Curios at a Market | 0.01 Curios/person/min |
| **Prestige** | Fine Cloth, Council Hall, victory progress, Notoriety | Situational |

Unmet needs reduce Contentment on a slow tick; the exact curve is in balance data. Needs are
evaluated **per District**, so coverage geography matters — a Tavern on the beach does nothing for
the cliff-top quarter.

### 5.3 Work rate

```
effective_output = base_rate
                 × station_skill        (Castaway 0.60 · Deckhand 1.00 · Freeholder 1.15 · Artificer 1.30)
                 × contentment_modifier (0.55 … 1.15, see GAME_DESIGN.md §8)
                 × staffing_ratio       (filled slots / total slots)
                 × supply_modifier      (1.0 if inputs present; 0 if starved)
                 × tech_modifier
```

A building with no inputs produces nothing and reports **"Starved: no Coal"** in its panel and in
the Logistics Overlay. It does not silently idle.

---

## 6. Logistics mathematics

This is the part players will feel most and understand least, so the numbers are explicit and are
surfaced in the UI.

### 6.1 Hauler throughput

```
round_trip = (2 × distance ÷ speed) + load_time + unload_time
throughput_per_hauler = carry_capacity ÷ round_trip
```

Deckhand: speed **1.4 m/s** (× road multiplier), carry **8 units**, load/unload **1.5 s** each.

| Distance | Round trip | Per hauler | Haulers for 12/min |
|---|---|---|---|
| 20 m | 31.6 s | 15.2 /min | 0.8 |
| 40 m | 60.1 s | 8.0 /min | 1.5 |
| 60 m | 88.7 s | 5.4 /min | **2.2** |
| 120 m | 174.4 s | 2.8 /min | **4.4** |
| 200 m | 288.7 s | 1.7 /min | **7.2** |

**Doubling the distance roughly halves throughput.** A sawmill 200 m from its lumber camp costs you
seven haulers instead of two — five workers you cannot put in a building. This is the whole game of
settlement layout, expressed in one table.

**Roads:** Dirt +10%, Gravel +25%, Stone +40% speed. **Wagons** (Rank III, built at a Depot, need
Stone Road) carry **40 units** at 1.2 m/s — a 3.6× throughput multiplier on long hauls, at the cost
of a road network that raiders can crater.

### 6.2 Vertical throughput

| Connector | Throughput | Constraint |
|---|---|---|
| **Ramp** | Walking speed, unlimited units | Only a 1-tier rise on shallow slope; big footprint |
| **Stair** | Walking speed × 0.6; ~4 units abreast | Cheap; a queue forms above ~25 haulers/min |
| **Rope Bridge** | Walking speed × 0.5; **max 3 units on it at once** | Hard cap ≈ 18 goods/min. Fragile |
| **Cargo Lift** | **90 goods/min** (3 crates × 10 goods per 20 s cycle), 1 operator | Goods only, no units. Costs 60 Planks, 30 Rope, 10 Bar Iron |
| **Crane Head** | **150 goods/min** ship↔shore | Dockside only. Without one, ships load at hauler speed (~5/min per hauler) |
| **Winch Tower** | **180 goods/min** + carries units | Rank IV. 150 Planks, 60 Rope, 40 Bar Iron, 20 Tools |

**Worked example — the cliff-top battery problem.** A Tier-4 Coastal Battery consumes 3 Powder/min
while firing. Its supply line from the Tier-1 Powder Mill is 140 m plus one connector.

- Via **Rope Bridge** (cap 18/min): feasible on paper, but the same bridge is your garrison's route and your repair materials' route. In practice it saturates and the battery goes quiet mid-assault.
- Via **Cargo Lift** (90/min): comfortable — but that is 60 Planks, 30 Rope, 10 Bar Iron and an operator, spent before the battery has fired a shot.
- Via **Depot at the top** pre-stocked with 300 Powder: 100 minutes of fire with no live supply line at all — the correct answer, and one players have to discover.

That progression *is* the vertical-building game.

### 6.3 The job market

Every tick, open jobs are scored and assigned to idle workers in a strictly ordered pass:

```
score = base_priority(job_type)
      × building_priority(1–5, player-set)
      × urgency(buffer fullness / starvation)
      ÷ (1 + travel_cost)        // HPA* distance, including connector costs
```

Ties break on entity id (determinism). Players tune it with **building priority**, **storage
filters and priorities**, **dedicated hauler assignment** (pin N workers to one route), and by
building **Depots** to shorten the longest legs.

---

## 7. Trade and free ports

### 7.1 Prices

Each free port has a base price per good, modulated by local supply/demand and by your **Relations**
and **Notoriety**:

```
sell_price = base × port_demand × (1 + relations_bonus) × (1 − notoriety_penalty)
buy_price  = base × port_supply × (1 − relations_bonus) × (1 + notoriety_penalty)
```

Indicative base sell prices (Coin/unit): Timber 1 · Stone 1 · Planks 3 · Salt Fish 4 · Bread 5 ·
Rope 5 · Cloth 6 · Rigging 12 · Bar Iron 8 · Tools 14 · Rum 15 · Weapons 18 · Powder 22 · Cannon 60 ·
Curios (buy only) 35.

**Prices move with volume.** Dumping 500 Rum into one port craters its rum price for several minutes.
Trade is a route-planning problem, not an infinite money faucet.

### 7.2 Trade routes

A **trade route** assigns a Fluyt (or any ship with cargo) to a repeating circuit: load goods at your
Dock, sail to a port, sell, optionally buy, return. Routes run automatically and are **fully
interceptable** — this is the primary target of naval raiding and the main reason Sloops exist.

Route income scales with cargo size × price differential ÷ round-trip time. A long route to a
distant, high-price port earns more per trip and is far more exposed.

### 7.3 Contracts

At Relations ≥ 50, ports offer timed contracts: *deliver 200 Planks in 6 minutes*, *sink the raider
harassing our shipping*, *escort our convoy to the next port*. Rewards: Coin, Notoriety, Relations,
and occasionally a unique item or mercenary unlock. Contracts are **visible to all players who have
that port's Relations tier** — competition for the same contract is intended.

### 7.4 Taxation

The player sets a tax rate (0–100%) on Coin-generating activity. Income scales linearly; Contentment
penalty scales **super-linearly** above 40%. High tax is a legitimate short-term lever with a real
bill attached.

---

## 8. Unrest as a weapon

Because Contentment is driven by physical goods reaching physical districts, an attacker can
engineer a rival's collapse without touching a single soldier:

| Attack | Consequence |
|---|---|
| Burn the Cane Field | Molasses stops → Rum stops → Taverns dry → Contentment falls across the settlement in ~4 min |
| Cut the Cargo Lift to the upper quarter | That district's food stops arriving → localised Unrest → workers desert their buildings |
| Blockade the harbour | No Curios → Artificer Luxury unmet → the Powder and Cannon chains lose their skilled staff |
| Sink the fishing fleet | Food variety collapses → the variety bonus is lost → a slow, broad Contentment sag |
| Raid the Granary | Immediate food shortage + looted goods for the raider |

Defensive counters are all real design choices, not just "build more walls": redundant chains, a
second Distillery on a different island, Depots pre-stocked with buffer stock, escorted trade routes,
warehouse dispersal, and Gaol/garrison coverage to suppress crime while you recover.

---

## 9. Rank advance costs (first pass)

| Rank | Coin | Planks | Stone | Other | Time | Structural requirement |
|---|---|---|---|---|---|---|
| **II Stockade** | 150 | 120 | 60 | — | 60 s | 20 pop, a Warehouse, a Stair or Ramp |
| **III Free Port** | 450 | 300 | 200 | 40 Bricks | 110 s | 60 pop, 12 Freeholders, a Trading Post, a Shipyard |
| **IV Marque** | 1,100 | 600 | 400 | 120 Bricks, 60 Tools | 170 s | 120 pop, 20 Artificers, a Foundry, 2 claimed islands |
| **V Admiralty** | 2,400 | 1,000 | 800 | 250 Bricks, 150 Tools, 40 Cannon | 240 s | 200 pop, housed Sea Officers, a Council Hall |

Rank advance is announced to all players when it begins, with the remaining time visible on scouting.

---

## 10. Prototype economy (Milestone 3 only)

Deliberately tiny, to prove the machinery rather than the design:

| Element | Prototype scope |
|---|---|
| Goods | **Timber, Food, Stone, Coin** only |
| Harvest | Lumber Camp → Timber (from forest nodes); Fishing Wharf → Food (from water) |
| Refine | **None.** No production chains in the prototype |
| Storage | Warehouse (single type, 500 capacity, all goods) |
| Housing | House (capacity 5) — provides population cap only, no needs |
| Buildings | Warehouse, House, Lumber Camp, Fishing Wharf, Dock |
| Workers | 10 per player, jobs: Harvest, Haul, Construct |
| Coin | Starting stock only; no trade |
| Population needs | **None** — deferred to Milestone 8 |

Everything else in this document is Milestone 8+ work and must not be started before the prototype
passes its acceptance tests.

---

*Related:* `GAME_DESIGN.md` §7–11 · `COMBAT_DESIGN.md` (what the economy pays for) ·
`DEVELOPMENT_ROADMAP.md`
