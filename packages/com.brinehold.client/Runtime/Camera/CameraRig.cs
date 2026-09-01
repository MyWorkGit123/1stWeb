using Brinehold.Core.Math;

namespace Brinehold.Client.CameraControl
{
    /// <summary>
    /// RTS camera state and the rules that move it.
    ///
    /// Kept out of Unity so the clamping, zoom curve and edge-scroll behaviour can be tested; the
    /// MonoBehaviour above simply reads this and positions a Transform. A camera that can be
    /// dragged off the edge of the map, or that zooms unevenly, is the kind of thing players feel
    /// immediately and bug reports describe badly, so it is worth pinning down in tests.
    ///
    /// The camera works in floating point in the view layer — nothing here feeds the simulation —
    /// but it is expressed in Fix64 so it can share the same vector types as everything else.
    /// </summary>
    public sealed class CameraRig
    {
        public Fix2 Focus;
        /// <summary>0 = fully zoomed in, 1 = fully zoomed out.</summary>
        public Fix64 Zoom = Fix64.Half;
        /// <summary>Yaw in radians. RTS cameras rotate about the focus point, not the eye.</summary>
        public Fix64 Yaw;

        public readonly Fix64 MinHeight = Fix64.FromInt(18);
        public readonly Fix64 MaxHeight = Fix64.FromInt(90);

        private readonly int _mapWidth;
        private readonly int _mapHeight;

        public CameraRig(int mapWidth, int mapHeight)
        {
            _mapWidth = mapWidth;
            _mapHeight = mapHeight;
            Focus = new Fix2(Fix64.FromInt(mapWidth / 2), Fix64.FromInt(mapHeight / 2));
        }

        /// <summary>Camera height above the ground for the current zoom.</summary>
        public Fix64 Height => MinHeight + (MaxHeight - MinHeight) * Zoom;

        /// <summary>
        /// Pan speed scales with zoom: at full zoom-out the player is looking at four times the area
        /// and expects the camera to cover it in a comparable number of seconds.
        /// </summary>
        public Fix64 PanSpeed => Fix64.FromInt(18) + Fix64.FromInt(42) * Zoom;

        /// <summary>Moves the focus by a screen-space direction, rotated into world space.</summary>
        public void Pan(Fix2 screenDirection, Fix64 deltaSeconds)
        {
            if (screenDirection.SqrMagnitude == Fix64.Zero) return;

            Fix2 normalised = screenDirection.Normalised;
            Fix64 cos = FixMath.Cos(Yaw);
            Fix64 sin = FixMath.Sin(Yaw);
            var rotated = new Fix2(
                normalised.X * cos - normalised.Y * sin,
                normalised.X * sin + normalised.Y * cos);

            Focus += rotated * (PanSpeed * deltaSeconds);
            Clamp();
        }

        /// <summary>Positive zooms out. Clamped to the full range.</summary>
        public void AddZoom(Fix64 delta)
        {
            Zoom = FixMath.Clamp01(Zoom + delta);
        }

        public void Rotate(Fix64 radians)
        {
            Yaw = FixMath.NormaliseAngle(Yaw + radians);
        }

        /// <summary>Snaps the camera to a point, for minimap clicks and alert jumps.</summary>
        public void JumpTo(Fix2 point)
        {
            Focus = point;
            Clamp();
        }

        /// <summary>
        /// Keeps the focus inside the map. A margin is allowed so the player can centre a unit that
        /// is standing at the very edge of the world.
        /// </summary>
        private void Clamp()
        {
            Fix64 margin = Fix64.FromInt(12);
            Focus = new Fix2(
                FixMath.Clamp(Focus.X, -margin, Fix64.FromInt(_mapWidth) + margin),
                FixMath.Clamp(Focus.Y, -margin, Fix64.FromInt(_mapHeight) + margin));
        }

        /// <summary>
        /// The world-space rectangle roughly visible at the current height, used for double-click
        /// type selection and for interest hints sent to the server.
        /// </summary>
        public void VisibleRegion(out Fix2 min, out Fix2 max)
        {
            Fix64 halfExtent = Height;   // a 45-degree camera sees roughly its own height each way
            min = new Fix2(Focus.X - halfExtent, Focus.Y - halfExtent);
            max = new Fix2(Focus.X + halfExtent, Focus.Y + halfExtent);
        }
    }
}
