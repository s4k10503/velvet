using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <see cref="UseFrameDispatcher"/>'s per-panel scoping, true-unmount cleanup, and per-subscriber
    /// timing baselines on the EditMode fake clock — properties its own class doc promises ("One
    /// dispatcher per panel") but that <see cref="UseFrameKeyedReorderTests"/> (reorder survival) and
    /// <see cref="UseFramePriorityTests"/> (ordering) don't themselves exercise: two panels never see each
    /// other's subscribers, a host that TRULY unmounts (not just reorders) stops firing while its
    /// still-mounted siblings keep their relative order undisturbed, a panel whose subscriber count drops
    /// to zero still fires a freshly mounted host later, and a late-joining subscriber's first real delta
    /// reflects only its OWN elapsed time — never a stall an earlier, unrelated subscriber on the same
    /// panel had already accumulated before the late joiner even existed.
    /// </summary>
    internal sealed class UseFrameDispatcherLifecycleTests
    {
        private static readonly List<string> s_order = new();
        private static bool s_mountSecond = true;

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

        // B renders only while s_mountSecond is true — the CLAUDE.md-documented "cond ? node : null" idiom
        // for a real unmount, distinct from a keyed reorder (which never removes a VNode from the tree).
        [Component]
        private static VNode TwoHostParent()
        {
            var (mountSecond, setMountSecond) = Hooks.UseState(s_mountSecond);
            s_setMountSecond = setMountSecond;
            return V.Div(children: new VNode[]
            {
                V.Component(HostA, key: "a"),
                mountSecond ? V.Component(HostB, key: "b") : null,
            });
        }

        private static StateUpdater<bool> s_setMountSecond;

        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            UseFrameFakeClockHost.Reset();
            s_mountSecond = true;
            s_order.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        private void MountAndArm(VNode root, HeadlessEditorPanelHost host)
        {
            EditorPanelTestHelpers.SetPanelTimeFunction(host.Panel, UseFrameFakeClockHost.ReadFakeClock);
            _mounted = V.Mount(host.Root, root);
            _mounted.FlushEffectsForTest();
            EditorPanelTestHelpers.DriveSchedulerOnce(host.Panel); // absorbs the zero-delta arm-time firing
        }

        [Test]
        public void Given_ATrulyUnmountedHost_When_Ticked_Then_ItStopsFiringAndItsSiblingIsUndisturbed()
        {
            // Arrange
            MountAndArm(V.Component(TwoHostParent, key: "root"), _host);
            s_order.Clear();
            UseFrameFakeClockHost.Ms += 16;
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            Assume.That(s_order, Is.EqualTo(new[] { "A", "B" }), "Precondition: both hosts fire before B unmounts");

            // Act — B leaves the tree entirely (VNode becomes null), not just a reorder.
            s_mountSecond = false;
            s_setMountSecond.Invoke(false);
            _mounted.FlushStateForTest();
            s_order.Clear();
            UseFrameFakeClockHost.Ms += 16;
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert — A keeps firing on its own, undisturbed; B never fires again.
            Assert.That(s_order, Is.EqualTo(new[] { "A" }));
        }

        [Test]
        public void Given_TwoSeparatePanels_When_OneTicks_Then_TheOtherPanelsSubscriberDoesNotFire()
        {
            // Arrange — two independent hosts/panels, each with its own fake clock, each mounting ONE
            // ticking component (HostA on panel 1, HostB on panel 2). UseFrameDispatcher's per-panel
            // ConditionalWeakTable means these must never share firing.
            using var hostPanel2 = new HeadlessEditorPanelHost();
            MountAndArm(V.Component(HostA, key: "root-1"), _host);
            var mounted1 = _mounted;
            EditorPanelTestHelpers.SetPanelTimeFunction(hostPanel2.Panel, UseFrameFakeClockHost.ReadFakeClock);
            var mounted2 = V.Mount(hostPanel2.Root, V.Component(HostB, key: "root-2"));
            mounted2.FlushEffectsForTest();
            EditorPanelTestHelpers.DriveSchedulerOnce(hostPanel2.Panel);
            try
            {
                s_order.Clear();

                // Act — only panel 1's scheduler advances.
                UseFrameFakeClockHost.Ms += 16;
                EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

                // Assert — panel 2's host (subscribed to a DIFFERENT dispatcher instance) never fired.
                Assert.That(s_order, Is.EqualTo(new[] { "A" }));
            }
            finally
            {
                mounted2.Dispose();
                mounted1.Dispose();
                _mounted = null;
            }
        }

        [Test]
        public void Given_APanelDrainedToZeroSubscribers_When_ANewHostMountsLater_Then_ItStartsFiring()
        {
            // Arrange — mount and then fully unmount the only host on this panel, draining
            // UseFrameDispatcher's subscriber list to zero (which pauses its own scheduled tick).
            MountAndArm(V.Component(HostA, key: "root"), _host);
            s_order.Clear();
            _mounted.Dispose();
            _mounted = null;

            // Act — a fresh host mounts on the SAME panel afterward.
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, UseFrameFakeClockHost.ReadFakeClock);
            _mounted = V.Mount(_host.Root, V.Component(HostB, key: "root2"));
            _mounted.FlushEffectsForTest();
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel); // absorbs the zero-delta arm-time firing
            s_order.Clear();
            UseFrameFakeClockHost.Ms += 16;
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert — the dispatcher re-armed its tick rather than staying paused forever.
            Assert.That(s_order, Is.EqualTo(new[] { "B" }));
        }

        private static readonly List<float> s_observedDts = new();

        [Component]
        private static VNode DtRecordingHost()
        {
            Hooks.UseFrame(dt => s_observedDts.Add(dt));
            return V.Div(className: "w-[1px] h-[1px]");
        }

        [Test]
        public void Given_ALateJoiningSubscriber_When_AnEarlierOneOnTheSamePanelHasBeenStalled_Then_ItsFirstDeltaIsSmall()
        {
            // Arrange — HostA mounts and ticks once (baseline established), then the clock jumps far
            // ahead WITHOUT driving the scheduler in between — simulating a panel that stalled (an
            // unfocused Editor window, a hitch) before a second, unrelated component mounts onto the
            // SAME panel and joins the SAME dispatcher HostA is already subscribed to.
            MountAndArm(V.Component(HostA, key: "root-1"), _host);
            UseFrameFakeClockHost.Ms += 500;
            var secondRoot = new VisualElement();
            _host.Root.Add(secondRoot);
            var second = V.Mount(secondRoot, V.Component(DtRecordingHost, key: "root-2"));
            second.FlushEffectsForTest();
            try
            {
                s_observedDts.Clear();

                // Act — the FIRST drive after DtRecordingHost joins only baselines its own clock (see
                // UseFrameDispatcher.Tick's per-Subscription LastTimeMs) and records nothing for it; a
                // second, small clock step is what exercises its first REAL delta. Both drives feed the
                // SAME s_observedDts list — deliberately not split across a separate precondition check —
                // so a regression that skips the baseline pass (and fires immediately with a borrowed,
                // inflated delta) shows up as a hard Assert failure below, not a merely-inconclusive one.
                EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
                UseFrameFakeClockHost.Ms += 16;
                EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

                // Assert — exactly one delta was EVER recorded across both drives (the baseline pass
                // contributes none), and it reflects only the 16ms step since DtRecordingHost was
                // baselined — never the 500ms HostA had already been stalled for before DtRecordingHost
                // even existed. A dt shared verbatim across every same-panel subscriber would instead
                // record a SECOND entry here too, from the first drive, clamped to Time.maximumDeltaTime.
                Assert.That((s_observedDts.Count, s_observedDts.Count == 1 && s_observedDts[0] < 0.1f),
                    Is.EqualTo((1, true)));
            }
            finally
            {
                second.Dispose();
            }
        }
    }
}
