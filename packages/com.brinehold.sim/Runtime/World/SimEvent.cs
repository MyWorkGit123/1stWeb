using Brinehold.Core.Collections;
using Brinehold.Core.Math;
using Brinehold.Sim.Commands;

namespace Brinehold.Sim.World
{
    public enum SimEventType : byte
    {
        None = 0,
        EntitySpawned = 1,
        EntityDestroyed = 2,
        /// <summary>An entity started a new behaviour. This is the message replication tier B carries.</summary>
        IntentChanged = 3,
        DamageApplied = 4,
        ResourceDeposited = 5,
        ConstructionStarted = 6,
        ConstructionCompleted = 7,
        TrainingCompleted = 8,
        CommandRejected = 9,
        PlayerDefeated = 10,
        MatchEnded = 11,
        ResourceNodeDepleted = 12
    }

    /// <summary>
    /// Something the simulation did on this tick that the outside world may need to know about.
    ///
    /// The replication layer drains this buffer every tick and decides, per player, which events
    /// that player is allowed to be told about. The renderer uses the same events to trigger
    /// effects. Nothing outside the simulation may mutate state in response to an event — events
    /// are a report, not an instruction.
    /// </summary>
    public struct SimEvent
    {
        public SimEventType Type;
        public EntityId Entity;
        public EntityId Other;
        public byte Player;
        public int ValueA;
        public int ValueB;
        public Fix2 Position;

        public static SimEvent Spawned(EntityId entity, byte player, Fix2 position, EntityKind kind)
            => new SimEvent { Type = SimEventType.EntitySpawned, Entity = entity, Player = player, Position = position, ValueA = (int)kind };

        public static SimEvent Destroyed(EntityId entity, byte player, Fix2 position)
            => new SimEvent { Type = SimEventType.EntityDestroyed, Entity = entity, Player = player, Position = position };

        public static SimEvent Intent(EntityId entity, byte player, JobType job, Fix2 destination, EntityId target)
            => new SimEvent { Type = SimEventType.IntentChanged, Entity = entity, Player = player, Position = destination, Other = target, ValueA = (int)job };

        public static SimEvent Damage(EntityId attacker, EntityId victim, int amount, Fix2 position)
            => new SimEvent { Type = SimEventType.DamageApplied, Entity = attacker, Other = victim, ValueA = amount, Position = position };

        public static SimEvent Deposited(EntityId worker, byte player, ResourceType type, int amount)
            => new SimEvent { Type = SimEventType.ResourceDeposited, Entity = worker, Player = player, ValueA = (int)type, ValueB = amount };

        public static SimEvent Rejected(byte player, uint sequence, RejectReason reason)
            => new SimEvent { Type = SimEventType.CommandRejected, Player = player, ValueA = unchecked((int)sequence), ValueB = (int)reason };
    }
}
