using UnityEngine;

namespace Brinehold.Unity.Boot
{
    /// <summary>
    /// Builds the entire prototype scene at runtime from primitives.
    ///
    /// Drop this one component into an empty scene and press Play. Nothing else needs wiring: it
    /// creates the camera, lighting, terrain, unit prefabs, fog quad, HUD and input controllers.
    ///
    /// This exists because the prototype's job is to prove the simulation and the network model, and
    /// spending that milestone hand-authoring prefabs and canvases would prove nothing. The art pass
    /// in M16 replaces the primitives; every script above keeps working, because none of them know
    /// what a unit looks like.
    /// </summary>
    public sealed class PrototypeSceneSetup : MonoBehaviour
    {
        [Header("Match")]
        public ulong Seed = 1;
        public byte LocalPlayer;

        private void Awake()
        {
            var game = gameObject.AddComponent<GameBootstrap>();
            game.Seed = Seed;
            game.LocalPlayer = LocalPlayer;

            Camera camera = BuildCamera();
            game.SceneCamera = camera;
            game.Terrain = BuildTerrain();
            game.Views = BuildViewPool();
            game.Fog = BuildFog();

            BuildLighting();

            var cameraController = gameObject.AddComponent<CameraController>();
            cameraController.Game = game;
            cameraController.Target = camera;

            var selection = gameObject.AddComponent<SelectionController>();
            selection.Game = game;
            selection.CameraRig = cameraController;
            selection.Views = game.Views;

            var hud = gameObject.AddComponent<HudOverlay>();
            hud.Game = game;

            var minimap = gameObject.AddComponent<MinimapOverlay>();
            minimap.Game = game;
        }

        private static Camera BuildCamera()
        {
            var holder = new GameObject("Main Camera");
            Camera camera = holder.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.farClipPlane = 600f;
            camera.backgroundColor = new Color(0.08f, 0.10f, 0.14f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            return camera;
        }

        private static void BuildLighting()
        {
            var holder = new GameObject("Sun");
            Light light = holder.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.color = new Color(1f, 0.96f, 0.88f);
            holder.transform.rotation = Quaternion.Euler(52f, 35f, 0f);
            RenderSettings.ambientLight = new Color(0.32f, 0.34f, 0.38f);
        }

        private TerrainBuilder BuildTerrain()
        {
            var holder = new GameObject("Terrain");
            TerrainBuilder builder = holder.AddComponent<TerrainBuilder>();
            builder.LandMaterial = MakeMaterial(new Color(0.28f, 0.36f, 0.22f));
            builder.WaterMaterial = MakeMaterial(new Color(0.13f, 0.24f, 0.38f));
            builder.RockMaterial = MakeMaterial(new Color(0.30f, 0.29f, 0.27f));
            return builder;
        }

        private EntityViewPool BuildViewPool()
        {
            var holder = new GameObject("Entities");
            EntityViewPool pool = holder.AddComponent<EntityViewPool>();

            pool.WorkerPrefab = MakeUnitPrefab("Worker", PrimitiveType.Capsule, new Vector3(0.6f, 0.5f, 0.6f));
            pool.SoldierPrefab = MakeUnitPrefab("Soldier", PrimitiveType.Capsule, new Vector3(0.75f, 0.7f, 0.75f));
            pool.ShipPrefab = MakeUnitPrefab("Ship", PrimitiveType.Cube, new Vector3(1.4f, 0.5f, 3.0f));
            pool.BuildingPrefab = MakeUnitPrefab("Building", PrimitiveType.Cube, new Vector3(1.0f, 1.2f, 1.0f));
            pool.ResourcePrefab = MakeUnitPrefab("Resource", PrimitiveType.Cylinder, new Vector3(0.5f, 0.9f, 0.5f));

            return pool;
        }

        private FogRenderer BuildFog()
        {
            var holder = new GameObject("Fog");
            FogRenderer fog = holder.AddComponent<FogRenderer>();

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "FogQuad";
            quad.transform.SetParent(holder.transform, false);
            Object.Destroy(quad.GetComponent<Collider>());

            var renderer = quad.GetComponent<Renderer>();
            renderer.material = MakeTransparentMaterial();
            fog.FogQuad = renderer;
            return fog;
        }

        /// <summary>
        /// A prefab-in-memory: an inactive template the pool clones. Building it from primitives
        /// keeps the prototype free of binary assets that cannot be reviewed in a pull request.
        /// </summary>
        private GameObject MakeUnitPrefab(string name, PrimitiveType shape, Vector3 scale)
        {
            GameObject root = new GameObject(name);
            root.SetActive(false);
            root.transform.SetParent(transform, false);

            GameObject body = GameObject.CreatePrimitive(shape);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = scale;
            body.transform.localPosition = new Vector3(0f, scale.y * 0.5f, 0f);
            Object.Destroy(body.GetComponent<Collider>());

            var view = root.AddComponent<EntityView>();
            view.TintedRenderer = body.GetComponent<Renderer>();
            view.TintedRenderer.sharedMaterial = MakeMaterial(Color.white);

            // A flat ring under the unit, shown when it is selected.
            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Quad);
            ring.name = "SelectionRing";
            ring.transform.SetParent(root.transform, false);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            ring.transform.localScale = new Vector3(1.6f, 1.6f, 1f);
            ring.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            Object.Destroy(ring.GetComponent<Collider>());
            ring.GetComponent<Renderer>().sharedMaterial = MakeMaterial(new Color(0.4f, 1f, 0.5f));
            ring.SetActive(false);
            view.SelectionRing = ring;

            // A small marker shown while the unit is carrying a load, so hauling is visible.
            GameObject carry = GameObject.CreatePrimitive(PrimitiveType.Cube);
            carry.name = "Carry";
            carry.transform.SetParent(root.transform, false);
            carry.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);
            carry.transform.localPosition = new Vector3(0f, scale.y + 0.35f, 0f);
            Object.Destroy(carry.GetComponent<Collider>());
            carry.GetComponent<Renderer>().sharedMaterial = MakeMaterial(new Color(0.75f, 0.55f, 0.25f));
            carry.SetActive(false);
            view.CarryIndicator = carry;

            return root;
        }

        private static Material MakeMaterial(Color colour)
        {
            // Works with both the built-in pipeline and URP: URP's Lit shader is preferred when
            // present, and the built-in Standard shader is the fallback.
            // Explicit rather than ??: UnityEngine.Object overloads == but the null-coalescing
            // operator does not use that overload, so ?? on Unity objects is a well-known trap.
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var material = new Material(shader);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", colour);
            if (material.HasProperty("_Color")) material.SetColor("_Color", colour);
            return material;
        }

        private static Material MakeTransparentMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            var material = new Material(shader);
            material.SetFloat("_Surface", 1f);           // transparent
            material.renderQueue = 3000;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            return material;
        }
    }
}
