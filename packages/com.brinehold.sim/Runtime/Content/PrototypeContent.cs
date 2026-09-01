using Brinehold.Sim.World;

namespace Brinehold.Sim.Content
{
    /// <summary>
    /// The default ruleset, as a convenience facade.
    ///
    /// The simulation itself reads <c>world.Content</c>, never this: a match can be started with a
    /// different balance pass and every system picks it up. This exists so that tests and tools that
    /// only care about the shipped values can say what they mean without threading a database
    /// through, and so the simulation can always start even if no content file is present.
    /// </summary>
    public static class PrototypeContent
    {
        /// <summary>The shipped ruleset. Loaded content replaces it per match, not globally.</summary>
        public static readonly ContentDatabase Default = ContentDatabase.CreateDefault();

        public static ContentDatabase.UnitStats Worker => Default.Unit(EntityKind.Worker);
        public static ContentDatabase.UnitStats Soldier => Default.Unit(EntityKind.Soldier);
        public static ContentDatabase.UnitStats Ship => Default.Unit(EntityKind.Ship);

        public static ContentDatabase.UnitStats ForKind(EntityKind kind) => Default.Unit(kind);

        public static ContentDatabase.BuildingStats ForBuilding(BuildingType type) => Default.Building(type);

        public static bool CanTrain(BuildingType building, EntityKind kind) => Default.CanTrain(building, kind);

        public static int NodeCapacity(ResourceNodeType type) => Default.Node(type).Capacity;

        public static ResourceType NodeResource(ResourceNodeType type) => Default.Node(type).Yields;

        public static int HarvestTicksPerUnit => Default.HarvestTicksPerUnit;
        public static int BuildProgressPerWorkerTick => Default.BuildProgressPerWorkerTick;

        public static int StartingWood => Default.StartingWood;
        public static int StartingFood => Default.StartingFood;
        public static int StartingStone => Default.StartingStone;
        public static int StartingCoin => Default.StartingCoin;
        public static int StartingWorkers => Default.StartingWorkers;
        public static int BasePopulationCap => Default.BasePopulationCap;
        public static int MaxPopulationCap => Default.MaxPopulationCap;
    }
}
