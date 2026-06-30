using System.Diagnostics;
using UnityEngine;

namespace Velvet
{
    /// <summary>
    /// Logger for Store. Self-contained within Velvet.
    /// Swap <see cref="Default"/> in tests to suppress log output.
    /// </summary>
    public class StoreLogger
    {
        /// <summary>
        /// Default logger used by Store. Replaceable in tests.
        /// Main-thread only. When running tests in parallel, reset it in each test class's SetUp/TearDown.
        /// </summary>
        public static StoreLogger Default { get; set; } = new StoreLogger();

        /// <summary>Logs an informational message. Compiled out of non-editor / non-development builds.</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public virtual void Log(string message) => UnityEngine.Debug.Log(message);

        /// <summary>Logs a warning. Compiled out of non-editor / non-development builds.</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public virtual void LogWarning(string message) => UnityEngine.Debug.LogWarning(message);

        /// <summary>Logs an error. Always emitted (not stripped from player builds).</summary>
        public virtual void LogError(string message) => UnityEngine.Debug.LogError(message);
    }

    /// <summary>
    /// No-op logger for tests.
    /// </summary>
    public sealed class NullStoreLogger : StoreLogger
    {
        public override void Log(string message) { }
        public override void LogWarning(string message) { }
        public override void LogError(string message) { }
    }
}
