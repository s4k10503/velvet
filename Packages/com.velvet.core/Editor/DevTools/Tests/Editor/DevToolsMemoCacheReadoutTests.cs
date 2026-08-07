using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.Editor.DevTools;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the DevTools "Memo Cache Entries" readout to the cache it reports on, and pins the reading it
    /// gives when it cannot reach that cache. The window used to resolve the count through a member name
    /// no type on the path declared, and the null guards along the way turned the miss into a count of
    /// zero — a reading indistinguishable from a cache that is genuinely empty.
    /// </summary>
    [TestFixture]
    internal sealed class DevToolsMemoCacheReadoutTests
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void Given_AMountedTreeHoldingMemoEntries_When_TheInspectorReadsTheCount_Then_ItReportsThoseHeld()
        {
            // Arrange
            var host = new VisualElement();
            using var mounted = V.Mount(host, V.Component(MemoCacheProbe.Render, key: "probe"));
            var held = EntriesHeld(mounted.Root);

            // Act
            var readout = ReadoutFor(mounted.Root);

            // Assert — the arrangement's own count is folded in, because a cache that never filled would
            // otherwise match a readout that was zero for the reason under test.
            Assert.That((readout, held > 0), Is.EqualTo(((int?)held, true)));
        }

        [Test]
        public void Given_AFiberWithNoReconciler_When_TheInspectorReadsTheCount_Then_ItReportsNoReading()
        {
            // Arrange
            var fiber = new ComponentFiber();
            Assume.That(fiber.Reconciler, Is.Null, "Precondition: an unmounted fiber holds no reconciler");

            // Act
            var readout = ReadoutFor(fiber);

            // Assert
            Assert.That(readout, Is.Null,
                "a count the window could not take must not reach the label as a count of zero");
        }

        /// <summary>
        /// The entry count read straight off the cache, or -1 when the private field holding the entries
        /// no longer resolves — which is the same miss the window itself would take, so the assertion
        /// above goes red rather than agreeing with it.
        /// </summary>
        private static int EntriesHeld(ComponentFiber fiber)
        {
            var memoCache = fiber.Reconciler?.Context.FiberMemoCache;
            var entries = typeof(FiberMemoCache).GetField("_cache", Hidden)?.GetValue(memoCache);
            return entries is ICollection held ? held.Count : -1;
        }

        private static int? ReadoutFor(ComponentFiber fiber)
        {
            var window = ScriptableObject.CreateInstance<VelvetDevToolsWindow>();
            try
            {
                typeof(VelvetDevToolsWindow)
                    .GetMethod("UpdateCache", Hidden)
                    .Invoke(window, new object[] { fiber });
                return (int?)typeof(VelvetDevToolsWindow)
                    .GetField("_cachedMemoCacheCount", Hidden)
                    .GetValue(window);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }
    }

    internal static class MemoCacheProbe
    {
        // Two keys rather than one, so the readout is a count rather than a flag.
        [Component]
        public static VNode Render() => V.Fragment(
            V.MemoizedWithKey("first", () => V.Label(text: "first"), 1),
            V.MemoizedWithKey("second", () => V.Label(text: "second"), 1));
    }
}
