// Stubs for the test-framework surface the play-mode tests use.
//
// Same purpose and same limits as the UnityEngine stub: it lets the play-mode tests be compiled in
// CI, where Unity cannot run, so a typo in a test is caught here rather than on the machine that
// actually has an editor.

using System;

namespace NUnit.Framework
{
    [AttributeUsage(AttributeTargets.Method)] public class TestAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class SetUpAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class TearDownAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public class TestFixtureAttribute : Attribute { }

    public static class Assert
    {
        public static void IsTrue(bool condition) { }
        public static void IsTrue(bool condition, string message) { }
        public static void IsFalse(bool condition) { }
        public static void IsFalse(bool condition, string message) { }
        public static void IsNull(object value) { }
        public static void IsNull(object value, string message) { }
        public static void IsNotNull(object value) { }
        public static void IsNotNull(object value, string message) { }
        public static void AreEqual(object expected, object actual) { }
        public static void AreEqual(object expected, object actual, string message) { }
        public static void AreNotEqual(object expected, object actual) { }
        public static void AreNotEqual(object expected, object actual, string message) { }
        public static void Greater(int actual, int expected) { }
        public static void Greater(int actual, int expected, string message) { }
        public static void Greater(uint actual, uint expected) { }
        public static void Greater(uint actual, uint expected, string message) { }
        public static void Greater(float actual, float expected) { }
        public static void Greater(float actual, float expected, string message) { }
        public static void GreaterOrEqual(int actual, int expected, string message) { }
        public static void Less(int actual, int expected, string message) { }
        public static void Fail(string message) { }
    }
}

namespace UnityEngine.TestTools
{
    [AttributeUsage(AttributeTargets.Method)] public class UnityTestAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class UnitySetUpAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Method)] public class UnityTearDownAttribute : Attribute { }

    public static class LogAssert
    {
        public static bool ignoreFailingMessages { get; set; }
        public static void Expect(LogType type, string message) { }
        public static void NoUnexpectedReceived() { }
    }
}

namespace UnityEngine
{
    public enum LogType { Error, Assert, Warning, Log, Exception }
}
