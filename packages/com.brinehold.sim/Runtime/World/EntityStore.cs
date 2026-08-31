using System;
using Brinehold.Core.Collections;
using Brinehold.Core.Math;

namespace Brinehold.Sim.World
{
    /// <summary>
    /// Structure-of-arrays entity storage.
    ///
    /// Every array is indexed by the entity's dense index. Iteration is always in index order, which
    /// is what makes the simulation deterministic: there is no hash-map ordering anywhere in the
    /// hot path. Dead slots are recycled with an incremented generation so that stale references
    /// are detectable rather than silently valid.
    /// </summary>
    public sealed class EntityStore
    {
        private int _capacity;

        // --- identity -------------------------------------------------------
        public bool[] Alive = null!;
        public byte[] Generation = null!;
        public EntityKind[] Kind = null!;
        public byte[] Owner = null!;

        // --- spatial --------------------------------------------------------
        public Fix2[] Position = null!;
        public Fix64[] Heading = null!;
        public MovementDomain[] Domain = null!;
        public Fix64[] MoveSpeed = null!;
        public Fix64[] Radius = null!;

        // --- health ---------------------------------------------------------
        public Fix64[] Health = null!;
        public Fix64[] MaxHealth = null!;

        // --- vision ---------------------------------------------------------
        public Fix64[] VisionRange = null!;

        // --- jobs -----------------------------------------------------------
        public JobType[] Job = null!;
        public EntityId[] JobTarget = null!;
        public Fix2[] JobDestination = null!;
        /// <summary>Countdown in ticks for timed work (a harvest swing, a build pulse, a reload).</summary>
        public int[] JobTimer = null!;
        /// <summary>Remembered node so a worker returns to it after delivering a load.</summary>
        public EntityId[] HomeNode = null!;
        /// <summary>Remembered drop-off so a worker does not re-search every trip.</summary>
        public EntityId[] HomeDropOff = null!;

        // --- carrying -------------------------------------------------------
        public ResourceType[] CarriedType = null!;
        public int[] CarriedAmount = null!;

        // --- combat ---------------------------------------------------------
        public Fix64[] AttackDamage = null!;
        public Fix64[] AttackRange = null!;
        /// <summary>Ticks between attacks.</summary>
        public int[] AttackCooldown = null!;
        public int[] AttackTimer = null!;

        // --- buildings ------------------------------------------------------
        public BuildingType[] Building = null!;
        public bool[] UnderConstruction = null!;
        public int[] BuildProgress = null!;
        public int[] BuildRequired = null!;
        /// <summary>Footprint half-extent in cells; buildings are square.</summary>
        public int[] FootprintHalf = null!;

        // --- production queue (buildings that train units) -------------------
        public EntityKind[] TrainingKind = null!;
        public int[] TrainingTimer = null!;
        public int[] TrainingQueued = null!;

        // --- resource nodes -------------------------------------------------
        public ResourceNodeType[] NodeType = null!;
        public ResourceType[] NodeResource = null!;
        public int[] NodeRemaining = null!;

        // --- pathing --------------------------------------------------------
        private int[][] _paths = null!;
        public int[] PathLength = null!;
        public int[] PathCursor = null!;

        private readonly System.Collections.Generic.List<int> _freeIndices = new System.Collections.Generic.List<int>();
        private int _highWater;

        public int Capacity => _capacity;
        /// <summary>One past the highest index ever used. Iterate [0, Count).</summary>
        public int Count => _highWater;

        public EntityStore(int capacity = 4096)
        {
            _capacity = capacity;
            AllocateArrays(capacity);
            // Index 0 is reserved so that EntityId.None (raw 0) can never alias a live entity.
            _highWater = 1;
        }

        private void AllocateArrays(int n)
        {
            Alive = new bool[n]; Generation = new byte[n]; Kind = new EntityKind[n]; Owner = new byte[n];
            Position = new Fix2[n]; Heading = new Fix64[n]; Domain = new MovementDomain[n];
            MoveSpeed = new Fix64[n]; Radius = new Fix64[n];
            Health = new Fix64[n]; MaxHealth = new Fix64[n]; VisionRange = new Fix64[n];
            Job = new JobType[n]; JobTarget = new EntityId[n]; JobDestination = new Fix2[n];
            JobTimer = new int[n]; HomeNode = new EntityId[n]; HomeDropOff = new EntityId[n];
            CarriedType = new ResourceType[n]; CarriedAmount = new int[n];
            AttackDamage = new Fix64[n]; AttackRange = new Fix64[n];
            AttackCooldown = new int[n]; AttackTimer = new int[n];
            Building = new BuildingType[n]; UnderConstruction = new bool[n];
            BuildProgress = new int[n]; BuildRequired = new int[n]; FootprintHalf = new int[n];
            TrainingKind = new EntityKind[n]; TrainingTimer = new int[n]; TrainingQueued = new int[n];
            NodeType = new ResourceNodeType[n]; NodeResource = new ResourceType[n]; NodeRemaining = new int[n];
            _paths = new int[n][]; PathLength = new int[n]; PathCursor = new int[n];
        }

        private void Grow()
        {
            int n = _capacity * 2;
            Array.Resize(ref Alive, n); Array.Resize(ref Generation, n); Array.Resize(ref Kind, n);
            Array.Resize(ref Owner, n); Array.Resize(ref Position, n); Array.Resize(ref Heading, n);
            Array.Resize(ref Domain, n); Array.Resize(ref MoveSpeed, n); Array.Resize(ref Radius, n);
            Array.Resize(ref Health, n); Array.Resize(ref MaxHealth, n); Array.Resize(ref VisionRange, n);
            Array.Resize(ref Job, n); Array.Resize(ref JobTarget, n); Array.Resize(ref JobDestination, n);
            Array.Resize(ref JobTimer, n); Array.Resize(ref HomeNode, n); Array.Resize(ref HomeDropOff, n);
            Array.Resize(ref CarriedType, n); Array.Resize(ref CarriedAmount, n);
            Array.Resize(ref AttackDamage, n); Array.Resize(ref AttackRange, n);
            Array.Resize(ref AttackCooldown, n); Array.Resize(ref AttackTimer, n);
            Array.Resize(ref Building, n); Array.Resize(ref UnderConstruction, n);
            Array.Resize(ref BuildProgress, n); Array.Resize(ref BuildRequired, n);
            Array.Resize(ref FootprintHalf, n);
            Array.Resize(ref TrainingKind, n); Array.Resize(ref TrainingTimer, n); Array.Resize(ref TrainingQueued, n);
            Array.Resize(ref NodeType, n); Array.Resize(ref NodeResource, n); Array.Resize(ref NodeRemaining, n);
            Array.Resize(ref _paths, n); Array.Resize(ref PathLength, n); Array.Resize(ref PathCursor, n);
            _capacity = n;
        }

        /// <summary>
        /// Allocates an entity slot. Free slots are reused lowest-index-first so that entity
        /// allocation order is a pure function of the command stream, not of allocator history.
        /// </summary>
        public EntityId Create(EntityKind kind, byte owner)
        {
            int index;
            if (_freeIndices.Count > 0)
            {
                // Lowest index first: keep allocation deterministic.
                int best = 0;
                for (int i = 1; i < _freeIndices.Count; i++)
                    if (_freeIndices[i] < _freeIndices[best]) best = i;
                index = _freeIndices[best];
                _freeIndices.RemoveAt(best);
            }
            else
            {
                if (_highWater >= _capacity) Grow();
                index = _highWater++;
            }

            ResetSlot(index);
            Alive[index] = true;
            Kind[index] = kind;
            Owner[index] = owner;
            return new EntityId(index, Generation[index]);
        }

        private void ResetSlot(int i)
        {
            Kind[i] = EntityKind.None; Owner[i] = SimConstants.NeutralPlayer;
            Position[i] = Fix2.Zero; Heading[i] = Fix64.Zero; Domain[i] = MovementDomain.Static;
            MoveSpeed[i] = Fix64.Zero; Radius[i] = Fix64.Half;
            Health[i] = Fix64.Zero; MaxHealth[i] = Fix64.Zero; VisionRange[i] = Fix64.Zero;
            Job[i] = JobType.Idle; JobTarget[i] = EntityId.None; JobDestination[i] = Fix2.Zero;
            JobTimer[i] = 0; HomeNode[i] = EntityId.None; HomeDropOff[i] = EntityId.None;
            CarriedType[i] = ResourceType.Wood; CarriedAmount[i] = 0;
            AttackDamage[i] = Fix64.Zero; AttackRange[i] = Fix64.Zero;
            AttackCooldown[i] = 0; AttackTimer[i] = 0;
            Building[i] = BuildingType.None; UnderConstruction[i] = false;
            BuildProgress[i] = 0; BuildRequired[i] = 0; FootprintHalf[i] = 0;
            TrainingKind[i] = EntityKind.None; TrainingTimer[i] = 0; TrainingQueued[i] = 0;
            NodeType[i] = ResourceNodeType.None; NodeResource[i] = ResourceType.Wood; NodeRemaining[i] = 0;
            PathLength[i] = 0; PathCursor[i] = 0;
        }

        public void Destroy(EntityId id)
        {
            if (!IsAlive(id)) return;
            int i = id.Index;
            Alive[i] = false;
            Generation[i] = unchecked((byte)(Generation[i] + 1));
            _freeIndices.Add(i);
        }

        public bool IsAlive(EntityId id)
        {
            int i = id.Index;
            return i > 0 && i < _highWater && Alive[i] && Generation[i] == id.Generation;
        }

        public EntityId IdOf(int index) => new EntityId(index, Generation[index]);

        // --- path storage ---------------------------------------------------

        public int[] PathBuffer(int index)
        {
            int[]? buffer = _paths[index];
            if (buffer == null)
            {
                buffer = new int[SimConstants.MaxPathLength];
                _paths[index] = buffer;
            }
            return buffer;
        }

        public void ClearPath(int index)
        {
            PathLength[index] = 0;
            PathCursor[index] = 0;
        }

        public bool HasPath(int index) => PathCursor[index] < PathLength[index];
    }
}
