using Brinehold.Core.Math;

namespace Brinehold.Core.Serialization
{
    /// <summary>
    /// Lossy encodings for values that only need display precision on the wire.
    ///
    /// These are used for correction messages, never for anything the simulation reads back:
    /// quantised values enter the client's replica for rendering only. The server's own state stays
    /// full-precision <see cref="Fix64"/>.
    /// </summary>
    public static class Quantise
    {
        /// <summary>Five-centimetre world grid. A 3.2 km map fits in 16 bits per axis.</summary>
        public const int PositionUnitsPerMetre = 20;
        public const int PositionBits = 16;
        public const int MaxPositionMetres = 3276;

        public static ushort EncodePosition(Fix64 metres)
        {
            // Round to nearest rather than truncating: truncation doubles the worst-case error and
            // biases every quantised position toward the map origin.
            int units = (metres * PositionUnitsPerMetre + Fix64.Half).ToInt();
            if (units < 0) units = 0;
            if (units > 65535) units = 65535;
            return (ushort)units;
        }

        public static Fix64 DecodePosition(ushort units)
            => Fix64.FromFraction(units, PositionUnitsPerMetre);

        /// <summary>Heading to 8 bits — 1.4 degrees, well below what a player can perceive on a unit.</summary>
        public static byte EncodeAngle(Fix64 radians)
        {
            Fix64 normalised = FixMath.NormaliseAngle(radians) + Fix64.Pi;
            Fix64 turns = normalised / Fix64.TwoPi;
            int value = (turns * 256).ToInt();
            return (byte)(value & 0xFF);
        }

        public static Fix64 DecodeAngle(byte encoded)
            => Fix64.FromFraction(encoded, 256) * Fix64.TwoPi - Fix64.Pi;

        /// <summary>Health and similar 0-1 ratios, to roughly 0.4% resolution.</summary>
        public static byte EncodeUnitRatio(Fix64 ratio)
        {
            int value = (FixMath.Clamp01(ratio) * 255 + Fix64.Half).ToInt();
            return (byte)value;
        }

        public static Fix64 DecodeUnitRatio(byte encoded) => Fix64.FromFraction(encoded, 255);
    }
}
