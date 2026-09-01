using Brinehold.Sim.World;
using UnityEngine;

namespace Brinehold.Unity.Boot
{
    /// <summary>
    /// Draws the fog of war as a texture over the map.
    ///
    /// This is presentation only. The security boundary is the server's replication filter, not this
    /// shader: entities the player cannot see were never sent, so there is nothing underneath this
    /// texture to uncover. Turning the fog off in a modified client would reveal terrain the player
    /// has already explored and nothing else.
    /// </summary>
    public sealed class FogRenderer : MonoBehaviour
    {
        public Renderer FogQuad;

        [Tooltip("Alpha over cells that have never been seen.")]
        [Range(0f, 1f)] public float UnexploredAlpha = 1.0f;

        [Tooltip("Alpha over cells that have been seen but are not currently visible.")]
        [Range(0f, 1f)] public float ExploredAlpha = 0.45f;

        private Texture2D _texture;
        private Color32[] _pixels;
        private int _width;
        private int _height;

        private static readonly int FogTextureProperty = Shader.PropertyToID("_BaseMap");

        public void Initialise(int width, int height)
        {
            _width = width;
            _height = height;
            _texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _pixels = new Color32[width * height];

            if (FogQuad != null)
            {
                FogQuad.material.SetTexture(FogTextureProperty, _texture);
                FogQuad.transform.position = new Vector3(width / 2f, 0.4f, height / 2f);
                FogQuad.transform.localScale = new Vector3(width, height, 1f);
                FogQuad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }
        }

        /// <summary>
        /// Rebuilds the fog texture from the authoritative fog grid.
        ///
        /// In listen mode the client has the server in-process, so it reads the grid directly. Once
        /// the socket transport lands the client will maintain its own copy from the replication
        /// stream instead; the rendering code does not change.
        /// </summary>
        public void Refresh(SimWorld world, byte player)
        {
            if (_texture == null) return;

            for (int i = 0; i < _pixels.Length; i++)
            {
                byte alpha;
                if (world.Fog.IsVisible(player, i)) alpha = 0;
                else if (world.Fog.IsExplored(player, i)) alpha = (byte)(ExploredAlpha * 255f);
                else alpha = (byte)(UnexploredAlpha * 255f);

                _pixels[i] = new Color32(0, 0, 0, alpha);
            }

            _texture.SetPixels32(_pixels);
            _texture.Apply(false);
        }
    }
}
