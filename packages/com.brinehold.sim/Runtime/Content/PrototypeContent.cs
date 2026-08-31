using Brinehold.Core.Math;
using Brinehold.Sim.World;

namespace Brinehold.Sim.Content
{
    /// <summary>
    /// Statistics for the M3 prototype.
    ///
    /// These live in code for the prototype only. ECONOMY_DESIGN.md and COMBAT_DESIGN.md are the
    /// design source of truth, and M8 moves these tables into JSON under com.brinehold.content so
    /// that designers can tune them without a rebuild. The shape of the structs here is the shape
    /// that loader will produce, so nothing downstream has to change when it does.
    /// </summary>
    public static class PrototypeContent
    {
        // ---------------------------------------------------------------- units

        public struct UnitStats
        {
            public Fix64 MaxHealth;
            public Fix64 MoveSpeed;        // metres per second
            public Fix64 VisionRange;      // metres
            public Fix64 AttackDamage;
            public Fix64 AttackRange;      // metres
            public int AttackCooldownTicks;
            public int CarryCapacity;
            public int TrainTicks;
            public int PopulationCost;
            public int CostWood;
            public int CostFood;
            public int CostStone;
            public int CostCoin;
            public MovementDomain Domain;
        }

        public static UnitStats Worker => new UnitStats
        {
            MaxHealth = Fix64.FromInt(60),
            MoveSpeed = Fix64.FromFraction(14, 10),
            VisionRange = Fix64.FromInt(12),
            AttackDamage = Fix64.FromInt(3),
            AttackRange = Fix64.FromInt(1),
            AttackCooldownTicks = 30,
            CarryCapacity = 8,
            TrainTicks = 240,              // 12 s
            PopulationCost = 1,
            CostWood = 0, CostFood = 50, CostStone = 0, CostCoin = 0,
            Domain = MovementDomain.Land
        };

        /// <summary>Cutthroat: the prototype's single military unit (COMBAT_DESIGN.md section 4).</summary>
        public static UnitStats Soldier => new UnitStats
        {
            MaxHealth = Fix64.FromInt(60),
            MoveSpeed = Fix64.FromFraction(32, 10),
            VisionRange = Fix64.FromInt(14),
            AttackDamage = Fix64.FromInt(9),
            AttackRange = Fix64.FromFraction(15, 10),
            AttackCooldownTicks = 20,      // 1.0 s
            CarryCapacity = 0,
            TrainTicks = 240,
            PopulationCost = 1,
            CostWood = 0, CostFood = 35, CostStone = 0, CostCoin = 15,
            Domain = MovementDomain.Land
        };

        /// <summary>Cutter: the prototype's single ship (COMBAT_DESIGN.md section 6.1).</summary>
        public static UnitStats Ship => new UnitStats
        {
            MaxHealth = Fix64.FromInt(200),
            MoveSpeed = Fix64.FromInt(5),
            VisionRange = Fix64.FromInt(30),
            AttackDamage = Fix64.FromInt(20),
            AttackRange = Fix64.FromInt(20),
            AttackCooldownTicks = 160,     // 8 s reload
            CarryCapacity = 0,
            TrainTicks = 900,              // 45 s
            PopulationCost = 3,
            CostWood = 40, CostFood = 0, CostStone = 0, CostCoin = 40,
            Domain = MovementDomain.Water
        };

        public static UnitStats ForKind(EntityKind kind)
        {
            switch (kind)
            {
                case EntityKind.Worker: return Worker;
                case EntityKind.Soldier: return Soldier;
                case EntityKind.Ship: return Ship;
                default: return Worker;
            }
        }

        // ---------------------------------------------------------------- buildings

        public struct BuildingStats
        {
            public Fix64 MaxHealth;
            public Fix64 VisionRange;
            public int FootprintHalf;      // square footprint half-extent in cells
            public int BuildTicks;
            public int CostWood;
            public int CostFood;
            public int CostStone;
            public int CostCoin;
            public bool RequiresWaterAdjacency;
            public bool IsDropOff;
            public int PopulationCapacity;
        }

        public static BuildingStats ForBuilding(BuildingType type)
        {
            switch (type)
            {
                case BuildingType.Warehouse:
                    return new BuildingStats
                    {
                        MaxHealth = Fix64.FromInt(800), VisionRange = Fix64.FromInt(20),
                        FootprintHalf = 2, BuildTicks = 600,
                        CostWood = 150, CostFood = 0, CostStone = 50, CostCoin = 0,
                        RequiresWaterAdjacency = false, IsDropOff = true, PopulationCapacity = 5
                    };
                case BuildingType.House:
                    return new BuildingStats
                    {
                        MaxHealth = Fix64.FromInt(400), VisionRange = Fix64.FromInt(10),
                        FootprintHalf = 1, BuildTicks = 300,
                        CostWood = 50, CostFood = 0, CostStone = 0, CostCoin = 0,
                        RequiresWaterAdjacency = false, IsDropOff = false, PopulationCapacity = 5
                    };
                case BuildingType.LumberCamp:
                    return new BuildingStats
                    {
                        MaxHealth = Fix64.FromInt(400), VisionRange = Fix64.FromInt(10),
                        FootprintHalf = 1, BuildTicks = 300,
                        CostWood = 60, CostFood = 0, CostStone = 0, CostCoin = 0,
                        RequiresWaterAdjacency = false, IsDropOff = true, PopulationCapacity = 0
                    };
                case BuildingType.FishingWharf:
                    return new BuildingStats
                    {
                        MaxHealth = Fix64.FromInt(400), VisionRange = Fix64.FromInt(10),
                        FootprintHalf = 1, BuildTicks = 400,
                        CostWood = 70, CostFood = 0, CostStone = 0, CostCoin = 0,
                        RequiresWaterAdjacency = true, IsDropOff = true, PopulationCapacity = 0
                    };
                case BuildingType.Dock:
                    return new BuildingStats
                    {
                        MaxHealth = Fix64.FromInt(600), VisionRange = Fix64.FromInt(15),
                        FootprintHalf = 2, BuildTicks = 500,
                        CostWood = 100, CostFood = 0, CostStone = 50, CostCoin = 0,
                        RequiresWaterAdjacency = true, IsDropOff = false, PopulationCapacity = 0
                    };
                default:
                    return new BuildingStats { MaxHealth = Fix64.FromInt(100), FootprintHalf = 1, BuildTicks = 100 };
            }
        }

        /// <summary>Which unit kinds a finished building of this type can train.</summary>
        public static bool CanTrain(BuildingType building, EntityKind kind)
        {
            if (building == BuildingType.Warehouse) return kind == EntityKind.Worker || kind == EntityKind.Soldier;
            if (building == BuildingType.Dock) return kind == EntityKind.Ship;
            return false;
        }

        // ---------------------------------------------------------------- harvesting

        /// <summary>Ticks to extract one unit of a resource from a node.</summary>
        public const int HarvestTicksPerUnit = 12;

        /// <summary>Ticks of labour one worker contributes to a construction site per tick applied.</summary>
        public const int BuildProgressPerWorkerTick = 1;

        public static int NodeCapacity(ResourceNodeType type)
        {
            switch (type)
            {
                case ResourceNodeType.Forest: return 300;
                case ResourceNodeType.FishShoal: return 400;
                case ResourceNodeType.StoneOutcrop: return 250;
                default: return 0;
            }
        }

        public static ResourceType NodeResource(ResourceNodeType type)
        {
            switch (type)
            {
                case ResourceNodeType.Forest: return ResourceType.Wood;
                case ResourceNodeType.FishShoal: return ResourceType.Food;
                case ResourceNodeType.StoneOutcrop: return ResourceType.Stone;
                default: return ResourceType.Wood;
            }
        }

        // ---------------------------------------------------------------- starting state

        public const int StartingWood = 200;
        public const int StartingFood = 200;
        public const int StartingStone = 100;
        public const int StartingCoin = 100;
        public const int StartingWorkers = 10;
        public const int BasePopulationCap = 5;
        public const int MaxPopulationCap = 50;
    }
}
