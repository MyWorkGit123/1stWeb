using System.Collections.Generic;
using Brinehold.Core.Collections;
using Brinehold.Core.Math;
using Brinehold.Core.Random;
using Brinehold.Sim.Commands;
using Brinehold.Sim.Content;
using Brinehold.Sim.Nav;
using Brinehold.Sim.Systems;
using Brinehold.Sim.Vision;

namespace Brinehold.Sim.World
{
    /// <summary>
    /// The authoritative simulation.
    ///
    /// One instance of this class is the entire truth of a match. The server owns it; clients own a
    /// replica that is corrected from it. It has no dependency on Unity, no floating point and no
    /// wall-clock time, so a given command stream always produces the same result on every machine.
    /// </summary>
    public sealed class SimWorld
    {
        public readonly MatchConfig Config;
        public readonly EntityStore Entities;
        public readonly NavGrid Nav;
        public readonly PathFinder Path;
        public readonly FogGrid Fog;
        public readonly DeterministicRandom Rng;
        public readonly PlayerState[] Players;

        /// <summary>Ticks elapsed. The simulation's only clock.</summary>
        public uint Tick { get; private set; }

        public bool MatchOver { get; private set; }
        public int WinningTeam { get; private set; } = -1;

        /// <summary>Events produced this tick. Drained by the replication layer after Step().</summary>
        public readonly List<SimEvent> Events = new List<SimEvent>(256);

        private readonly List<Command> _pendingCommands = new List<Command>(64);
        private readonly ISimSystem[] _systems;

        /// <summary>Scratch buffer reused by systems that need a temporary entity list.</summary>
        internal readonly List<int> Scratch = new List<int>(256);

        public SimWorld(MatchConfig config)
        {
            Config = config;
            Entities = new EntityStore();
            Nav = new NavGrid(config.MapWidth, config.MapHeight);
            Path = new PathFinder(Nav);
            Fog = new FogGrid(config.MapWidth, config.MapHeight, config.PlayerCount);
            Rng = new DeterministicRandom(config.Seed);

            Players = new PlayerState[config.PlayerCount];
            for (int i = 0; i < config.PlayerCount; i++)
            {
                byte team = i < config.Teams.Length ? config.Teams[i] : (byte)i;
                string name = i < config.PlayerNames.Length ? config.PlayerNames[i] : $"Player {i + 1}";
                Players[i] = new PlayerState((byte)i, name, team);
            }

            // Declared, fixed execution order. Changing this order changes the simulation, so it
            // lives in one place where a reviewer can see it.
            _systems = new ISimSystem[]
            {
                new CommandIngestSystem(),
                new MovementSystem(),
                new HarvestSystem(),
                new ConstructionSystem(),
                new ProductionSystem(),
                new CombatSystem(),
                new DeathSystem(),
                new VisionSystem(),
                new VictorySystem()
            };
        }

        // ------------------------------------------------------------------ command intake

        /// <summary>
        /// Queues a command for execution on the next tick. The server calls this after validating
        /// the sender; the command itself is validated inside the tick, at the moment it executes,
        /// because affordability can change between issue and execution.
        /// </summary>
        public void EnqueueCommand(Command command) => _pendingCommands.Add(command);

        internal List<Command> PendingCommands => _pendingCommands;

        // ------------------------------------------------------------------ the tick

        /// <summary>Advances the simulation by exactly one tick.</summary>
        public void Step()
        {
            Events.Clear();
            Tick++;

            for (int i = 0; i < _systems.Length; i++)
                _systems[i].Execute(this);
        }

        /// <summary>Convenience for tests and headless runs.</summary>
        public void Step(int ticks)
        {
            for (int i = 0; i < ticks; i++) Step();
        }

        // ------------------------------------------------------------------ spawning

        public EntityId SpawnUnit(EntityKind kind, byte owner, Fix2 position)
        {
            PrototypeContent.UnitStats stats = PrototypeContent.ForKind(kind);
            EntityId id = Entities.Create(kind, owner);
            int i = id.Index;

            Entities.Position[i] = position;
            Entities.Domain[i] = stats.Domain;
            Entities.MoveSpeed[i] = stats.MoveSpeed;
            Entities.Health[i] = stats.MaxHealth;
            Entities.MaxHealth[i] = stats.MaxHealth;
            Entities.VisionRange[i] = stats.VisionRange;
            Entities.AttackDamage[i] = stats.AttackDamage;
            Entities.AttackRange[i] = stats.AttackRange;
            Entities.AttackCooldown[i] = stats.AttackCooldownTicks;
            Entities.Radius[i] = Fix64.Half;
            Entities.Job[i] = JobType.Idle;

            if (owner < Players.Length) Players[owner].PopulationUsed += stats.PopulationCost;

            Events.Add(SimEvent.Spawned(id, owner, position, kind));
            return id;
        }

        /// <summary>
        /// Places a building. When <paramref name="completed"/> is false the building starts as a
        /// construction site with a sliver of health, which is why a half-built structure is so
        /// easy to raid.
        /// </summary>
        public EntityId SpawnBuilding(BuildingType type, byte owner, int cellX, int cellY, bool completed)
        {
            PrototypeContent.BuildingStats stats = PrototypeContent.ForBuilding(type);
            EntityId id = Entities.Create(EntityKind.Building, owner);
            int i = id.Index;

            Entities.Position[i] = new Fix2(Fix64.FromInt(cellX) + Fix64.Half, Fix64.FromInt(cellY) + Fix64.Half);
            Entities.Domain[i] = MovementDomain.Static;
            Entities.Building[i] = type;
            Entities.FootprintHalf[i] = stats.FootprintHalf;
            Entities.MaxHealth[i] = stats.MaxHealth;
            Entities.VisionRange[i] = stats.VisionRange;
            Entities.BuildRequired[i] = stats.BuildTicks;
            Entities.UnderConstruction[i] = !completed;
            Entities.BuildProgress[i] = completed ? stats.BuildTicks : 0;
            Entities.Health[i] = completed ? stats.MaxHealth : stats.MaxHealth / 10;

            Nav.SetFootprint(cellX, cellY, stats.FootprintHalf, true);

            if (completed && owner < Players.Length)
                Players[owner].PopulationCap = System.Math.Min(
                    Players[owner].PopulationCap + stats.PopulationCapacity,
                    PrototypeContent.MaxPopulationCap);

            Events.Add(SimEvent.Spawned(id, owner, Entities.Position[i], EntityKind.Building));
            if (!completed)
                Events.Add(new SimEvent { Type = SimEventType.ConstructionStarted, Entity = id, Player = owner, ValueA = (int)type, Position = Entities.Position[i] });

            return id;
        }

        public EntityId SpawnResourceNode(ResourceNodeType type, int cellX, int cellY)
        {
            EntityId id = Entities.Create(EntityKind.ResourceNode, SimConstants.NeutralPlayer);
            int i = id.Index;
            Entities.Position[i] = new Fix2(Fix64.FromInt(cellX) + Fix64.Half, Fix64.FromInt(cellY) + Fix64.Half);
            Entities.Domain[i] = MovementDomain.Static;
            Entities.NodeType[i] = type;
            Entities.NodeResource[i] = PrototypeContent.NodeResource(type);
            Entities.NodeRemaining[i] = PrototypeContent.NodeCapacity(type);
            Entities.MaxHealth[i] = Fix64.FromInt(1);
            Entities.Health[i] = Fix64.FromInt(1);
            Entities.FootprintHalf[i] = 0;
            return id;
        }

        // ------------------------------------------------------------------ helpers used by systems

        public bool AreHostile(byte a, byte b)
        {
            if (a == SimConstants.NeutralPlayer || b == SimConstants.NeutralPlayer) return false;
            if (a == b) return false;
            if (a >= Players.Length || b >= Players.Length) return false;
            return Players[a].Team != Players[b].Team;
        }

        /// <summary>
        /// Applies damage. All damage in the game funnels through here, on the server only, which is
        /// what makes a client-side damage hack impossible rather than merely detectable.
        /// </summary>
        public void ApplyDamage(EntityId attacker, EntityId victim, Fix64 amount)
        {
            if (!Entities.IsAlive(victim)) return;
            int v = victim.Index;
            Entities.Health[v] -= amount;
            Events.Add(SimEvent.Damage(attacker, victim, amount.ToInt(), Entities.Position[v]));
        }

        /// <summary>Sets an entity's job and emits the intent that replication tier B carries.</summary>
        public void SetJob(int index, JobType job, Fix2 destination, EntityId target)
        {
            Entities.Job[index] = job;
            Entities.JobDestination[index] = destination;
            Entities.JobTarget[index] = target;
            Events.Add(SimEvent.Intent(Entities.IdOf(index), Entities.Owner[index], job, destination, target));
        }

        /// <summary>Requests a path and returns whether one was found.</summary>
        public bool RequestPath(int index, Fix2 destination)
        {
            int start = Nav.CellAt(Entities.Position[index]);
            int goal = Nav.CellAt(destination);
            MovementDomain domain = Entities.Domain[index];

            int[] buffer = Entities.PathBuffer(index);
            int length = Path.FindPath(start, goal, domain, buffer);
            Entities.PathLength[index] = length;
            Entities.PathCursor[index] = 0;
            return length > 0;
        }

        internal void SetMatchOver(int winningTeam)
        {
            if (MatchOver) return;
            MatchOver = true;
            WinningTeam = winningTeam;
            Events.Add(new SimEvent { Type = SimEventType.MatchEnded, ValueA = winningTeam });
        }

        // ------------------------------------------------------------------ determinism fingerprint

        /// <summary>
        /// A 64-bit fingerprint of everything that affects gameplay. Written into the replay every
        /// 200 ticks and compared across platforms in CI. Field order is part of the contract:
        /// changing it invalidates existing golden replays, so it changes only with a version bump.
        /// </summary>
        public ulong ComputeStateHash()
        {
            var hash = StateHash.Create();
            hash.Add(Tick);
            hash.Add(MatchOver);
            hash.Add(WinningTeam);

            for (int p = 0; p < Players.Length; p++)
            {
                PlayerState player = Players[p];
                for (int r = 0; r < SimConstants.ResourceTypeCount; r++) hash.Add(player.Resources[r]);
                hash.Add(player.PopulationUsed);
                hash.Add(player.PopulationCap);
                hash.Add(player.Defeated);
            }

            for (int i = 1; i < Entities.Count; i++)
            {
                hash.Add(Entities.Alive[i]);
                if (!Entities.Alive[i]) continue;
                hash.Add((int)Entities.Kind[i]);
                hash.Add((int)Entities.Owner[i]);
                hash.Add(Entities.Position[i]);
                hash.Add(Entities.Health[i]);
                hash.Add((int)Entities.Job[i]);
                hash.Add(Entities.JobTarget[i]);
                hash.Add(Entities.JobTimer[i]);
                hash.Add(Entities.CarriedAmount[i]);
                hash.Add((int)Entities.CarriedType[i]);
                hash.Add(Entities.BuildProgress[i]);
                hash.Add(Entities.NodeRemaining[i]);
                hash.Add(Entities.TrainingQueued[i]);
                hash.Add(Entities.TrainingTimer[i]);
                hash.Add(Entities.PathCursor[i]);
                hash.Add(Entities.PathLength[i]);
            }

            Rng.GetState(out ulong s0, out ulong s1);
            hash.Add(s0);
            hash.Add(s1);
            return hash.Value;
        }
    }
}
