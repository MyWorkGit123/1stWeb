using System;
using System.Collections.Generic;
using Brinehold.Core.Random;

namespace Brinehold.Net.Transport
{
    /// <summary>Latency, jitter and loss to impose on a loopback link.</summary>
    public struct NetworkConditions
    {
        /// <summary>One-way delay in simulation ticks. 4 ticks is 200 ms at 20 Hz.</summary>
        public int LatencyTicks;
        /// <summary>Extra delay of 0..JitterTicks added per packet.</summary>
        public int JitterTicks;
        /// <summary>Percentage of unreliable packets to drop. Reliable packets are never dropped.</summary>
        public int LossPercent;

        public static NetworkConditions Perfect => new NetworkConditions();

        /// <summary>The condition the prototype's acceptance criteria name: 200 ms and 5% loss.</summary>
        public static NetworkConditions Poor => new NetworkConditions
        {
            LatencyTicks = 4,
            JitterTicks = 2,
            LossPercent = 5
        };
    }

    /// <summary>
    /// An in-process network with controllable conditions.
    ///
    /// This is how the integration tests run a real server and real clients without sockets: the
    /// same encoders, the same decoders, the same replication logic, with latency and loss applied
    /// deterministically from a seeded generator so a failing run can be replayed exactly.
    /// </summary>
    public sealed class LoopbackNetwork
    {
        private struct Packet
        {
            public int Connection;
            public byte[] Data;
            public int Length;
            public int DeliverAtTick;
            public ulong Order;
        }

        private readonly List<Packet> _toServer = new List<Packet>();
        private readonly List<Packet> _toClient = new List<Packet>();
        private readonly DeterministicRandom _rng;
        private int _tick;
        private ulong _order;

        public NetworkConditions Conditions;

        /// <summary>Counters for assertions about what actually crossed the link.</summary>
        public int PacketsSentToClients { get; private set; }
        public int PacketsDropped { get; private set; }
        public long BytesToClients { get; private set; }

        public LoopbackNetwork(NetworkConditions conditions, ulong seed = 99)
        {
            Conditions = conditions;
            _rng = new DeterministicRandom(seed);
        }

        public void Tick() => _tick++;

        private int DeliveryTick()
        {
            int delay = Conditions.LatencyTicks;
            if (Conditions.JitterTicks > 0) delay += _rng.NextInt(Conditions.JitterTicks + 1);
            return _tick + delay;
        }

        private bool ShouldDrop(Channel channel)
            => channel == Channel.UnreliableSequenced && Conditions.LossPercent > 0 && _rng.Chance(Conditions.LossPercent);

        public void SendToServer(int connection, ArraySegment<byte> payload, Channel channel)
        {
            if (ShouldDrop(channel)) { PacketsDropped++; return; }
            _toServer.Add(Copy(connection, payload));
        }

        /// <summary>
        /// Invoked for every packet accepted for delivery to a client, before the link delay is
        /// applied. Tests use it to snoop the wire without disturbing delivery; re-injecting packets
        /// to inspect them would re-apply the latency and starve the client forever.
        /// </summary>
        public Action<int, ArraySegment<byte>>? ClientPacketSnoop;

        public void SendToClient(int connection, ArraySegment<byte> payload, Channel channel)
        {
            if (ShouldDrop(channel)) { PacketsDropped++; return; }
            PacketsSentToClients++;
            BytesToClients += payload.Count;
            ClientPacketSnoop?.Invoke(connection, payload);
            _toClient.Add(Copy(connection, payload));
        }

        private Packet Copy(int connection, ArraySegment<byte> payload)
        {
            var data = new byte[payload.Count];
            Array.Copy(payload.Array!, payload.Offset, data, 0, payload.Count);
            return new Packet
            {
                Connection = connection,
                Data = data,
                Length = payload.Count,
                DeliverAtTick = DeliveryTick(),
                Order = _order++
            };
        }

        public bool TryReceiveServer(out int connection, out ArraySegment<byte> payload)
            => TryDequeue(_toServer, -1, out connection, out payload);

        public bool TryReceiveClient(int connection, out ArraySegment<byte> payload)
            => TryDequeue(_toClient, connection, out _, out payload);

        /// <summary>
        /// Dequeues the oldest deliverable packet. Ordering is by send order, not by arrival jitter,
        /// so the reliable-ordered channel behaves as its name promises.
        /// </summary>
        private bool TryDequeue(List<Packet> queue, int filterConnection, out int connection, out ArraySegment<byte> payload)
        {
            int best = -1;
            for (int i = 0; i < queue.Count; i++)
            {
                if (filterConnection >= 0 && queue[i].Connection != filterConnection) continue;
                if (queue[i].DeliverAtTick > _tick) continue;
                if (best < 0 || queue[i].Order < queue[best].Order) best = i;
            }

            if (best < 0)
            {
                connection = -1;
                payload = default;
                return false;
            }

            Packet packet = queue[best];
            queue.RemoveAt(best);
            connection = packet.Connection;
            payload = new ArraySegment<byte>(packet.Data, 0, packet.Length);
            return true;
        }
    }
}
