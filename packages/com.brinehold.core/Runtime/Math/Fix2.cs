using System;
using System.Runtime.CompilerServices;

namespace Brinehold.Core.Math
{
    /// <summary>
    /// Deterministic 2D vector on the ground plane. The simulation is 2.5D: horizontal position is
    /// a <see cref="Fix2"/> and vertical position is a discrete terrain tier, so slopes never leak
    /// floating-point error into movement.
    /// </summary>
    public readonly struct Fix2 : IEquatable<Fix2>
    {
        public readonly Fix64 X;
        public readonly Fix64 Y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Fix2(Fix64 x, Fix64 y) { X = x; Y = y; }

        public static Fix2 Zero => new Fix2(Fix64.Zero, Fix64.Zero);
        public static Fix2 One => new Fix2(Fix64.One, Fix64.One);
        public static Fix2 UnitX => new Fix2(Fix64.One, Fix64.Zero);
        public static Fix2 UnitY => new Fix2(Fix64.Zero, Fix64.One);

        public static Fix2 FromInt(int x, int y) => new Fix2(Fix64.FromInt(x), Fix64.FromInt(y));

        public static Fix2 operator +(Fix2 a, Fix2 b) => new Fix2(a.X + b.X, a.Y + b.Y);
        public static Fix2 operator -(Fix2 a, Fix2 b) => new Fix2(a.X - b.X, a.Y - b.Y);
        public static Fix2 operator -(Fix2 a) => new Fix2(-a.X, -a.Y);
        public static Fix2 operator *(Fix2 a, Fix64 s) => new Fix2(a.X * s, a.Y * s);
        public static Fix2 operator *(Fix64 s, Fix2 a) => new Fix2(a.X * s, a.Y * s);
        public static Fix2 operator /(Fix2 a, Fix64 s) => new Fix2(a.X / s, a.Y / s);

        public static bool operator ==(Fix2 a, Fix2 b) => a.X == b.X && a.Y == b.Y;
        public static bool operator !=(Fix2 a, Fix2 b) => a.X != b.X || a.Y != b.Y;

        public Fix64 SqrMagnitude => X * X + Y * Y;

        public Fix64 Magnitude => FixMath.Sqrt(X * X + Y * Y);

        /// <summary>Returns the zero vector for a zero-length input rather than dividing by zero.</summary>
        public Fix2 Normalised
        {
            get
            {
                Fix64 m = Magnitude;
                return m == Fix64.Zero ? Zero : new Fix2(X / m, Y / m);
            }
        }

        public static Fix64 Dot(Fix2 a, Fix2 b) => a.X * b.X + a.Y * b.Y;

        /// <summary>Z component of the 3D cross product — positive when b is counter-clockwise of a.</summary>
        public static Fix64 Cross(Fix2 a, Fix2 b) => a.X * b.Y - a.Y * b.X;

        public static Fix64 Distance(Fix2 a, Fix2 b) => (a - b).Magnitude;

        public static Fix64 SqrDistance(Fix2 a, Fix2 b) => (a - b).SqrMagnitude;

        public static Fix2 Lerp(Fix2 a, Fix2 b, Fix64 t)
            => new Fix2(FixMath.Lerp(a.X, b.X, t), FixMath.Lerp(a.Y, b.Y, t));

        /// <summary>
        /// Moves from <paramref name="current"/> toward <paramref name="target"/> by at most
        /// <paramref name="maxDelta"/>, landing exactly on the target when it is within reach.
        /// This exactness matters: it stops units oscillating around a destination forever.
        /// </summary>
        public static Fix2 MoveTowards(Fix2 current, Fix2 target, Fix64 maxDelta)
        {
            Fix2 delta = target - current;
            Fix64 sqr = delta.SqrMagnitude;
            if (sqr == Fix64.Zero || (maxDelta >= Fix64.Zero && sqr <= maxDelta * maxDelta)) return target;
            Fix64 magnitude = FixMath.Sqrt(sqr);
            return current + delta / magnitude * maxDelta;
        }

        public Fix64 Angle => FixMath.Atan2(Y, X);

        public static Fix2 FromAngle(Fix64 radians) => new Fix2(FixMath.Cos(radians), FixMath.Sin(radians));

        public bool Equals(Fix2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object? obj) => obj is Fix2 other && Equals(other);
        public override int GetHashCode() => unchecked((X.GetHashCode() * 397) ^ Y.GetHashCode());
        public override string ToString() => $"({X}, {Y})";
    }
}
