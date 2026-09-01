using Brinehold.Core.Math;
using UnityEngine;

namespace Brinehold.Unity.Boot
{
    /// <summary>
    /// Reads input and drives the tested camera model.
    ///
    /// Every rule about how the camera behaves — pan speed against zoom, clamping, rotation — lives
    /// in CameraRig, which is engine-independent and has unit tests. This class only converts key
    /// presses and mouse movement into calls, and positions the Transform from the result.
    /// </summary>
    public sealed class CameraController : MonoBehaviour
    {
        public GameBootstrap Game;
        public Camera Target;

        [Header("Feel")]
        [Range(0f, 60f)] public float EdgeScrollMargin = 12f;
        public bool EdgeScrollEnabled = true;
        [Range(15f, 75f)] public float Pitch = 50f;
        [Range(0f, 30f)] public float RotateDegreesPerSecond = 90f;

        private void LateUpdate()
        {
            if (Game == null || Target == null || Game.Rig == null) return;

            float delta = Time.deltaTime;
            ReadPan(delta);
            ReadZoom();
            ReadRotate(delta);
            Apply();
        }

        private void ReadPan(float delta)
        {
            var direction = Vector2.zero;

            if (UnityEngine.Input.GetKey(KeyCode.W) || UnityEngine.Input.GetKey(KeyCode.UpArrow)) direction.y += 1f;
            if (UnityEngine.Input.GetKey(KeyCode.S) || UnityEngine.Input.GetKey(KeyCode.DownArrow)) direction.y -= 1f;
            if (UnityEngine.Input.GetKey(KeyCode.A) || UnityEngine.Input.GetKey(KeyCode.LeftArrow)) direction.x -= 1f;
            if (UnityEngine.Input.GetKey(KeyCode.D) || UnityEngine.Input.GetKey(KeyCode.RightArrow)) direction.x += 1f;

            if (EdgeScrollEnabled && Application.isFocused)
            {
                Vector3 mouse = UnityEngine.Input.mousePosition;
                if (mouse.x >= 0 && mouse.x < Screen.width && mouse.y >= 0 && mouse.y < Screen.height)
                {
                    if (mouse.x < EdgeScrollMargin) direction.x -= 1f;
                    if (mouse.x > Screen.width - EdgeScrollMargin) direction.x += 1f;
                    if (mouse.y < EdgeScrollMargin) direction.y -= 1f;
                    if (mouse.y > Screen.height - EdgeScrollMargin) direction.y += 1f;
                }
            }

            if (direction.sqrMagnitude <= 0f) return;

            Game.Rig.Pan(
                new Fix2((Fix64)direction.x, (Fix64)direction.y),
                (Fix64)delta);
        }

        private void ReadZoom()
        {
            float scroll = UnityEngine.Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) < 0.01f) return;
            Game.Rig.AddZoom((Fix64)(-scroll * 0.08f));
        }

        private void ReadRotate(float delta)
        {
            float rotation = 0f;
            if (UnityEngine.Input.GetKey(KeyCode.Q)) rotation += 1f;
            if (UnityEngine.Input.GetKey(KeyCode.E)) rotation -= 1f;
            if (Mathf.Abs(rotation) < 0.01f) return;

            Game.Rig.Rotate((Fix64)(rotation * RotateDegreesPerSecond * Mathf.Deg2Rad * delta));
        }

        private void Apply()
        {
            var focus = new Vector3(
                (float)Game.Rig.Focus.X.ToDouble(), 0f, (float)Game.Rig.Focus.Y.ToDouble());
            float height = (float)Game.Rig.Height.ToDouble();
            float yaw = (float)Game.Rig.Yaw.ToDouble() * Mathf.Rad2Deg;

            var rotation = Quaternion.Euler(Pitch, yaw, 0f);
            Target.transform.rotation = rotation;
            Target.transform.position = focus - rotation * Vector3.forward * height;
        }

        /// <summary>World point under the cursor on the ground plane, for picking and orders.</summary>
        public bool TryGetGroundPoint(out Fix2 point)
        {
            point = Fix2.Zero;
            if (Target == null) return false;

            Ray ray = Target.ScreenPointToRay(UnityEngine.Input.mousePosition);
            var ground = new Plane(Vector3.up, Vector3.zero);
            if (!ground.Raycast(ray, out float distance)) return false;

            Vector3 hit = ray.GetPoint(distance);
            point = new Fix2((Fix64)hit.x, (Fix64)hit.z);
            return true;
        }
    }
}
