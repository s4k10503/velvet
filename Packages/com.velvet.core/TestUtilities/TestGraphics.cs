using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Guards tests that need a real graphics device. Under <c>-batchmode -nographics</c> (headless CI or local
    /// batch verification) Unity initializes no graphics device, so <see cref="SystemInfo.graphicsDeviceType"/>
    /// reports <see cref="GraphicsDeviceType.Null"/> and graphics-dependent work cannot run meaningfully:
    /// <c>EditorWindow.Show()</c> fails to create a host view ("No graphic device is available to initialize the
    /// view"), and GPU bakes / <c>ReadPixels</c> produce no valid pixels. Such tests should be cleanly Ignored
    /// rather than Failed so a headless run reports them as skipped.
    /// </summary>
    /// <remarks>
    /// <see cref="SystemInfo.graphicsDeviceType"/> is the canonical check here: it reflects the ACTUAL initialized
    /// device (the same state the native view-creation code fails on), unlike <c>Application.isBatchMode</c>, which
    /// answers "interactive?" not "is there a graphics device?".
    /// </remarks>
    public static class TestGraphics
    {
        /// <summary>
        /// True when running without a graphics device (e.g. <c>-nographics</c> / headless). Private on purpose:
        /// the only safe entry point is <see cref="IgnoreIfHeadless"/>. Exposing the raw boolean would invite
        /// <c>if (IsHeadless) return;</c> inside a test body, which reports the test as PASSED headless instead of
        /// skipped — silently green while exercising nothing.
        /// </summary>
        private static bool IsHeadless => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;

        /// <summary>
        /// Ignores the current test when no graphics device is available, so headless batch runs report it as
        /// skipped instead of failing on a missing GPU. Call as the FIRST statement of a graphics-dependent test
        /// or <c>[SetUp]</c> — before creating any window/panel — which also keeps <c>[TearDown]</c> cleanup
        /// trivially null-safe (the resource is never constructed when the guard fires).
        /// </summary>
        /// <param name="needs">What requires the device, e.g. "an EditorWindow panel"; used in the skip message.</param>
        public static void IgnoreIfHeadless(string needs)
        {
            if (IsHeadless)
                Assert.Ignore($"Requires a graphics device ({needs}); skipped under -nographics (no graphics device available).");
        }
    }
}
