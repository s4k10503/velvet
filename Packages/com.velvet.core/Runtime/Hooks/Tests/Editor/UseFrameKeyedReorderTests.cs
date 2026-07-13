using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins UseFrame's recurring tick across a keyed reorder deterministically, on the EditMode fake clock:
    /// a keyed reorder detaches and re-inserts the ticking host's element, and the tick must keep firing
    /// afterward without needing to be re-armed by hand. Complements
    /// <see cref="UseFramePerFrameContractTests"/> (cadence) with the reorder-survival contract that
    /// PlayMode's <c>UseFramePlaybackTests</c> exercises on a real, wall-clock-driven scheduler.
    /// </summary>
    internal sealed class UseFrameKeyedReorderTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        private static int s_calls;
        private static long s_fakeMs;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            s_calls = 0;
            s_fakeMs = 1000;
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        // The per-panel time function reports SECONDS as a double (the ms-facing surface multiplies
        // by 1000); the fake clock is kept in milliseconds and converted here for readability.
        private static double ReadFakeClock() => s_fakeMs / 1000.0;

        [Component]
        private static VNode CountingHost()
        {
            Hooks.UseFrame(_ => s_calls++);
            return V.Div(className: "w-[10px] h-[10px]");
        }

        // CountingHost is wrapped in its OWN dedicated keyed div ("cnt-wrap") rather than sitting
        // directly alongside "sp" in the reordered slot: a component that shares an unkeyed parent
        // with a plain sibling is inline-mounted onto that shared parent (ComponentFiber.MountPoint),
        // which never itself moves when only its children swap places — only a dedicated element
        // that is ONE OF the keyed items being reordered ever has ITS OWN attach/detach cycle. The
        // branch order below (spacer first, then the wrap) is also deliberate: the keyed diff's
        // LIS-based placement leaves whichever element ends up first in the OLD order as the anchor
        // that is never touched, so the wrap must start second and move to first for this test to
        // actually exercise a real detach/re-attach of UseFrame's host.
        [Component]
        private static VNode ReorderParent()
        {
            var (swapped, setSwapped) = Hooks.UseState(false);
            var countingWrap = V.Div(key: "cnt-wrap", children: new VNode[] { V.Component(CountingHost) });
            var spacer = V.Div(key: "sp", className: "w-[1px] h-[1px]");
            return V.Div(children: new VNode[]
            {
                V.Button(name: "reorder", onClick: () => setSwapped.Invoke(true)),
                V.Div(className: "flex-col", children: swapped
                    ? new VNode[] { countingWrap, spacer }
                    : new VNode[] { spacer, countingWrap }),
            });
        }

        [Test]
        public void Given_ATickingUseFrame_When_AKeyedReorderMovesItsHost_Then_TheTickKeepsFiringAfterward()
        {
            // Arrange — mount on the fake clock, run the passive effect that arms the tick, absorb the
            // arm-time firing (its delta is zero on the frozen clock), then drive a few spaced updates
            // so the tick has already fired at least once before the reorder.
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, ReadFakeClock);
            _mounted = V.Mount(_host.Root, V.Component(ReorderParent, key: "root"));
            _mounted.FlushEffectsForTest();
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            for (var i = 0; i < 3; i++)
            {
                s_fakeMs += 16;
                EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            }
            Assume.That(s_calls, Is.GreaterThan(0), "Precondition: the tick fired before the reorder");

            // Act — a keyed reorder (driven through a real discrete click, which commits synchronously)
            // detaches and re-inserts the counting host's wrapping element, then the clock advances
            // through several more scheduler updates.
            var callsBeforeReorder = s_calls;
            _host.Root.Q<Button>("reorder").SimulateClick();
            for (var i = 0; i < 4; i++)
            {
                s_fakeMs += 16;
                EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            }

            // Assert — the tick kept firing after the reorder moved its host, with no manual re-arming.
            Assert.That(s_calls, Is.GreaterThan(callsBeforeReorder));
        }
    }
}
