using Brinehold.Core.Collections;
using Brinehold.Sim.World;

namespace Brinehold.Sim.Commands
{
    public enum CommandType : byte
    {
        None = 0,
        /// <summary>Move the selected entities to a cell.</summary>
        Move = 1,
        /// <summary>Send the selected workers to harvest a resource node.</summary>
        Harvest = 2,
        /// <summary>Place a construction site and send the selected workers to build it.</summary>
        Build = 3,
        /// <summary>Queue a unit at a building.</summary>
        Train = 4,
        /// <summary>Attack a specific target entity.</summary>
        Attack = 5,
        /// <summary>Cancel current orders and hold position.</summary>
        Stop = 6,
        /// <summary>Cancel the front item of a building's training queue and refund it.</summary>
        CancelTraining = 7
    }

    /// <summary>
    /// Why the server refused a command. Rejections are reported back to the issuing client so the
    /// UI can say what went wrong; they are never silently clamped into something legal, because a
    /// clamped command is indistinguishable from a successful cheat.
    /// </summary>
    public enum RejectReason : byte
    {
        None = 0,
        UnknownPlayer = 1,
        PlayerDefeated = 2,
        NoEntities = 3,
        NotOwner = 4,
        EntityDead = 5,
        InvalidTarget = 6,
        OutOfBounds = 7,
        NotEnoughResources = 8,
        PopulationCapReached = 9,
        IllegalPlacement = 10,
        BuildingUnderConstruction = 11,
        CannotTrainHere = 12,
        WrongEntityKind = 13,
        MatchOver = 14,
        RateLimited = 15,
        NothingQueued = 16,
        TooManyEntities = 17
    }

    /// <summary>
    /// A player order. Commands are the only thing a client may send that affects the simulation,
    /// and every field is validated server-side before execution
    /// (MULTIPLAYER_ARCHITECTURE.md section 7.2).
    /// </summary>
    public sealed class Command
    {
        /// <summary>Upper bound on a single command's selection, enforced on ingest.</summary>
        public const int MaxEntities = 256;

        public byte PlayerId;
        public uint Sequence;
        public CommandType Type;

        public EntityId[] Entities = System.Array.Empty<EntityId>();
        public int EntityCount;

        public EntityId TargetEntity;
        public int TargetCellX;
        public int TargetCellY;
        public BuildingType Building;
        public EntityKind TrainKind;

        public Command() { }

        public static Command Move(byte player, uint sequence, EntityId[] entities, int cellX, int cellY)
            => new Command
            {
                PlayerId = player, Sequence = sequence, Type = CommandType.Move,
                Entities = entities, EntityCount = entities.Length,
                TargetCellX = cellX, TargetCellY = cellY
            };

        public static Command Harvest(byte player, uint sequence, EntityId[] entities, EntityId node)
            => new Command
            {
                PlayerId = player, Sequence = sequence, Type = CommandType.Harvest,
                Entities = entities, EntityCount = entities.Length, TargetEntity = node
            };

        public static Command Build(byte player, uint sequence, EntityId[] builders, BuildingType type, int cellX, int cellY)
            => new Command
            {
                PlayerId = player, Sequence = sequence, Type = CommandType.Build,
                Entities = builders, EntityCount = builders.Length,
                Building = type, TargetCellX = cellX, TargetCellY = cellY
            };

        public static Command Train(byte player, uint sequence, EntityId building, EntityKind kind)
            => new Command
            {
                PlayerId = player, Sequence = sequence, Type = CommandType.Train,
                Entities = new[] { building }, EntityCount = 1, TrainKind = kind
            };

        public static Command Attack(byte player, uint sequence, EntityId[] entities, EntityId target)
            => new Command
            {
                PlayerId = player, Sequence = sequence, Type = CommandType.Attack,
                Entities = entities, EntityCount = entities.Length, TargetEntity = target
            };

        public static Command Stop(byte player, uint sequence, EntityId[] entities)
            => new Command
            {
                PlayerId = player, Sequence = sequence, Type = CommandType.Stop,
                Entities = entities, EntityCount = entities.Length
            };
    }
}
