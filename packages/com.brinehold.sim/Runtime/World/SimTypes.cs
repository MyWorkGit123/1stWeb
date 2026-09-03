namespace Brinehold.Sim.World
{
    /// <summary>
    /// The prototype resource set. The full game adds refined and imported goods
    /// (see ECONOMY_DESIGN.md); these four are the ones the M3 prototype simulates.
    /// </summary>
    public enum ResourceType : byte
    {
        Wood = 0,
        Food = 1,
        Stone = 2,
        Coin = 3
    }

    public enum EntityKind : byte
    {
        None = 0,
        Worker = 1,
        Soldier = 2,
        Ship = 3,
        Building = 4,
        ResourceNode = 5
    }

    public enum BuildingType : byte
    {
        None = 0,
        /// <summary>Settlement core: stores goods, trains workers and soldiers, and is the victory target.</summary>
        Warehouse = 1,
        House = 2,
        LumberCamp = 3,
        FishingWharf = 4,
        Dock = 5
    }

    public enum ResourceNodeType : byte
    {
        None = 0,
        Forest = 1,
        FishShoal = 2,
        StoneOutcrop = 3
    }

    /// <summary>
    /// What an entity is currently doing. Job state is replicated as an intent, not as a stream of
    /// positions — see MULTIPLAYER_ARCHITECTURE.md section 5.
    /// </summary>
    public enum JobType : byte
    {
        Idle = 0,
        MoveTo = 1,
        /// <summary>Travelling to a resource node.</summary>
        MoveToHarvest = 2,
        /// <summary>Standing at a node, extracting.</summary>
        Harvesting = 3,
        /// <summary>Carrying goods back to a drop-off building.</summary>
        Delivering = 4,
        /// <summary>Travelling to a construction site.</summary>
        MoveToBuild = 5,
        /// <summary>Standing at a site, applying build labour.</summary>
        Building = 6,
        /// <summary>Moving into range of an attack target.</summary>
        MoveToAttack = 7,
        /// <summary>In contact with an attack target.</summary>
        Attacking = 8
    }

    public enum TerrainType : byte
    {
        Land = 0,
        Water = 1,
        /// <summary>Impassable to everything. Cliffs and rock faces.</summary>
        Blocked = 2
    }

    /// <summary>Which navigation layer an entity moves on.</summary>
    public enum MovementDomain : byte
    {
        Land = 0,
        Water = 1,
        /// <summary>Buildings and resource nodes. Never moves.</summary>
        Static = 2
    }

    public static class SimConstants
    {
        /// <summary>Fixed simulation rate. Never variable, never frame-coupled.</summary>
        public const int TicksPerSecond = 20;
        public const int MillisecondsPerTick = 1000 / TicksPerSecond;

        /// <summary>One navigation cell is one metre.</summary>
        public const int CellSizeMetres = 1;

        public const int ResourceTypeCount = 4;

        /// <summary>Neutral / unowned. Resource nodes use this.</summary>
        public const byte NeutralPlayer = 255;

        public const int MaxPlayers = 8;

        /// <summary>Maximum waypoints retained for a single path.</summary>
        public const int MaxPathLength = 256;

        /// <summary>State hash checkpoint interval, in ticks (10 seconds).</summary>
        public const int StateHashInterval = 200;
    }
}
