using Brinehold.Sim.World;

namespace Brinehold.Sim.Systems
{
    /// <summary>
    /// One stage of a simulation tick.
    ///
    /// Systems run in a fixed, declared order (see <see cref="SimWorld"/>), each iterating entities
    /// in dense index order. A system may read anything and write only what its documentation says
    /// it writes; nothing here may allocate on the hot path, use wall-clock time, or iterate an
    /// unordered collection in a way that affects state.
    /// </summary>
    public interface ISimSystem
    {
        void Execute(SimWorld world);
    }

    /// <summary>Geometry helpers shared by the systems that need "am I close enough yet".</summary>
    internal static class SimRange
    {
        /// <summary>Extra reach beyond a target's footprint when harvesting or delivering.</summary>
        public static readonly Brinehold.Core.Math.Fix64 InteractionReach =
            Brinehold.Core.Math.Fix64.FromFraction(18, 10);

        /// <summary>
        /// True when <paramref name="a"/> is close enough to interact with <paramref name="b"/>,
        /// measured against b's footprint so that big buildings are reachable from their edge.
        /// Compares squared distances to avoid a square root in the hot path.
        /// </summary>
        public static bool InReach(EntityStore store, int a, int b, Brinehold.Core.Math.Fix64 extra)
        {
            Brinehold.Core.Math.Fix64 reach =
                Brinehold.Core.Math.Fix64.FromInt(store.FootprintHalf[b]) + extra;
            Brinehold.Core.Math.Fix64 sqr =
                Brinehold.Core.Math.Fix2.SqrDistance(store.Position[a], store.Position[b]);
            return sqr <= reach * reach;
        }
    }
}
