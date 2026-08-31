# BRINEHOLD — Combat Design

**Status:** Proposed. All values are **first-pass targets for implementation**, not final balance.
Everything here lives in content data and is tuned without code changes.

**Conventions:** damage is per hit; cooldown in seconds; range in metres; speed in m/s; morale 0–100.
All combat resolves **only on the authoritative server** (`MULTIPLAYER_ARCHITECTURE.md` §2.3).

---

## 1. Principles

1. **Combat is decided before contact.** Positioning, elevation, supply and composition should matter more than click speed.
2. **Morale, not just hit points.** Armies break. A routed army that escapes can be rallied and is a resource, not a write-off.
3. **Terrain is a weapon.** Elevation, forest, beaches, walls, stairs and shoals produce real, legible modifiers.
4. **Naval combat is manoeuvre.** Broadside arcs, wind and shot selection — not a sailing simulator, not an arcade shooter.
5. **Capture beats destruction.** Boarding a ship and taking it is the highest-value play available, and it costs you a dedicated unit type to enable.
6. **Everything gunpowder consumes supply.** Guns without Powder are clubs. Logistics reaches all the way onto the battlefield.
7. **No hard counters, strong soft counters.** Every unit loses badly to something, but nothing is useless against anything.

---

## 2. Damage model

```
hit_chance   = base_accuracy × elevation_acc × cover_mod × movement_mod × morale_acc
raw          = damage × type_vs_armour[damage_type, armour_class]
after_armour = max(raw × (1 − armour_reduction), raw × 0.15)      // armour never fully negates
final        = after_armour × elevation_dmg × flank_mod × charge_mod × supply_mod
```

- **Armour floor:** a minimum 15% of pre-armour damage always lands. No unit is immune to anything.
- **Supply modifier:** gunpowder units at 0 Powder fall back to melee at **0.4×** damage.
- Damage is applied in a deterministic, ordered pass; simultaneous kills resolve by entity id.

### 2.1 Damage types × armour classes

| ↓ Damage \ Armour → | Unarmoured | Padded | Cuirass | Wood structure | Stone structure | Light hull | Heavy hull |
|---|---|---|---|---|---|---|---|
| **Blade** | 1.00 | 0.75 | 0.45 | 0.25 | 0.05 | 0.10 | 0.05 |
| **Shot** (musket) | 1.00 | 0.85 | 0.60 | 0.30 | 0.10 | 0.15 | 0.08 |
| **Blast** (grenade) | 1.10 | 1.00 | 0.85 | 0.90 | 0.40 | 0.55 | 0.35 |
| **Round shot** | 1.20 | 1.15 | 1.10 | 1.60 | 1.00 | 1.00 | 0.80 |
| **Chain shot** | 0.60 | 0.55 | 0.50 | 0.40 | 0.15 | *sail 1.80* | *sail 1.60* |
| **Grape shot** | 1.50 | 1.25 | 0.80 | 0.20 | 0.05 | *crew 2.00* | *crew 1.80* |
| **Mortar shell** | 1.00 | 0.95 | 0.90 | 1.80 | 1.40 | 0.70 | 0.55 |
| **Demolition** (Sapper) | 0.20 | 0.15 | 0.10 | 3.00 | 2.20 | 0.30 | 0.20 |

Armour reduction by class: Unarmoured 0% · Padded 20% · Cuirass 40% · Wood structure 30% ·
Stone structure 55% · Light hull 25% · Heavy hull 45%.

---

## 3. Morale

Morale is the systemic backbone of land combat.

| State | Morale | Effect |
|---|---|---|
| **Steady** | 75–100 | +10% damage, holds formation, will charge |
| **Shaken** | 45–74 | −10% accuracy, slower to obey new orders |
| **Wavering** | 25–44 | −30% accuracy, −20% damage, will not charge, may fall back |
| **Routed** | < 25 | Ignores orders, flees toward the nearest friendly building; deals no damage; takes +50% damage from pursuit |

**Morale loss** from: taking casualties in the unit's group, being flanked (attacked from > 90° off facing) −15, being charged by cavalry-equivalent melee −10, artillery landing nearby −12 per shell, losing a Bosun within 20 m −20, fighting uphill −8, being outnumbered locally, being at 0 Powder −10, and having no line of retreat.

**Morale gain** from: winning a local engagement, a Bosun's aura (+15 within 20 m), being on high
ground (+8), fighting inside your own territory (+5), garrison structures (+10), and time out of
combat (+4/s after 5 s clear).

**Routed units can be rallied** — walk them back to a Bosun or a friendly Barracks. A routed unit that
escapes and rallies is a fully functional unit again at 40 morale. **This is why chasing a broken
enemy is worth doing, and why "you won the fight" is not the same as "you won the battle."**

---

## 4. Land units

| Unit | Rank | HP | Armour | Damage | Type | Range | CD | Speed | Morale | Pop | Cost | Train |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **Cutthroat** | I | 60 | Unarmoured | 9 | Blade | 1.0 | 1.0 | 3.2 | 55 | 1 | 35 Food, 1 Weapon, 15 Coin | 12 s |
| **Buccaneer** | III | 110 | Padded | 18 | Blade | 1.0 | 1.1 | 3.0 | 80 | 2 | 60 Food, 3 Weapons, 45 Coin | 22 s |
| **Fusilier** | II | 70 | Padded | 22 | Shot | 22 | 3.5 | 2.6 | 65 | 1 | 55 Food, 2 Weapons, 35 Coin | 18 s |
| **Sharpshot** | IV | 55 | Unarmoured | 30 | Shot | 34 | 6.0 | 2.6 | 60 | 1 | 65 Food, 3 Weapons, 60 Coin | 24 s |
| **Grenadier** | IV | 95 | Padded | 26 (3 m splash) | Blast | 12 | 5.0 | 2.4 | 75 | 2 | 70 Food, 3 Weapons, 2 Powder, 70 Coin | 26 s |
| **Gun Crew** | III | 130 | Padded | 70 (2 m splash) | Round shot | 45 | 9.0 | 1.2 | 50 | 3 | 90 Food, 1 Cannon, 4 Tools, 150 Coin | 40 s |
| **Marine** | III | 85 | Padded | 16 / 12 melee | Shot / Blade | 18 | 3.2 | 2.8 | 75 | 1 | 60 Food, 2 Weapons, 45 Coin | 20 s |
| **Boarding Crew** | II | 80 | Padded | 20 (×2.5 vs crew) | Blade | 1.0 | 0.9 | 3.0 | 70 | 1 | 50 Food, 2 Weapons, 30 Coin | 16 s |
| **Sapper** | III | 70 | Padded | 120 (structures only) | Demolition | 2.0 | 6.0 | 2.6 | 45 | 1 | 60 Food, 2 Tools, 50 Coin | 22 s |
| **Bosun** *(officer)* | III | 120 | Cuirass | 20 | Blade | 1.0 | 1.2 | 2.8 | 95 | 2 | 80 Food, 2 Weapons, 100 Coin | 30 s |

**Upkeep:** every military unit consumes **0.05 Food/min**. Gunpowder units (Fusilier, Sharpshot,
Grenadier, Gun Crew, Marine) additionally consume **0.05–0.20 Powder/min while engaged**.

### 4.1 Role sketch

- **Cutthroat** — mass, screen, raid. Dies to anything with armour or range. Cheap enough to trade.
- **Buccaneer** — the melee decision-maker. Charges, breaks lines, holds stairs.
- **Fusilier** — the line. Strong in numbers, in formation, on a wall, or behind a chokepoint. Helpless when charged.
- **Sharpshot** — punishes officers and Gun Crews, and is the reason high ground is contested. Range × 1.3 on a higher tier than its target.
- **Grenadier** — cracks formations and wooden structures. Friendly fire is on; splash does 50% to your own.
- **Gun Crew** — the siege answer. Immobile in practice; must be escorted; dies instantly to a cavalry-style charge or a sharpshooter.
- **Marine** — the amphibious specialist: no landing penalty, fights competently at both ranges, costs more than a Fusilier for the privilege.
- **Boarding Crew** — enables ship **capture**. Useless on land against armour, decisive on a deck.
- **Sapper** — destroys walls, gates, bridges, rope bridges, cargo lifts and roads at speed. The economic-warfare unit.
- **Bosun** — morale anchor. Losing one is a −20 morale shock across the group; protecting one is a real tactical objective.

### 4.2 Stances

**Aggressive** (pursue), **Defensive** (hold position, engage in range), **Hold Fire** (no auto-attack —
essential for ambushes and for not revealing your position), **Skirmish** (retreat when an enemy
closes to melee).

### 4.3 Formations

Line (+10% ranged, wide frontage), Column (+15% move, terrible in a fight), Loose (−40% splash
damage taken, −10% ranged), Square (+25% vs charge, −20% ranged). Formation is applied to a control
group and is maintained by the movement system; a routed unit leaves formation.

---

## 5. Terrain

| Terrain | Effect |
|---|---|
| **Higher tier than target** | +15% ranged damage, +20% accuracy, **+30% range** for ranged units, +8 morale, +vision |
| **Lower tier than target** | −15% accuracy, −8 morale |
| **Forest** | Cover: −25% incoming ranged damage; blocks line of sight; −20% move; breaks formation |
| **Beach (landing side)** | −20% damage and −15 morale for units that disembarked in the last 8 s (Marines exempt) |
| **Shallow water (wading)** | −50% move, −20% damage, cannot use formations |
| **Road** | +10 / +25 / +40% move by road tier |
| **Stair / ramp** | Attacker fights at a −15% penalty; defender at the top gets the full high-ground bonus. **A defended stair is the best land position in the game** |
| **Rope bridge** | Max 3 units; anyone on it takes +50% damage and cannot return fire |
| **Behind a wall** | +40% cover vs Shot, no cover vs Blast or Round shot |
| **Storm zone** | −20% ranged accuracy for everyone, −25% ship speed |

---

## 6. Naval combat

### 6.1 Ship statistics

| Ship | Rank | Hull | Sail | Crew | Cargo | Guns/side | Gun rng | Reload | Turn °/s | Speed | Draught | Vision |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **Longboat** | I | 120 | 30 | 8 | 20 | — | — | — | 90 | 4.5 | Shallow | 25 |
| **Cutter** | II | 200 | 80 | 12 | 30 | 1 | 25 | 8 | 45 | 8.5 | Shallow | **60** |
| **Sloop** | II | 380 | 140 | 25 | 40 | 4 | 30 | 9 | 32 | 7.5 | Shallow | 45 |
| **Fluyt** | III | 520 | 180 | 20 | **250** | — | — | — | 18 | 5.5 | Medium | 35 |
| **Brig** | III | 750 | 260 | 55 | 60 | 8 | 34 | 10 | 24 | 6.5 | Medium | 45 |
| **Bombard Ketch** | IV | 600 | 200 | 40 | 30 | 2 mortars | **70** *(land only)* | 16 | 20 | 5.0 | Medium | 40 |
| **Troopship** | III | 700 | 240 | 35 | 120 + **12 troop slots** | 3 | 30 | 11 | 20 | 5.8 | Medium | 40 |
| **Frigate** | IV | 1400 | 420 | 110 | 80 | 16 | 40 | 11 | 16 | 6.2 | Deep | 55 |
| **Razee** | V | 1150 | 400 | 90 | 40 | 12 | 38 | 10 | 20 | 7.2 | Deep | 50 |

**Three damage pools per ship:**

- **Hull** — reaching 0 sinks the ship. Below 30% it takes on water and loses speed.
- **Sail** — reaching 0 immobilises it (dead in the water, still fights, cannot flee).
- **Crew** — drives reload speed (`reload × (1 + 0.8 × crew_lost_fraction)`) and boarding defence. At 0 crew the ship is derelict and can be claimed by anyone who touches it.

### 6.2 Shot selection

Each ship carries a loadout, switchable at sea with a 6 s changeover:

| Shot | Hull | Sail | Crew | Use |
|---|---|---|---|---|
| **Round shot** | 1.00 | 0.30 | 0.20 | Sink it |
| **Chain shot** | 0.20 | **1.80** | 0.30 | Immobilise it — for prizes and for stopping a runner |
| **Grape shot** | 0.15 | 0.30 | **2.00** | Strip the crew before boarding; anti-boarder defence |

The chain-then-grape-then-board sequence is the intended skill expression of naval play.

### 6.3 Broadsides, arcs and wind

- Guns fire in **port and starboard arcs** (roughly 45°–135° off the bow each side). Nothing fires forward or aft except bow chasers on the Frigate and Razee (2 guns, half damage).
- **Reload is per side.** Firing a broadside and then turning to present the fresh side is the core manoeuvre.
- **Wind** is a single prevailing vector per map, visible in the UI, rotating slowly over the match. Speed multiplier by heading: running before it **1.25×**, beam reach 1.0×, close-hauled 0.75×, directly into it **0.45×**. It is a readable strategic layer — it decides who can disengage — without being a sailing simulator.
- **Shallows and draught.** Deep-draught ships (Frigate, Razee) cannot enter shallow water at all. A Sloop that runs into the shoals is safe from a Frigate. Shoal maps favour light fleets; open water favours heavy ones.

### 6.4 Boarding and prize-taking

**Prerequisites:** the target is within 6 m, and either its Sail is below 40% or its speed is below
2.0 m/s. A grapple then locks both ships together.

```
attacker_power = Σ(crew_strength) + Σ(Boarding Crew × 3.0) + morale_mod + (elevation? no) + surprise_mod
defender_power = Σ(crew_strength) + Σ(embarked land units × 0.5) + defensive_mod(+25% own ship)
```

Resolved as repeated 1-second rounds, each removing crew from both sides in proportion to the power
ratio. When the defender reaches 0 effective crew, the ship is **captured**: it changes owner at 30%
hull, 0 sail, and is crewed from the boarders. It must limp to a friendly dock to be repaired and
re-crewed.

**Captured ships keep their class** — taking an enemy Frigate with two Sloops and a deck full of
Boarding Crews is the single biggest swing available in a match, and is exactly the fantasy the game
is selling.

**Counters:** grape shot, embarked Marines, keeping your Sail alive, and speed.

### 6.5 Fleet control

Ships are directly and manually controllable in real time: move orders, attack orders, a
**broadside-facing order** (hold a heading relative to a target), formations (Line Ahead, Line
Abreast, Escort, Screen) and fleet stances (Engage, Escort, Patrol, Blockade, Evade).

Fleets are control-group-assignable like land units. The **Fleet Panel** shows hull/sail/crew/cargo
bars per ship, so a player commanding twelve ships can see at a glance which one is about to sink and
which one is ripe to be boarded.

### 6.6 Blockade

A fleet holding station in a harbour's mouth arc for 30 s establishes a **Blockade**:

- Trade routes into and out of that harbour are halted.
- Trade income from that harbour drops to 0.
- Cargo transfers at that dock are suspended.
- The defender is notified prominently, with a clear map marker.

Blockades are broken by destroying, boarding or driving off the blockading fleet, or by coastal
batteries making the station untenable. **A blockade is a slow win condition against a naval-weak
player and a real reason to contest the sea early.**

---

## 7. Siege and structures

### 7.1 Defensive structures

| Structure | Rank | HP | Armour | Damage | Range | Cost | Notes |
|---|---|---|---|---|---|---|---|
| **Palisade** | II | 400 | Wood | — | — | 15 Planks/segment | Blocks movement; burns |
| **Watchtower** | II | 500 | Wood | 14 Shot | 26 | 40 Planks, 10 Stone | +vision; garrison 3 (their range applies) |
| **Gatehouse** | II | 700 | Wood | — | — | 50 Planks, 30 Stone | Passable by owner and allies only |
| **Stone Wall** | III | 1200 | Stone | — | — | 40 Stone/segment | Units can be garrisoned on top (full high-ground bonus) |
| **Coastal Battery** | IV | 1100 | Stone | 90 Round shot | **60** *(sea only)* | 120 Stone, 4 Cannon, 20 Tools | Consumes 3 Powder/min while firing. The answer to fleets |
| **Bastion** | V | 2200 | Stone | 60 Round shot ×2 | 45 | 300 Stone, 8 Cannon, 60 Tools | Garrison 12; fires on land and sea |
| **Sea Chain** | V | 900 | — | — | — | 80 Bar Iron, 40 Rope | Spans a harbour mouth; stops all ships until destroyed |

### 7.2 Siege interactions

- **Gun Crews and Bombard Ketches** are the intended answer to stone. Blade and Shot units barely scratch it (see the multiplier table).
- **Sappers** are the answer to *infrastructure* — bridges, rope bridges, cargo lifts, roads and gates — and are far faster at it than artillery.
- **Fire.** Wooden structures set alight by Blast or Mortar damage keep burning until a worker extinguishes them. Fire spreads between adjacent wooden buildings. This is why dense wooden settlements are a risk and why Bricks matter.
- **Repair.** Workers repair structures using the same materials that built them. Repairing under fire is a real tactic and a real cost.
- **Capture, not just destroy.** Buildings dropped below 15% HP with no defenders in radius can be **captured** by an adjacent enemy unit over 10 s. Capturing a Shipyard is better than burning it.

---

## 8. Amphibious operations

```
 Load  → Troopship / Longboat (12 / 4 troop slots)  → Sail → approach beach → Disembark (4 s per unit)
                                                                      │
                                                    landing penalty: −20% damage, −15 morale, 8 s
                                                    (Marines exempt)
```

- **Landing zones:** Tier 0/1 beach cells only, until the Rank IV **Grapple Assault** technology allows Marines and Boarding Crews to scale a one-tier cliff (slowly, and under fire).
- **Naval gunfire support:** ships in range can bombard the beachhead. Bombard Ketches out-range Coastal Batteries (70 vs 60) and are the correct tool for cracking a defended harbour — but they are helpless against warships and must be escorted.
- **Defender toolkit:** Coastal Batteries, Watchtowers for early warning, Sea Chains, beach Palisades, pre-positioned Gun Crews, and Fusiliers on the tier above the beach (full high-ground bonus onto a penalised landing force).
- **Raid vs invasion.** A Sloop and 8 Cutthroats burning a Cane Field is a raid: cheap, fast, and it can cripple a rival's morale economy for four minutes. A Troopship convoy with Gun Crews under Frigate escort is an invasion. Both must be viable at their price point; if raiding is not profitable, the raid design has failed.

---

## 9. Vision, detection and fog

- Vision radius per unit/building; **+1 tier of elevation grants +25% vision radius** and lets a unit see over forest one tier below.
- Forest blocks line of sight at the same tier or above.
- Ships have the largest vision; the Cutter is the dedicated scout at 60 m.
- **Firing reveals you.** A unit that fires becomes visible to the target's owner for 3 s regardless of fog. This makes "Hold Fire" a real tactical stance, and ambushes a real tactic.
- **Wakes.** Moving ships leave a visible wake at longer range than the ship itself, giving a directional contact without full detail (replication Tier E). The Sablewake doctrine suppresses this.
- No stealth units in v1. Concealment comes from terrain, fog and discipline.

---

## 10. Balance framework

- **Cost efficiency parity.** Equal Coin-and-materials investment in any two units should trade at roughly 1:1 in their *intended* matchup and 1:2.5 or worse in their counter matchup.
- **Time to kill.** Land skirmishes should resolve in **8–20 s**; naval engagements in **30–90 s**. Long enough to react and micro, short enough to feel decisive.
- **No unit is a hard counter.** The worst matchup multiplier in the game is capped at 3×.
- **Army supply ceiling.** Population cost plus Food/Powder upkeep is the real army cap, not an arbitrary unit limit. A player who out-economies their opponent should be able to field a bigger army — that is the point of the economy.
- **Defender's advantage** is deliberately strong (walls, high ground, batteries, short supply lines) so that turtling is *viable*, and **economic warfare** (`ECONOMY_DESIGN.md` §8) is the intended counter to turtling rather than an unwinnable frontal assault.

---

## 11. Prototype combat (Milestone 3 only)

Everything above is Milestone 9+ work. The prototype implements only:

| Element | Prototype scope |
|---|---|
| Land unit | **One:** Cutthroat (60 HP, 9 Blade, 1 m, 1.0 s CD, 3.2 m/s) |
| Ship | **One:** Cutter (200 hull, 1 gun, 25 m range, 8 s reload) |
| Damage | Flat: `damage` applied on cooldown, no armour table, no accuracy roll |
| Morale | **None** |
| Terrain modifiers | **None** |
| Structures | Buildings have HP and can be destroyed. No walls, no towers |
| Boarding / capture | **None** |
| Win condition | Destroy the opponent's Warehouse |

The prototype exists to prove that combat resolves **server-side**, that both clients see identical
results, and that a win condition fires correctly for both players — nothing more.

---

*Related:* `GAME_DESIGN.md` §12–15 · `ECONOMY_DESIGN.md` (what pays for all this) ·
`MULTIPLAYER_ARCHITECTURE.md` (why the server does the maths)
