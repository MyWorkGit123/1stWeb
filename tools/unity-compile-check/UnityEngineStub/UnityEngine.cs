// A stub of the UnityEngine API surface the Brinehold client uses.
//
// Unity cannot be installed in the environment this project is developed in, so the client's
// MonoBehaviours were previously written but never compiled. Compiling them against this stub
// catches the large majority of what a first editor session would otherwise catch: typos, missing
// usings, wrong member names on our own types, signature mismatches, and plain C# errors.
//
// What it does NOT prove: that a signature here matches Unity's exactly, that a component wiring
// order is right, or that anything behaves correctly at runtime. Treat a clean compile here as
// "the code is structurally sound", not as "the client works".
//
// Every member below exists because the client uses it. If the client stops using something,
// delete it from here too — a stub that drifts wider than its consumer stops being a check.

using System;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0, 0);
        public float sqrMagnitude => x * x + y * y;
        public float magnitude => (float)System.Math.Sqrt(sqrMagnitude);
        public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static implicit operator Vector2(Vector3 v) => new Vector2(v.x, v.y);
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator *(Vector3 a, float s) => new Vector3(a.x * s, a.y * s, a.z * s);
        public static Vector3 operator *(float s, Vector3 a) => a * s;
    }

    public struct Quaternion
    {
        public static Quaternion identity => default;
        public static Quaternion Euler(float x, float y, float z) => default;
        public static Vector3 operator *(Quaternion q, Vector3 v) => v;
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1, 1, 1);
        public static Color black => new Color(0, 0, 0);
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color32(Color c) => new Color32(0, 0, 0, 0);
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
        public float xMax => x + width;
        public float yMax => y + height;
        public Vector2 position => new Vector2(x, y);
        public bool Contains(Vector2 point) => false;
    }

    public struct Ray
    {
        public Vector3 GetPoint(float distance) => Vector3.zero;
    }

    public struct Plane
    {
        public Plane(Vector3 normal, Vector3 point) { }
        public Plane(Vector3 normal, float distance) { }
        public bool Raycast(Ray ray, out float enter) { enter = 0f; return false; }
    }

    public static class Mathf
    {
        public const float Rad2Deg = 57.29578f;
        public const float Deg2Rad = 0.0174532924f;
        public static float Abs(float value) => System.Math.Abs(value);
        public static float Clamp01(float value) => value;
        public static float Min(float a, float b) => System.Math.Min(a, b);
        public static float Max(float a, float b) => System.Math.Max(a, b);
        public static float Lerp(float a, float b, float t) => a;
        public static float LerpAngle(float a, float b, float t) => a;
    }

    public class Object
    {
        public string name { get; set; } = string.Empty;
        public static void Destroy(Object target) { }
        public static void DestroyImmediate(Object target) { }
        public static T Instantiate<T>(T original) where T : Object => original;
        public static T Instantiate<T>(T original, Transform parent) where T : Object => original;
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b);
        public static bool operator !=(Object a, Object b) => !ReferenceEquals(a, b);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => base.GetHashCode();
    }

    public class Transform : Component
    {
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public Vector3 localScale { get; set; }
        public Quaternion rotation { get; set; }
        public Quaternion localRotation { get; set; }
        public void SetParent(Transform parent) { }
        public void SetParent(Transform parent, bool worldPositionStays) { }
    }

    public class Component : Object
    {
        public Transform transform => null!;
        public GameObject gameObject => null!;
        public string tag { get; set; } = string.Empty;
        public T GetComponent<T>() where T : Component => null!;
        public T AddComponent<T>() where T : Component => null!;
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; }
    }

    public class MonoBehaviour : Behaviour { }

    public class GameObject : Object
    {
        public GameObject() { }
        public GameObject(string name) { this.name = name; }
        public Transform transform => null!;
        public bool activeSelf => false;
        public void SetActive(bool value) { }
        public T GetComponent<T>() where T : Component => null!;
        public T AddComponent<T>() where T : Component => null!;
        public static GameObject CreatePrimitive(PrimitiveType type) => null!;
    }

    public enum PrimitiveType { Sphere, Capsule, Cylinder, Cube, Plane, Quad }

    public class Camera : Behaviour
    {
        public float farClipPlane { get; set; }
        public Color backgroundColor { get; set; }
        public CameraClearFlags clearFlags { get; set; }
        public Ray ScreenPointToRay(Vector3 position) => default;
        public static Camera main => null!;
    }

    public enum CameraClearFlags { Skybox = 1, Color = 2, SolidColor = 2, Depth = 3, Nothing = 4 }

    public class Light : Behaviour
    {
        public LightType type { get; set; }
        public float intensity { get; set; }
        public Color color { get; set; }
    }

    public enum LightType { Spot, Directional, Point, Area }

    public static class RenderSettings
    {
        public static Color ambientLight { get; set; }
    }

    public class Renderer : Component
    {
        public Material material { get; set; } = null!;
        public Material sharedMaterial { get; set; } = null!;
        public void GetPropertyBlock(MaterialPropertyBlock properties) { }
        public void SetPropertyBlock(MaterialPropertyBlock properties) { }
    }

    public class MeshRenderer : Renderer { }

    public class MeshFilter : Component
    {
        public Mesh mesh { get; set; } = null!;
        public Mesh sharedMesh { get; set; } = null!;
    }

    public class Collider : Component { }

    public class MeshCollider : Collider
    {
        public Mesh sharedMesh { get; set; } = null!;
    }

    public class Mesh : Object
    {
        public Rendering.IndexFormat indexFormat { get; set; }
        public void SetVertices(System.Collections.Generic.List<Vector3> vertices) { }
        public void SetTriangles(System.Collections.Generic.List<int> triangles, int submesh) { }
        public void SetUVs(int channel, System.Collections.Generic.List<Vector2> uvs) { }
        public void RecalculateNormals() { }
        public void RecalculateBounds() { }
    }

    public class Material : Object
    {
        public Material(Shader shader) { }
        public bool HasProperty(string name) => false;
        public void SetColor(string name, Color value) { }
        public void SetColor(int nameId, Color value) { }
        public void SetFloat(string name, float value) { }
        public void SetTexture(int nameId, Texture value) { }
        public void SetTexture(string name, Texture value) { }
        public int renderQueue { get; set; }
    }

    public class MaterialPropertyBlock
    {
        public void SetColor(int nameId, Color value) { }
    }

    public class Shader : Object
    {
        public static Shader Find(string name) => null!;
        public static int PropertyToID(string name) => 0;
    }

    public class Texture : Object { }

    public class Texture2D : Texture
    {
        public Texture2D(int width, int height) { }
        public Texture2D(int width, int height, TextureFormat format, bool mipChain) { }
        public FilterMode filterMode { get; set; }
        public TextureWrapMode wrapMode { get; set; }
        public void SetPixel(int x, int y, Color color) { }
        public void SetPixels32(Color32[] colors) { }
        public void Apply() { }
        public void Apply(bool updateMipmaps) { }
        public static Texture2D whiteTexture => null!;
    }

    public enum TextureFormat { RGBA32 = 4, ARGB32 = 5 }
    public enum FilterMode { Point, Bilinear, Trilinear }
    public enum TextureWrapMode { Repeat, Clamp }
    public enum ScaleMode { StretchToFill, ScaleAndCrop, ScaleToFit }

    public static class Time
    {
        public static float deltaTime => 0f;
        public static float time => 0f;
        public static float unscaledDeltaTime => 0f;
    }

    public static class Screen
    {
        public static int width => 1920;
        public static int height => 1080;
    }

    public static class Application
    {
        public static bool isFocused => true;
        public static bool isPlaying => true;
    }

    public static class Input
    {
        public static Vector3 mousePosition => Vector3.zero;
        public static Vector2 mouseScrollDelta => Vector2.zero;
        public static bool GetKey(KeyCode key) => false;
        public static bool GetKeyDown(KeyCode key) => false;
        public static bool GetKeyUp(KeyCode key) => false;
        public static bool GetMouseButton(int button) => false;
        public static bool GetMouseButtonDown(int button) => false;
        public static bool GetMouseButtonUp(int button) => false;
    }

    public enum KeyCode
    {
        None = 0, Backspace = 8, Tab = 9, Return = 13, Escape = 27, Space = 32,
        Alpha0 = 48, Alpha1, Alpha2, Alpha3, Alpha4, Alpha5, Alpha6, Alpha7, Alpha8, Alpha9,
        A = 97, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, V, W, X, Y, Z,
        UpArrow = 273, DownArrow = 274, RightArrow = 275, LeftArrow = 276,
        F1 = 282, F2, F3, F4,
        RightShift = 303, LeftShift = 304, RightControl = 305, LeftControl = 306
    }

    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }

    public class GUIStyle
    {
        public GUIStyle() { }
        public GUIStyle(GUIStyle other) { }
        public int fontSize { get; set; }
        public FontStyle fontStyle { get; set; }
        public GUIStyleState normal { get; set; } = new GUIStyleState();
    }

    public class GUIStyleState
    {
        public Texture2D background { get; set; } = null!;
        public Color textColor { get; set; }
    }

    public class GUISkin
    {
        public GUIStyle box => new GUIStyle();
        public GUIStyle label => new GUIStyle();
    }

    public static class GUI
    {
        public static GUISkin skin => new GUISkin();
        public static Color color { get; set; }
        public static void DrawTexture(Rect position, Texture image) { }
        public static void DrawTexture(Rect position, Texture image, ScaleMode scaleMode) { }
        public static void Label(Rect position, string text, GUIStyle style) { }
    }

    public class GUILayoutOption { }

    public static class GUILayout
    {
        public static void BeginArea(Rect screenRect) { }
        public static void BeginArea(Rect screenRect, GUIStyle style) { }
        public static void EndArea() { }
        public static void BeginHorizontal(params GUILayoutOption[] options) { }
        public static void EndHorizontal() { }
        public static void BeginVertical(params GUILayoutOption[] options) { }
        public static void EndVertical() { }
        public static void Label(string text, params GUILayoutOption[] options) { }
        public static void Label(string text, GUIStyle style, params GUILayoutOption[] options) { }
        public static bool Button(string text, params GUILayoutOption[] options) => false;
        public static GUILayoutOption Width(float value) => new GUILayoutOption();
        public static GUILayoutOption Height(float value) => new GUILayoutOption();
    }

    public class Event
    {
        public static Event current => null!;
        public EventType type => EventType.Ignore;
        public Vector2 mousePosition => Vector2.zero;
        public void Use() { }
    }

    public enum EventType { MouseDown = 0, MouseUp = 1, MouseMove = 2, MouseDrag = 3, KeyDown = 4, KeyUp = 5, Repaint = 7, Layout = 8, Ignore = 11 }

    [AttributeUsage(AttributeTargets.Field)]
    public class HeaderAttribute : Attribute { public HeaderAttribute(string header) { } }

    [AttributeUsage(AttributeTargets.Field)]
    public class TooltipAttribute : Attribute { public TooltipAttribute(string tooltip) { } }

    [AttributeUsage(AttributeTargets.Field)]
    public class RangeAttribute : Attribute { public RangeAttribute(float min, float max) { } }

    [AttributeUsage(AttributeTargets.Field)]
    public class SerializeFieldAttribute : Attribute { }
}

namespace UnityEngine.Rendering
{
    public enum IndexFormat { UInt16 = 0, UInt32 = 1 }
}
