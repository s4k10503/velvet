using System;

namespace Velvet
{
    internal static class FiberLogger
    {
        internal static void LogWarning(string tag, string msg) =>
            UnityEngine.Debug.LogWarning($"[{tag}] {msg}");

        internal static void LogError(string tag, string msg) =>
            UnityEngine.Debug.LogError($"[{tag}] {msg}");

        internal static void LogException(string tag, Exception ex)
        {
            UnityEngine.Debug.LogError($"[{tag}] An exception occurred. See the next line for details.");
            UnityEngine.Debug.LogException(ex);
        }
    }
}
