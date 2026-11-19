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
        /// <summary>
        /// Yields for a fixed number of frames.
        /// </summary>
        /// <remarks>
        /// For a fixture that needs the panel drawn rather than time elapsed. A realtime wait spins as
        /// many frames as it can, and where a frame's rasterisation is queued rather than executed it
        /// can spin hundreds — each queueing a full panel render that the next readback pays for.
        /// </remarks>
        public static IEnumerator WaitFrames(int frames)
        {
            for (var frame = 0; frame < frames; frame++)
            {
                yield return null;
            }
        }

        /// <summary>
        /// Yields for a fixed number of frames, draining the render queue on each one.
        /// </summary>
        /// <remarks>
        /// Eight frames of one story cost 96.8 s queued and 13.4 s drained on a runner that
        /// rasterises on the CPU, for the same eight frames and a byte-identical capture. Paying per
        /// frame is not merely a redistribution of when the cost lands.
        /// </remarks>
        public static IEnumerator WaitFramesDraining(int frames, RenderTexture texture)
        {
            for (var frame = 0; frame < frames; frame++)
            {
                yield return null;
                RenderTexturePixelReader.ReadPixels(texture, new RectInt(0, 0, 1, 1));
            }
        }

        /// <summary>
        /// Yields for the given realtime, draining the render queue on every frame.
        /// </summary>
        /// <remarks>
        /// For a fixture that needs real time to pass AND draws every frame. Where rasterisation is
        /// queued rather than executed, a plain realtime wait spins as many frames as the CPU allows
        /// and each one queues a full render that the next readback pays for; draining each frame
        /// makes the frame cost what it costs, so the number of frames falls to what the wall clock
        /// affords and the total is bounded by the wait rather than by the spin rate.
        /// </remarks>
        public static IEnumerator WaitRealtimeDraining(double seconds, RenderTexture texture)
        {
            var deadline = Time.realtimeSinceStartupAsDouble + seconds;
            while (Time.realtimeSinceStartupAsDouble < deadline)
            {
                yield return null;
                RenderTexturePixelReader.ReadPixels(texture, new RectInt(0, 0, 1, 1));
            }
        }

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
