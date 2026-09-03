using System;
using System.Collections.Generic;
using System.Net;
using Brinehold.Net.Reliability;

namespace Brinehold.Net.Transport
{
    /// <summary>
    /// One peer on a UDP link: its address, its reliability state, and its fragment reassembly.
    ///
    /// Shared by both ends — a server holds one of these per client, a client holds exactly one for
    /// the server — so the reliability behaviour is identical in both directions rather than being
    /// implemented twice and drifting.
    /// </summary>
    internal sealed class UdpConnection
    {
        public readonly int Id;
        public readonly IPEndPoint EndPoint;
        public readonly ReliableChannel Reliability;

        /// <summary>Fragments of the message currently being reassembled.</summary>
        private readonly List<byte[]> _fragments = new List<byte[]>();
        private int _expectedFragments;

        public bool Connected = true;

        public UdpConnection(int id, IPEndPoint endPoint, long nowMs)
        {
            Id = id;
            EndPoint = endPoint;
            Reliability = new ReliableChannel(nowMs);
        }

        /// <summary>
        /// Feeds a delivered reliable payload into reassembly. Returns the complete message when the
        /// last fragment arrives, or null while more are outstanding.
        ///
        /// Reassembly relies on the reliable channel's ordering guarantee: fragments are handed up in
        /// sequence, so they can simply be appended rather than indexed.
        /// </summary>
        public byte[]? Reassemble(byte[] payload, byte fragmentIndex, byte fragmentCount)
        {
            if (fragmentCount <= 1) return payload;

            if (fragmentIndex == 0)
            {
                _fragments.Clear();
                _expectedFragments = fragmentCount;
            }
            else if (_expectedFragments != fragmentCount || _fragments.Count != fragmentIndex)
            {
                // Out of step with the group we were building. Drop it rather than splice together
                // fragments from two different messages.
                _fragments.Clear();
                _expectedFragments = 0;
                return null;
            }

            _fragments.Add(payload);
            if (_fragments.Count < _expectedFragments) return null;

            int total = 0;
            for (int i = 0; i < _fragments.Count; i++) total += _fragments[i].Length;

            var complete = new byte[total];
            int offset = 0;
            for (int i = 0; i < _fragments.Count; i++)
            {
                Buffer.BlockCopy(_fragments[i], 0, complete, offset, _fragments[i].Length);
                offset += _fragments[i].Length;
            }

            _fragments.Clear();
            _expectedFragments = 0;
            return complete;
        }
    }
}
