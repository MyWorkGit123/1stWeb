using System;
using System.Collections.Generic;
using Brinehold.Core.Collections;
using Brinehold.Core.Serialization;
using Brinehold.Sim.Commands;
using Brinehold.Sim.World;

namespace Brinehold.Protocol
{
    /// <summary>
    /// Hand-written bit-packed codecs.
    ///
    /// Written by hand for the prototype; M4 replaces these with codecs generated from
    /// Schema/messages.schema.json so that the client and server encoders cannot drift apart. The
    /// shapes here are the shapes the generator will emit.
    ///
    /// Every decoder is defensive: a truncated or malformed packet from a hostile client must make
    /// the reader report end-of-stream, never throw into the tick loop.
    /// </summary>
    public static class MessageCodec
    {
        private const int EntityIdBits = 32;

        // ---------------------------------------------------------------- primitives

        public static void WriteEntity(BitWriter w, EntityId id) => w.WriteBits(id.Raw, EntityIdBits);

        public static EntityId ReadEntity(BitReader r) => new EntityId(r.ReadBits(EntityIdBits));

        public static void WriteType(BitWriter w, MessageType type) => w.WriteByte((byte)type);

        // ---------------------------------------------------------------- client to server

        public static void Write(BitWriter w, in HelloMessage m)
        {
            WriteType(w, MessageType.Hello);
            w.WriteUInt16(m.ProtocolVersion);
            w.WriteUInt64(m.ContentHash);
            w.WriteString(m.PlayerName ?? string.Empty);
            w.WriteUInt64(m.ReconnectToken);
        }

        public static HelloMessage ReadHello(BitReader r) => new HelloMessage
        {
            ProtocolVersion = r.ReadUInt16(),
            ContentHash = r.ReadUInt64(),
            PlayerName = r.ReadString(),
            ReconnectToken = r.ReadUInt64()
        };

        /// <summary>
        /// A player order. The selection is length-prefixed and capped on both sides: the encoder
        /// refuses to write more than the cap, and the decoder refuses to read more, so an
        /// oversized selection cannot be used to make the server allocate.
        /// </summary>
        public static void Write(BitWriter w, Command c)
        {
            WriteType(w, MessageType.ClientCommand);
            w.WriteUInt32(c.Sequence);
            w.WriteByte((byte)c.Type);

            int count = System.Math.Min(c.EntityCount, Command.MaxEntities);
            w.WriteBits((uint)count, 9);
            for (int i = 0; i < count; i++) WriteEntity(w, c.Entities[i]);

            WriteEntity(w, c.TargetEntity);
            w.WriteUInt16((ushort)System.Math.Max(0, System.Math.Min(65535, c.TargetCellX)));
            w.WriteUInt16((ushort)System.Math.Max(0, System.Math.Min(65535, c.TargetCellY)));
            w.WriteByte((byte)c.Building);
            w.WriteByte((byte)c.TrainKind);
        }

        /// <summary>
        /// Decodes a command. The server fills in the player id from the authenticated session and
        /// ignores anything the client might claim about it — a client cannot act as another player
        /// because the field is not on the wire at all.
        /// </summary>
        public static Command ReadCommand(BitReader r, byte authenticatedPlayerId)
        {
            var command = new Command { PlayerId = authenticatedPlayerId };
            command.Sequence = r.ReadUInt32();
            command.Type = (CommandType)r.ReadByte();

            int count = (int)r.ReadBits(9);
            if (count > Command.MaxEntities) count = Command.MaxEntities + 1;   // preserved so the server can reject it

            int toRead = System.Math.Min(count, Command.MaxEntities);
            var entities = new EntityId[toRead];
            for (int i = 0; i < toRead; i++)
            {
                entities[i] = ReadEntity(r);
                if (r.EndOfStream) return command;
            }
            command.Entities = entities;
            command.EntityCount = count;

            command.TargetEntity = ReadEntity(r);
            command.TargetCellX = r.ReadUInt16();
            command.TargetCellY = r.ReadUInt16();
            command.Building = (BuildingType)r.ReadByte();
            command.TrainKind = (EntityKind)r.ReadByte();
            return command;
        }

        public static void Write(BitWriter w, in PingMessage m)
        {
            WriteType(w, MessageType.Ping);
            w.WriteUInt32(m.ClientStamp);
        }

        public static PingMessage ReadPing(BitReader r) => new PingMessage { ClientStamp = r.ReadUInt32() };

        // ---------------------------------------------------------------- server to client

        public static void Write(BitWriter w, in WelcomeMessage m)
        {
            WriteType(w, MessageType.Welcome);
            w.WriteByte((byte)m.Result);
            w.WriteByte(m.PlayerId);
            w.WriteByte(m.PlayerCount);
            w.WriteUInt16(m.MapWidth);
            w.WriteUInt16(m.MapHeight);
            w.WriteUInt64(m.Seed);
            w.WriteUInt64(m.ContentHash);
            w.WriteUInt64(m.ReconnectToken);
            w.WriteBool(m.Reconnected);
            w.WriteUInt32(m.Tick);
        }

        public static WelcomeMessage ReadWelcome(BitReader r) => new WelcomeMessage
        {
            Result = (HandshakeResult)r.ReadByte(),
            PlayerId = r.ReadByte(),
            PlayerCount = r.ReadByte(),
            MapWidth = r.ReadUInt16(),
            MapHeight = r.ReadUInt16(),
            Seed = r.ReadUInt64(),
            ContentHash = r.ReadUInt64(),
            ReconnectToken = r.ReadUInt64(),
            Reconnected = r.ReadBool(),
            Tick = r.ReadUInt32()
        };

        public static void Write(BitWriter w, in TickHeaderMessage m)
        {
            WriteType(w, MessageType.TickHeader);
            w.WriteUInt32(m.Tick);
        }

        public static TickHeaderMessage ReadTickHeader(BitReader r) => new TickHeaderMessage { Tick = r.ReadUInt32() };

        public static void Write(BitWriter w, in SpawnEntityMessage m)
        {
            WriteType(w, MessageType.SpawnEntity);
            WriteEntity(w, m.Entity);
            w.WriteByte((byte)m.Kind);
            w.WriteByte(m.Owner);
            w.WriteByte((byte)m.Building);
            w.WriteByte((byte)m.Node);
            w.WriteUInt16(m.PositionX);
            w.WriteUInt16(m.PositionY);
            w.WriteByte(m.HealthRatio);
            w.WriteBool(m.UnderConstruction);
        }

        public static SpawnEntityMessage ReadSpawn(BitReader r) => new SpawnEntityMessage
        {
            Entity = ReadEntity(r),
            Kind = (EntityKind)r.ReadByte(),
            Owner = r.ReadByte(),
            Building = (BuildingType)r.ReadByte(),
            Node = (ResourceNodeType)r.ReadByte(),
            PositionX = r.ReadUInt16(),
            PositionY = r.ReadUInt16(),
            HealthRatio = r.ReadByte(),
            UnderConstruction = r.ReadBool()
        };

        public static void Write(BitWriter w, in DespawnEntityMessage m)
        {
            WriteType(w, MessageType.DespawnEntity);
            WriteEntity(w, m.Entity);
            w.WriteBool(m.Destroyed);
        }

        public static DespawnEntityMessage ReadDespawn(BitReader r) => new DespawnEntityMessage
        {
            Entity = ReadEntity(r),
            Destroyed = r.ReadBool()
        };

        public static void Write(BitWriter w, in SetIntentMessage m)
        {
            WriteType(w, MessageType.SetIntent);
            WriteEntity(w, m.Entity);
            w.WriteByte((byte)m.Job);
            w.WriteUInt16(m.OriginX);
            w.WriteUInt16(m.OriginY);
            w.WriteUInt16(m.DestinationX);
            w.WriteUInt16(m.DestinationY);
            WriteEntity(w, m.Target);
        }

        public static SetIntentMessage ReadIntent(BitReader r) => new SetIntentMessage
        {
            Entity = ReadEntity(r),
            Job = (JobType)r.ReadByte(),
            OriginX = r.ReadUInt16(),
            OriginY = r.ReadUInt16(),
            DestinationX = r.ReadUInt16(),
            DestinationY = r.ReadUInt16(),
            Target = ReadEntity(r)
        };

        public static void Write(BitWriter w, in CorrectionMessage m)
        {
            WriteType(w, MessageType.Correction);
            WriteEntity(w, m.Entity);
            w.WriteUInt16(m.PositionX);
            w.WriteUInt16(m.PositionY);
            w.WriteByte(m.Heading);
            w.WriteByte(m.HealthRatio);
        }

        public static CorrectionMessage ReadCorrection(BitReader r) => new CorrectionMessage
        {
            Entity = ReadEntity(r),
            PositionX = r.ReadUInt16(),
            PositionY = r.ReadUInt16(),
            Heading = r.ReadByte(),
            HealthRatio = r.ReadByte()
        };

        public static void Write(BitWriter w, in PrivateDeltaMessage m)
        {
            WriteType(w, MessageType.PrivateDelta);
            w.WriteInt32(m.Wood);
            w.WriteInt32(m.Food);
            w.WriteInt32(m.Stone);
            w.WriteInt32(m.Coin);
            w.WriteUInt16(m.PopulationUsed);
            w.WriteUInt16(m.PopulationCap);
        }

        public static PrivateDeltaMessage ReadPrivateDelta(BitReader r) => new PrivateDeltaMessage
        {
            Wood = r.ReadInt32(),
            Food = r.ReadInt32(),
            Stone = r.ReadInt32(),
            Coin = r.ReadInt32(),
            PopulationUsed = r.ReadUInt16(),
            PopulationCap = r.ReadUInt16()
        };

        public static void Write(BitWriter w, in GameEventMessage m)
        {
            WriteType(w, MessageType.GameEvent);
            w.WriteByte((byte)m.Type);
            WriteEntity(w, m.Entity);
            WriteEntity(w, m.Other);
            w.WriteByte(m.Player);
            w.WriteInt32(m.ValueA);
            w.WriteInt32(m.ValueB);
            w.WriteUInt16(m.PositionX);
            w.WriteUInt16(m.PositionY);
        }

        public static GameEventMessage ReadGameEvent(BitReader r) => new GameEventMessage
        {
            Type = (SimEventType)r.ReadByte(),
            Entity = ReadEntity(r),
            Other = ReadEntity(r),
            Player = r.ReadByte(),
            ValueA = r.ReadInt32(),
            ValueB = r.ReadInt32(),
            PositionX = r.ReadUInt16(),
            PositionY = r.ReadUInt16()
        };

        public static void Write(BitWriter w, in CommandRejectedMessage m)
        {
            WriteType(w, MessageType.CommandRejected);
            w.WriteUInt32(m.Sequence);
            w.WriteByte((byte)m.Reason);
        }

        public static CommandRejectedMessage ReadRejected(BitReader r) => new CommandRejectedMessage
        {
            Sequence = r.ReadUInt32(),
            Reason = (RejectReason)r.ReadByte()
        };

        public static void Write(BitWriter w, in MatchEndMessage m)
        {
            WriteType(w, MessageType.MatchEnd);
            w.WriteInt32(m.WinningTeam);
            w.WriteBool(m.LocalPlayerWon);
        }

        public static MatchEndMessage ReadMatchEnd(BitReader r) => new MatchEndMessage
        {
            WinningTeam = r.ReadInt32(),
            LocalPlayerWon = r.ReadBool()
        };
    }
}
