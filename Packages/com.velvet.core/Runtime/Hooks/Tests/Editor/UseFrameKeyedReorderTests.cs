using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins UseFrame's recurring tick across a keyed reorder deterministically, on the EditMode fake clock:
    /// a keyed reorder detaches and re-inserts the ticking host's element, and the tick must keep firing
    /// afterward without needing to be re-armed by hand — and, separately, that firing order between
    /// DIFFERENT components' UseFrame callbacks stays put across such a reorder too, backing up
    /// <see cref="UseFrameDispatcher"/>'s own contract (a transient detach pauses a subscription in place
    /// rather than re-appending it). Complements <see cref="UseFramePerFrameContractTests"/> (cadence) with
    /// the reorder-survival contract that PlayMode's <c>UseFramePlaybackTests</c> exercises on a real,
    /// wall-clock-driven scheduler.
    /// </summary>
    internal sealed class UseFrameKeyedReorderTests
    {
        // Order log for ThreeHostReorderParent below: each host's UseFrame appends its own id, so a test
        // can observe firing order between DIFFERENT components' callbacks (UseFrameFakeClockHost.Calls is
        // a single counter and cannot distinguish which of several hosts fired). Static for the same
        // reason the fake clock is: [Component] methods must be static.
        private static readonly List<string> s_order = new();

        [Component]
        private static VNode HostA()
        {
            Hooks.UseFrame(_ => s_order.Add("A"));
            return V.Div(className: "w-[1px] h-[1px]");
        }

        [Component]
        private static VNode HostB()
        {
            Hooks.UseFrame(_ => s_order.Add("B"));
            return V.Div(className: "w-[1px] h-[1px]");
        }

        [Component]
        private static VNode HostC()
        {
            Hooks.UseFrame(_ => s_order.Add("C"));
            return V.Div(className: "w-[1px] h-[1px]");
        }

        // Three dedicated keyed items, old order [A, B, C]. Swapping to [B, A, C] traces through
        // ChildElementPlacement.ComputeLisAnchors as: domIndices (old index, in new order) = [1, 0, 2] →
        // patience-sort LIS = {A, C} (newElements indices 1 and 2) → B is the ONE index left out of the
        // LIS, so ReorderToNewElementOrder physically removes and reinserts ONLY B's element. A and C's
        // elements are never touched, isolating B's scheduled-item re-registration from theirs.
        [Component]
        private static VNode ThreeHostReorderParent()
        {
            var (swapped, setSwapped) = Hooks.UseState(false);
            var a = V.Div(key: "a", children: new VNode[] { V.Component(HostA) });
            var b = V.Div(key: "b", children: new VNode[] { V.Component(HostB) });
            var c = V.Div(key: "c", children: new VNode[] { V.Component(HostC) });
            return V.Div(children: new VNode[]
            {
                V.Button(name: "reorder", onClick: () => setSwapped.Invoke(true)),
                V.Div(className: "flex-col", children: swapped
                    ? new VNode[] { b, a, c }
                    : new VNode[] { a, b, c }),
            });
        }

        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            UseFrameFakeClockHost.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        // CountingHost (Velvet.TestUtilities.UseFrameFakeClockHost.CountingHost) is wrapped in its OWN dedicated keyed div ("cnt-wrap") rather than sitting
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
            var countingWrap = V.Div(key: "cnt-wrap", children: new VNode[] { V.Component(UseFrameFakeClockHost.CountingHost) });
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
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, UseFrameFakeClockHost.ReadFakeClock);
            _mounted = V.Mount(_host.Root, V.Component(ReorderParent, key: "root"));
            _mounted.FlushEffectsForTest();
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            for (var i = 0; i < 3; i++)
            {
                UseFrameFakeClockHost.Ms += 16;
                EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            }
            Assume.That(UseFrameFakeClockHost.Calls, Is.GreaterThan(0), "Precondition: the tick fired before the reorder");

            // Act — a keyed reorder (driven through a real discrete click, which commits synchronously)
            // detaches and re-inserts the counting host's wrapping element, then the clock advances
            // through several more scheduler updates.
            var callsBeforeReorder = UseFrameFakeClockHost.Calls;
            _host.Root.Q<Button>("reorder").SimulateClick();
            for (var i = 0; i < 4; i++)
            {
                UseFrameFakeClockHost.Ms += 16;
                EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            }

            // Assert — the tick kept firing after the reorder moved its host, with no manual re-arming.
            Assert.That(UseFrameFakeClockHost.Calls, Is.GreaterThan(callsBeforeReorder));
        }

        [Test]
        public void Given_ThreeTickingHosts_When_AKeyedReorderMovesOnlyTheMiddleOne_Then_FiringOrderStaysAtRegistrationOrder()
        {
            // Arrange — mount A, B, C (registration order A, B, C), and confirm a scheduler update fires
            // them in that same order before anything has moved.
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, UseFrameFakeClockHost.ReadFakeClock);
            _mounted = V.Mount(_host.Root, V.Component(ThreeHostReorderParent, key: "root"));
            _mounted.FlushEffectsForTest();
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel); // absorbs the zero-delta arm-time firing
            s_order.Clear();
            UseFrameFakeClockHost.Ms += 16;
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            Assume.That(s_order, Is.EqualTo(new[] { "A", "B", "C" }),
                "Precondition: the scheduler fires newly-mounted hosts in registration order");

            // Act — a keyed reorder (a real discrete click, which commits synchronously) puts B in front of
            // A (see ThreeHostReorderParent's own note on why only B's element is actually detached and
            // reinserted — A and C stay physically untouched), then the clock advances one more tick.
            _host.Root.Q<Button>("reorder").SimulateClick();
            s_order.Clear();
            UseFrameFakeClockHost.Ms += 16;
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert — B's firing position is UNCHANGED despite its element being physically detached and
            // reinserted first in DOM/keyed order: UseFrameDispatcher subscribes per PANEL, not per host
            // element, so B's transient detach only flips its Subscription.Active off and back on — the
            // slot B already held in the dispatcher's ordered list is never vacated, unlike a plain
            // per-element IVisualElementScheduledItem (which UI Toolkit's own scheduler re-appends to the
            // end of its internal list on every re-attach; see UseFrameDispatcher's own remarks).
            Assert.That(s_order, Is.EqualTo(new[] { "A", "B", "C" }));
        }
    }
}
