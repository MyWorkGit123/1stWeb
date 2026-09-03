using System;
using System.Collections.Generic;
using Brinehold.Core.Collections;
using Brinehold.Core.Serialization;
using Brinehold.Sim.Commands;
using Brinehold.Sim.World;

namespace Brinehold.Sim.Replay
{
    /// <summary>
    /// The replay file format.
    ///
    /// A replay is a match configuration plus the command stream, not a recording of state. Because
    /// the simulation is deterministic, replaying the commands reproduces the match exactly — which
    /// is why a ninety-minute eight-player match fits in a couple of megabytes instead of gigabytes,
    /// and why a bug report with a replay attached is a bug report a developer can step through.
    ///
    /// State hashes are written every 200 ticks. They are not needed to play the replay back; they
    /// are there so that playback can *prove* it reproduced the original, and so CI can detect a
    /// determinism regression the moment it appears.
    ///
    /// Deliberately independent of the wire format: the replay codec lives in the simulation
    /// assembly and knows nothing about the network protocol, so a protocol change does not
    /// invalidate the replay archive.
    /// </summary>
    public static class ReplayFormat
    {
        public const uint Magic = 0x52485242;   // "BRHR" little-endian
        public const ushort FormatVersion = 1;

        public enum RecordType : byte
        {
            Command = 0,
            StateHash = 1,
            End = 2
        }
    }

    public sealed class ReplayHeader
    {
        public ushort FormatVersion = ReplayFormat.FormatVersion;
        public ulong ContentHash;
        public ulong Seed;
        public ushort MapWidth;
        public ushort MapHeight;
        public VictoryCondition Victory;
        public string[] PlayerNames = Array.Empty<string>();
        public byte[] Teams = Array.Empty<byte>();

        public int PlayerCount => PlayerNames.Length;

        public static ReplayHeader FromConfig(MatchConfig config) => new ReplayHeader
        {
            ContentHash = config.ContentHash(),
            Seed = config.Seed,
            MapWidth = (ushort)config.MapWidth,
            MapHeight = (ushort)config.MapHeight,
            Victory = config.Victory,
            PlayerNames = (string[])config.PlayerNames.Clone(),
            Teams = (byte[])config.Teams.Clone()
        };

        /// <summary>Rebuilds the match configuration a replay was recorded from.</summary>
        public MatchConfig ToConfig() => new MatchConfig
        {
            Seed = Seed,
            PlayerCount = PlayerCount,
            MapWidth = MapWidth,
            MapHeight = MapHeight,
            Victory = Victory,
            PlayerNames = (string[])PlayerNames.Clone(),
            Teams = (byte[])Teams.Clone()
        };

        public void Write(BitWriter w)
        {
            w.WriteUInt32(ReplayFormat.Magic);
            w.WriteUInt16(FormatVersion);
            w.WriteUInt64(ContentHash);
            w.WriteUInt64(Seed);
            w.WriteUInt16(MapWidth);
            w.WriteUInt16(MapHeight);
            w.WriteByte((byte)Victory);
            w.WriteByte((byte)PlayerCount);
            for (int i = 0; i < PlayerCount; i++)
            {
                w.WriteString(PlayerNames[i] ?? string.Empty);
                w.WriteByte(i < Teams.Length ? Teams[i] : (byte)i);
            }
        }

        public static bool TryRead(BitReader r, out ReplayHeader header, out string error)
        {
            header = new ReplayHeader();
            error = string.Empty;

            if (r.ReadUInt32() != ReplayFormat.Magic) { error = "not a Brinehold replay"; return false; }

            header.FormatVersion = r.ReadUInt16();
            if (header.FormatVersion != ReplayFormat.FormatVersion)
            {
                error = $"replay format version {header.FormatVersion}, this build reads {ReplayFormat.FormatVersion}";
                return false;
            }

            header.ContentHash = r.ReadUInt64();
            header.Seed = r.ReadUInt64();
            header.MapWidth = r.ReadUInt16();
            header.MapHeight = r.ReadUInt16();
            header.Victory = (VictoryCondition)r.ReadByte();

            int players = r.ReadByte();
            if (players is <= 0 or > SimConstants.MaxPlayers) { error = $"implausible player count {players}"; return false; }

            header.PlayerNames = new string[players];
            header.Teams = new byte[players];
            for (int i = 0; i < players; i++)
            {
                header.PlayerNames[i] = r.ReadString();
                header.Teams[i] = r.ReadByte();
            }

            if (r.EndOfStream) { error = "replay header is truncated"; return false; }
            return true;
        }
    }

    /// <summary>
    /// Serialises a command for the replay log.
    ///
    /// Separate from the wire codec on purpose. The wire format optimises for bytes on a hot path;
    /// this optimises for staying readable by future builds, so an archive of replays survives
    /// protocol churn.
    /// </summary>
    public static class ReplayCommandCodec
    {
        public static void Write(BitWriter w, Command command)
        {
            w.WriteByte(command.PlayerId);
            w.WriteUInt32(command.Sequence);
            w.WriteByte((byte)command.Type);

            int count = Math.Min(command.EntityCount, Command.MaxEntities);
            w.WriteBits((uint)count, 9);
            for (int i = 0; i < count; i++) w.WriteUInt32(command.Entities[i].Raw);

            w.WriteUInt32(command.TargetEntity.Raw);
            w.WriteUInt16((ushort)Math.Clamp(command.TargetCellX, 0, 65535));
            w.WriteUInt16((ushort)Math.Clamp(command.TargetCellY, 0, 65535));
            w.WriteByte((byte)command.Building);
            w.WriteByte((byte)command.TrainKind);
        }

        public static Command Read(BitReader r)
        {
            var command = new Command
            {
                PlayerId = r.ReadByte(),
                Sequence = r.ReadUInt32(),
                Type = (CommandType)r.ReadByte()
            };

            int count = (int)r.ReadBits(9);
            int toRead = Math.Min(count, Command.MaxEntities);
            var entities = new EntityId[toRead];
            for (int i = 0; i < toRead; i++) entities[i] = new EntityId(r.ReadUInt32());

            command.Entities = entities;
            command.EntityCount = count;
            command.TargetEntity = new EntityId(r.ReadUInt32());
            command.TargetCellX = r.ReadUInt16();
            command.TargetCellY = r.ReadUInt16();
            command.Building = (BuildingType)r.ReadByte();
            command.TrainKind = (EntityKind)r.ReadByte();
            return command;
        }
    }
}
