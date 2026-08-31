using System;
using Brinehold.Core.Serialization;
using Brinehold.Net.Transport;
using Brinehold.Protocol;
using Brinehold.Sim.Commands;

namespace Brinehold.Net.Client
{
    /// <summary>
    /// A client's end of the wire.
    ///
    /// It encodes orders and decodes the server's stream into a <see cref="ReplicaWorld"/>. It holds
    /// no authority: the sequence number it stamps on a command exists so the server can reject
    /// replays, and the local optimistic feedback a UI shows is purely visual until the server's
    /// answer arrives.
    /// </summary>
    public sealed class ClientConnection
    {
        private readonly LoopbackNetwork _network;
        private readonly int _connectionId;
        private readonly BitWriter _writer = new BitWriter(1024);
        private uint _sequence;

        public readonly ReplicaWorld Replica;

        /// <summary>Commands sent that the server has not answered. Used by the UI to show pending orders.</summary>
        public int PendingCommands { get; private set; }

        public ClientConnection(LoopbackNetwork network, int connectionId, ReplicaWorld replica)
        {
            _network = network;
            _connectionId = connectionId;
            Replica = replica;
        }

        public uint NextSequence() => ++_sequence;

        /// <summary>Sends an order. The player id on the command is ignored by the server.</summary>
        public void Send(Command command)
        {
            command.Sequence = NextSequence();
            _writer.Reset();
            MessageCodec.Write(_writer, command);
            _network.SendToServer(_connectionId, _writer.AsSegment(), Channel.ReliableOrdered);
            PendingCommands++;
        }

        /// <summary>
        /// Sends a command exactly as given, without renumbering it. Used by the cheat-client test
        /// harness to replay a stale sequence number, which the server must refuse.
        /// </summary>
        public void SendRaw(Command command)
        {
            _writer.Reset();
            MessageCodec.Write(_writer, command);
            _network.SendToServer(_connectionId, _writer.AsSegment(), Channel.ReliableOrdered);
        }

        /// <summary>Pulls everything the server has sent and applies it, then extrapolates one tick.</summary>
        public void Pump()
        {
            while (_network.TryReceiveClient(_connectionId, out ArraySegment<byte> payload))
            {
                Replica.Receive(payload);
                PendingCommands = Math.Max(0, PendingCommands - Replica.Rejections.Count);
            }
            Replica.Step();
        }
    }
}
