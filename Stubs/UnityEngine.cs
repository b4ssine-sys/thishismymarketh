// CI-only stub: compiled solely by Stubs/GameStubs.csproj, which defines
// GAME_STUBS. Inside the game these would collide with the real assemblies.
#if GAME_STUBS
namespace UnityEngine
{
    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
    }

    public struct Vector2
    {
        public float x;
        public float y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero { get { return new Vector2(0, 0); } }
    }

    public struct Vector3
    {
        public float x;
        public float y;
        public float z;
        public Vector3(float x, float y) { this.x = x; this.y = y; this.z = 0f; }
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return new Vector3(0, 0, 0); } }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; this.a = 1f; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white { get { return new Color(1, 1, 1, 1); } }
        public static Color black { get { return new Color(0, 0, 0, 1); } }
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public static class Input
    {
        public static bool GetKeyDown(KeyCode key) { return false; }
        public static bool GetKey(KeyCode key) { return false; }
    }

    public enum KeyCode
    {
        None = 0,
        B = 98,
        LeftShift = 304,
        RightShift = 303
    }

    public static class Time
    {
        public static float deltaTime { get { return 0f; } }
    }

    public class MonoBehaviour : Object
    {
        public void StartCoroutine(object routine) { }
    }

    public class Object
    {
        public string name;
        public GameObject gameObject;
        public static void Destroy(Object obj) { }
        public static void DontDestroyOnLoad(Object obj) { }
    }

    public class GameObject : Object
    {
    }
}

#endif
