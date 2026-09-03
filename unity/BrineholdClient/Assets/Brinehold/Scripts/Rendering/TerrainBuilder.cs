using Brinehold.Sim.Nav;
using Brinehold.Sim.World;
using UnityEngine;

namespace Brinehold.Unity.Boot
{
    /// <summary>
    /// Builds a mesh for the map from the navigation grid.
    ///
    /// One quad per cell would be 25,600 quads on the prototype map, so cells are merged into runs
    /// of the same terrain type along each row before being emitted. The result is a handful of
    /// thousand triangles rather than a hundred thousand, and it is built once at load.
    ///
    /// Terrain is drawn from the client's own copy of the grid, which contains only public map data.
    /// </summary>
    public sealed class TerrainBuilder : MonoBehaviour
    {
        public Material LandMaterial;
        public Material WaterMaterial;
        public Material RockMaterial;

        [Tooltip("Height above the ground plane, per terrain type, in metres.")]
        public float WaterDepth = -0.6f;
        public float RockHeight = 2.5f;

        public void Build(NavGrid grid)
        {
            BuildLayer(grid, TerrainType.Land, 0f, LandMaterial, "Land");
            BuildLayer(grid, TerrainType.Water, WaterDepth, WaterMaterial, "Water");
            BuildLayer(grid, TerrainType.Blocked, RockHeight, RockMaterial, "Rock");
        }

        private void BuildLayer(NavGrid grid, TerrainType type, float height, Material material, string name)
        {
            var vertices = new System.Collections.Generic.List<Vector3>();
            var triangles = new System.Collections.Generic.List<int>();
            var uvs = new System.Collections.Generic.List<Vector2>();

            for (int y = 0; y < grid.Height; y++)
            {
                int runStart = -1;
                for (int x = 0; x <= grid.Width; x++)
                {
                    bool matches = x < grid.Width && grid.TerrainAt(grid.Index(x, y)) == type;

                    if (matches && runStart < 0) runStart = x;
                    else if (!matches && runStart >= 0)
                    {
                        AddQuad(vertices, triangles, uvs, runStart, y, x - runStart, 1, height);
                        runStart = -1;
                    }
                }
            }

            if (vertices.Count == 0) return;

            var mesh = new Mesh { name = $"Terrain_{name}" };
            mesh.indexFormat = vertices.Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var child = new GameObject($"Terrain_{name}");
            child.transform.SetParent(transform, false);
            child.AddComponent<MeshFilter>().sharedMesh = mesh;
            child.AddComponent<MeshRenderer>().sharedMaterial = material;

            // Land carries a collider so that mouse picking can raycast against the ground plane.
            if (type == TerrainType.Land) child.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        private static void AddQuad(System.Collections.Generic.List<Vector3> vertices,
                                    System.Collections.Generic.List<int> triangles,
                                    System.Collections.Generic.List<Vector2> uvs,
                                    int x, int y, int width, int depth, float height)
        {
            int baseIndex = vertices.Count;

            vertices.Add(new Vector3(x, height, y));
            vertices.Add(new Vector3(x + width, height, y));
            vertices.Add(new Vector3(x + width, height, y + depth));
            vertices.Add(new Vector3(x, height, y + depth));

            uvs.Add(new Vector2(0, 0));
            uvs.Add(new Vector2(width, 0));
            uvs.Add(new Vector2(width, depth));
            uvs.Add(new Vector2(0, depth));

            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 3);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex);
            triangles.Add(baseIndex + 2);
            triangles.Add(baseIndex + 1);
        }
    }
}
