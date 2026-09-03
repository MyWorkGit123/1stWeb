using Brinehold.Net.Client;
using Brinehold.Sim.Nav;
using Brinehold.Sim.World;
using UnityEngine;

namespace Brinehold.Unity.Boot
{
    /// <summary>
    /// The minimap, drawn as a texture rebuilt a few times a second.
    ///
    /// It draws only what the replica knows, which means it inherits the fog guarantee for free:
    /// an enemy the server has not told this client about cannot appear on the minimap, because the
    /// client has no record of it to draw.
    ///
    /// Explored terrain is currently read from the server's fog grid directly, which is only
    /// possible because listen mode has the server in-process. When the client connects to a remote
    /// server it will maintain its own explored-terrain mask from the replication stream instead;
    /// the drawing code does not change.
    /// </summary>
    public sealed class MinimapOverlay : MonoBehaviour
    {
        public GameBootstrap Game;
        public int Size = 200;
        public int Margin = 8;

        [Tooltip("How often to rebuild the minimap texture, in seconds.")]
        public float RefreshInterval = 0.2f;

        private Texture2D _texture;
        private Color32[] _pixels;
        private float _nextRefresh;

        private static readonly Color32 Unexplored = new Color32(8, 9, 12, 255);
        private static readonly Color32 LandColour = new Color32(58, 74, 48, 255);
        private static readonly Color32 WaterColour = new Color32(28, 48, 74, 255);
        private static readonly Color32 RockColour = new Color32(70, 68, 64, 255);

        private void Update()
        {
            if (Game == null || Game.ClientNav == null) return;
            if (Time.time < _nextRefresh) return;
            _nextRefresh = Time.time + RefreshInterval;
            Rebuild();
        }

        private void Rebuild()
        {
            NavGrid nav = Game.ClientNav;
            if (_texture == null)
            {
                _texture = new Texture2D(nav.Width, nav.Height, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Point
                };
                _pixels = new Color32[nav.Width * nav.Height];
            }

            for (int i = 0; i < _pixels.Length; i++)
            {
                if (!Game.Host.World.Fog.IsExplored(Game.LocalPlayer, i)) { _pixels[i] = Unexplored; continue; }

                switch (nav.TerrainAt(i))
                {
                    case TerrainType.Water: _pixels[i] = WaterColour; break;
                    case TerrainType.Blocked: _pixels[i] = RockColour; break;
                    default: _pixels[i] = LandColour; break;
                }
            }

            foreach (ReplicaWorld.Entity entity in Game.Replica.Entities)
            {
                if (entity.Kind == EntityKind.ResourceNode) continue;

                int x = entity.State.Value.Position.X.ToInt();
                int y = entity.State.Value.Position.Y.ToInt();
                if (x < 0 || y < 0 || x >= nav.Width || y >= nav.Height) continue;

                Color32 colour = entity.Owner == Game.LocalPlayer
                    ? new Color32(90, 150, 235, 255)
                    : new Color32(225, 100, 65, 255);

                int radius = entity.Kind == EntityKind.Building ? 2 : 1;
                for (int dy = -radius; dy <= radius; dy++)
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int px = x + dx, py = y + dy;
                    if (px < 0 || py < 0 || px >= nav.Width || py >= nav.Height) continue;
                    _pixels[py * nav.Width + px] = colour;
                }
            }

            _texture.SetPixels32(_pixels);
            _texture.Apply(false);
        }

        private void OnGUI()
        {
            if (_texture == null) return;

            var rect = new Rect(Margin, Screen.height - Size - Margin - 130, Size, Size);
            GUI.DrawTexture(rect, _texture, ScaleMode.StretchToFill);

            // Clicking the minimap jumps the camera, as every RTS player expects.
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                Vector2 local = Event.current.mousePosition - rect.position;
                float u = local.x / rect.width;
                float v = 1f - local.y / rect.height;

                Game.Rig.JumpTo(new Brinehold.Core.Math.Fix2(
                    (Brinehold.Core.Math.Fix64)(u * Game.ClientNav.Width),
                    (Brinehold.Core.Math.Fix64)(v * Game.ClientNav.Height)));
                Event.current.Use();
            }
        }
    }
}
