using System.Diagnostics;
using UnityEngine;

namespace Velvet
{
    public class StoreLogger
    {
        /// <summary>
        /// Default logger captured by each store at construction.
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

        public virtual void LogError(string message) => UnityEngine.Debug.LogError(message);
    }

    public sealed class NullStoreLogger : StoreLogger
    {
        public override void Log(string message) { }
        public override void LogWarning(string message) { }
        public override void LogError(string message) { }
    }
}
