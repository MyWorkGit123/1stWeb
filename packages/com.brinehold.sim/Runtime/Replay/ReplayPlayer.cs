using System.Collections.Generic;
using Brinehold.Sim.Map;
using Brinehold.Sim.World;

namespace Brinehold.Sim.Replay
{
    /// <summary>
    /// Re-simulates a recorded match.
    ///
    /// This is the same <see cref="SimWorld"/> the server runs, fed the same commands on the same
    /// ticks. If the result diverges from the recorded state hashes, the simulation is no longer
    /// deterministic and something has broken — which is exactly what CI is looking for, and why a
    /// divergence is reported with the tick it first appeared on rather than just a pass or fail.
    ///
    /// It is also the replay viewer's engine: the client swaps its network connection for one of
    /// these and every rendering path stays the same.
    /// </summary>
    public sealed class ReplayPlayer
    {
        public readonly SimWorld World;
        public readonly ReplayData Data;

        private int _nextCommand;
        private int _nextCheckpoint;

        public readonly List<Divergence> Divergences = new List<Divergence>();

        public struct Divergence
        {
            public uint Tick;
            public ulong Expected;
            public ulong Actual;
            public override string ToString() => $"tick {Tick}: expected {Expected:X16}, got {Actual:X16}";
        }

        public ReplayPlayer(ReplayData data)
        {
            Data = data;
            World = new SimWorld(data.Header.ToConfig());
            PrototypeMap.Build(World);
        }

        public bool Finished => Data.HasEnd
            ? World.Tick >= Data.EndTick
            : _nextCommand >= Data.Commands.Count && _nextCheckpoint >= Data.Checkpoints.Count;

        /// <summary>Advances one tick, feeding in any commands due to execute on it.</summary>
        public void StepOnce()
        {
            uint executeTick = World.Tick + 1;

            while (_nextCommand < Data.Commands.Count && Data.Commands[_nextCommand].ExecuteTick <= executeTick)
            {
                World.EnqueueCommand(Data.Commands[_nextCommand].Command);
                _nextCommand++;
            }

            World.Step();

            while (_nextCheckpoint < Data.Checkpoints.Count && Data.Checkpoints[_nextCheckpoint].Tick <= World.Tick)
            {
                ReplayCheckpoint checkpoint = Data.Checkpoints[_nextCheckpoint];
                _nextCheckpoint++;

                if (checkpoint.Tick != World.Tick) continue;

                ulong actual = World.ComputeStateHash();
                if (actual != checkpoint.Hash)
                {
                    Divergences.Add(new Divergence
                    {
                        Tick = checkpoint.Tick,
                        Expected = checkpoint.Hash,
                        Actual = actual
                    });
                }
            }
        }

        public void StepTo(uint tick)
        {
            while (World.Tick < tick && !AtHardEnd()) StepOnce();
        }

        /// <summary>
        /// Replays the whole match and reports whether every checkpoint matched.
        /// Stops early on the first divergence when <paramref name="stopOnFirstDivergence"/> is set,
        /// which is what a developer bisecting a determinism bug wants; CI wants the whole list.
        /// </summary>
        public bool Verify(bool stopOnFirstDivergence = false)
        {
            while (!AtHardEnd())
            {
                StepOnce();
                if (stopOnFirstDivergence && Divergences.Count > 0) return false;
            }
            return Divergences.Count == 0;
        }

        private bool AtHardEnd()
        {
            if (Data.HasEnd) return World.Tick >= Data.EndTick;

            uint lastInterest = 0;
            if (Data.Commands.Count > 0) lastInterest = Data.Commands[Data.Commands.Count - 1].ExecuteTick;
            if (Data.Checkpoints.Count > 0)
            {
                uint lastCheckpoint = Data.Checkpoints[Data.Checkpoints.Count - 1].Tick;
                if (lastCheckpoint > lastInterest) lastInterest = lastCheckpoint;
            }
            return World.Tick >= lastInterest;
        }

        public string Summary()
        {
            return $"{Data.Commands.Count} commands, {Data.Checkpoints.Count} checkpoints, " +
                   $"replayed to tick {World.Tick}, " +
                   (Divergences.Count == 0 ? "no divergence" : $"{Divergences.Count} DIVERGENCES");
        }
    }
}
