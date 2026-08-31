using Brinehold.Core.Collections;
using Brinehold.Core.Math;
using Brinehold.Sim.Commands;
using Brinehold.Sim.World;

namespace Brinehold.Protocol
{
    public enum MessageType : byte
    {
        None = 0,

        // client -> server
        Hello = 1,
        ClientCommand = 2,
        Ping = 3,

        // server -> client
        Welcome = 16,
        MatchStart = 17,
        TickHeader = 18,
        SpawnEntity = 19,
        DespawnEntity = 20,
        SetIntent = 21,
        Correction = 22,
        PrivateDelta = 23,
        LostSight = 24,
        GameEvent = 25,
        MatchEnd = 26,
        CommandRejected = 27,
        Pong = 28
    }

    public static class ProtocolVersion
    {
        /// <summary>
        /// Bumped whenever the wire format changes in a way that is not backward compatible. It is
        /// checked at handshake: mismatched builds are refused with a clear reason rather than being
        /// allowed to desync interestingly later.
        /// </summary>
        public const ushort Current = 1;
    }

    public enum HandshakeResult : byte
    {
        Accepted = 0,
        ProtocolMismatch = 1,
        ContentMismatch = 2,
        MatchFull = 3,
        MatchAlreadyStarted = 4
    }

    // ---------------------------------------------------------------- client to server

    public struct HelloMessage
    {
        public ushort ProtocolVersion;
        public ulong ContentHash;
        public string PlayerName;
    }

    public struct PingMessage
    {
        public uint ClientStamp;
    }

    // ---------------------------------------------------------------- server to client

    public struct WelcomeMessage
    {
        public HandshakeResult Result;
        public byte PlayerId;
        public byte PlayerCount;
        public ushort MapWidth;
        public ushort MapHeight;
        public ulong Seed;
        public ulong ContentHash;
    }

    public struct TickHeaderMessage
    {
        public uint Tick;
    }

    /// <summary>
    /// An entity has entered this player's interest set. Carries everything needed to display it;
    /// the client learns nothing about entities it cannot see, because no such message is sent.
    /// </summary>
    public struct SpawnEntityMessage
    {
        public EntityId Entity;
        public EntityKind Kind;
        public byte Owner;
        public BuildingType Building;
        public ResourceNodeType Node;
        public ushort PositionX;
        public ushort PositionY;
        public byte HealthRatio;
        public bool UnderConstruction;
    }

    public struct DespawnEntityMessage
    {
        public EntityId Entity;
        /// <summary>True when the entity actually died, false when it merely left vision.</summary>
        public bool Destroyed;
    }

    /// <summary>
    /// Replication tier B. One of these replaces an entire stream of position updates: the client
    /// reproduces the movement locally from the destination and the entity's speed.
    /// </summary>
    public struct SetIntentMessage
    {
        public EntityId Entity;
        public JobType Job;
        /// <summary>
        /// Where the entity stood when the intent was issued. Carrying it costs four bytes on a
        /// message that is already rare, and it means the client and the server's shadow
        /// extrapolator start from an identical position — so measured drift is real divergence
        /// rather than accumulated disagreement about where the entity began.
        /// </summary>
        public ushort OriginX;
        public ushort OriginY;
        public ushort DestinationX;
        public ushort DestinationY;
        public EntityId Target;
    }

    /// <summary>
    /// Replication tier C. Sent only when the client's local extrapolation has drifted past the
    /// tolerance, never as a per-frame stream.
    /// </summary>
    public struct CorrectionMessage
    {
        public EntityId Entity;
        public ushort PositionX;
        public ushort PositionY;
        public byte Heading;
        public byte HealthRatio;
    }

    /// <summary>Replication tier D: the player's own economy. Never sent about anybody else.</summary>
    public struct PrivateDeltaMessage
    {
        public int Wood;
        public int Food;
        public int Stone;
        public int Coin;
        public ushort PopulationUsed;
        public ushort PopulationCap;
    }

    public struct GameEventMessage
    {
        public SimEventType Type;
        public EntityId Entity;
        public EntityId Other;
        public byte Player;
        public int ValueA;
        public int ValueB;
        public ushort PositionX;
        public ushort PositionY;
    }

    public struct CommandRejectedMessage
    {
        public uint Sequence;
        public RejectReason Reason;
    }

    public struct MatchEndMessage
    {
        public int WinningTeam;
        public bool LocalPlayerWon;
    }
}
