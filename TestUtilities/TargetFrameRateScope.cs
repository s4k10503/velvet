using System;
using System.Collections;
using UnityEngine;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Raises Application.targetFrameRate for the scope's lifetime and restores the previous value on
    /// Dispose. PlayMode fixtures that assert on real elapsed time (camera/particle playback) bump the
    /// frame rate so their realtime waits resolve in fewer, more predictable frames, without each
    /// fixture saving and restoring the value by hand in UnitySetUp/UnityTearDown.
    /// Test-only. Must not be used from production code.
    /// </summary>
    public readonly struct TargetFrameRateScope : IDisposable
    {
        private readonly int _previous;

        public TargetFrameRateScope(int frameRate)
        {
            _previous = Application.targetFrameRate;
            Application.targetFrameRate = frameRate;
        }

        public void Dispose()
        {
            Application.targetFrameRate = _previous;
        }
    }

    /// <summary>
    /// Shared realtime wait for PlayMode fixtures that assert on actual rendered/simulated output
    /// rather than a fixed frame count. Shared across the SceneView/Particles/Portal playback specs
    /// so each does not duplicate this wait verbatim.
    /// Test-only. Must not be used from production code.
    /// </summary>
    public static class PlayModeRealtimeTestHelpers
    {
        /// <summary>Yields until at least <paramref name="seconds"/> of realtime have elapsed.</summary>
        public static IEnumerator WaitRealtime(double seconds)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + seconds;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
            }
        }
    }
}
