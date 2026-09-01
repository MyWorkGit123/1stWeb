using System.Collections.Generic;
using Brinehold.Core.Serialization;
using Brinehold.Sim.Commands;
using Brinehold.Sim.World;

namespace Brinehold.Sim.Replay
{
    /// <summary>
    /// Records a match as it is played.
    ///
    /// The server calls this for every command it accepts, and for a state hash every 200 ticks. It
    /// costs a few bytes per order — there is no reason for a competitive match ever not to be
    /// recorded, which is what makes "attach the replay" a reasonable thing to ask of a bug report.
    /// </summary>
    public sealed class ReplayWriter
    {
        private readonly BitWriter _writer = new BitWriter(1 << 16);
        private bool _ended;

        public ReplayHeader Header { get; }
        public int CommandCount { get; private set; }
        public int CheckpointCount { get; private set; }

        public ReplayWriter(MatchConfig config)
        {
            Header = ReplayHeader.FromConfig(config);
            Header.Write(_writer);
        }

        /// <summary>
        /// Records a command and the tick it will execute on. The execution tick matters more than
        /// the arrival tick: replaying by arrival would re-order anything that was delayed.
        /// </summary>
        public void RecordCommand(uint executeTick, Command command)
        {
            if (_ended) return;
            _writer.WriteByte((byte)ReplayFormat.RecordType.Command);
            _writer.WriteUInt32(executeTick);
            ReplayCommandCodec.Write(_writer, command);
            CommandCount++;
        }

        public void RecordStateHash(uint tick, ulong hash)
        {
            if (_ended) return;
            _writer.WriteByte((byte)ReplayFormat.RecordType.StateHash);
            _writer.WriteUInt32(tick);
            _writer.WriteUInt64(hash);
            CheckpointCount++;
        }

        public void RecordEnd(uint tick, int winningTeam)
        {
            if (_ended) return;
            _writer.WriteByte((byte)ReplayFormat.RecordType.End);
            _writer.WriteUInt32(tick);
            _writer.WriteInt32(winningTeam);
            _ended = true;
        }

        public byte[] ToArray() => _writer.ToArray();

        public int ByteLength => _writer.ByteLength;
    }

    public struct ReplayCommandRecord
    {
        public uint ExecuteTick;
        public Command Command;
    }

    public struct ReplayCheckpoint
    {
        public uint Tick;
        public ulong Hash;
    }

    /// <summary>A parsed replay, ready to be re-simulated.</summary>
    public sealed class ReplayData
    {
        public ReplayHeader Header = new ReplayHeader();
        public readonly List<ReplayCommandRecord> Commands = new List<ReplayCommandRecord>();
        public readonly List<ReplayCheckpoint> Checkpoints = new List<ReplayCheckpoint>();
        public bool HasEnd;
        public uint EndTick;
        public int WinningTeam = -1;

        /// <summary>Reads a replay, tolerating truncation: a crashed match still yields what it recorded.</summary>
        public static bool TryParse(byte[] bytes, out ReplayData data, out string error)
        {
            data = new ReplayData();
            var reader = new BitReader(bytes);

            if (!ReplayHeader.TryRead(reader, out ReplayHeader header, out error)) return false;
            data.Header = header;

            while (reader.BitsRemaining >= 8)
            {
                var type = (ReplayFormat.RecordType)reader.ReadByte();
                if (reader.EndOfStream) break;

                switch (type)
                {
                    case ReplayFormat.RecordType.Command:
                    {
                        uint tick = reader.ReadUInt32();
                        Command command = ReplayCommandCodec.Read(reader);
                        if (reader.EndOfStream) return true;   // truncated tail: keep what we have
                        data.Commands.Add(new ReplayCommandRecord { ExecuteTick = tick, Command = command });
                        break;
                    }

                    case ReplayFormat.RecordType.StateHash:
                    {
                        uint tick = reader.ReadUInt32();
                        ulong hash = reader.ReadUInt64();
                        if (reader.EndOfStream) return true;
                        data.Checkpoints.Add(new ReplayCheckpoint { Tick = tick, Hash = hash });
                        break;
                    }

                    case ReplayFormat.RecordType.End:
                    {
                        data.EndTick = reader.ReadUInt32();
                        data.WinningTeam = reader.ReadInt32();
                        data.HasEnd = true;
                        return true;
                    }

                    default:
                        error = $"unknown replay record type {(byte)type}";
                        return false;
                }
            }

            return true;
        }
    }
}
