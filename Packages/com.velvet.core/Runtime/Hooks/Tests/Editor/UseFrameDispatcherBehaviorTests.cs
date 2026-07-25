using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <see cref="UseFrameDispatcher"/>'s full behavioral contract on the EditMode fake clock: per-panel
    /// subscriber scoping and true-unmount cleanup, per-subscriber timing baselines, the reentrancy guard around
    /// a synchronous unsubscribe triggered mid-tick, survival of a keyed reorder, the per-update cadence
    /// contract, and the <c>priority</c> ordering contract.
    /// <list type="bullet">
    /// <item>Two panels never see each other's subscribers; a host that TRULY unmounts (not just reorders) stops
    /// firing while its still-mounted siblings keep firing undisturbed; a panel whose subscriber count drops to
    /// zero still fires a freshly mounted host later; and a late-joining subscriber's first real delta reflects
    /// only its OWN elapsed time, never a stall an earlier, unrelated subscriber on the same panel had already
    /// accumulated.</item>
    /// <item>An error boundary's fallback swap, triggered synchronously from inside one subscriber's callback,
    /// can dispose a LATER-sorted sibling before <c>Tick</c>'s own snapshot foreach ever reaches that sibling's
    /// entry — <c>Unsubscribe</c> must clear <c>Active</c> immediately so the stale snapshot reference is skipped
    /// rather than firing once more on an already-disposed component.</item>
    /// <item>A keyed reorder detaches and re-inserts a ticking host's element, and the tick must keep firing
    /// afterward without needing to be re-armed by hand; firing order between DIFFERENT components' callbacks
    /// stays put across such a reorder too, because UseFrameDispatcher subscribes per PANEL, not per host
    /// element.</item>
    /// <item>The callback follows the panel's scheduler update — once per update with a positive delta — rather
    /// than a fixed minimum wall-clock interval.</item>
    /// <item>Lower <c>priority</c> runs earlier within a panel regardless of mount order, equal priorities fall
    /// back to subscription (mount) order, and a priority change on a live subscription applies on the very next
    /// tick without a remount.</item>
    /// </list>
    /// </summary>
    internal sealed class UseFrameDispatcherBehaviorTests
    {
        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            UseFrameFakeClockHost.Reset();
            s_mountSecond = true;
            s_lifecycleOrder.Clear();
            s_shouldThrow = false;
            s_log.Clear();
            s_priorityA = 0;
            s_priorityB = 0;
            s_priorityC = 0;
            s_priorityOrder.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

        private static readonly List<string> s_lifecycleOrder = new();
        private static bool s_mountSecond = true;

        [Component]
        private static VNode LifecycleHostA()
        {
            Hooks.UseFrame(_ => s_lifecycleOrder.Add("A"));
            return V.Div(className: "w-[1px] h-[1px]");
        }

        [Component]
        private static VNode LifecycleHostB()
        {
            Hooks.UseFrame(_ => s_lifecycleOrder.Add("B"));
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
                V.Component(LifecycleHostA, key: "a"),
                mountSecond ? V.Component(LifecycleHostB, key: "b") : null,
            });
        }

        private static StateUpdater<bool> s_setMountSecond;

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
            s_lifecycleOrder.Clear();
            UseFrameFakeClockHost.Ms += 16;
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            Assume.That(s_lifecycleOrder, Is.EqualTo(new[] { "A", "B" }), "Precondition: both hosts fire before B unmounts");

            // Act — B leaves the tree entirely (VNode becomes null), not just a reorder.
            s_mountSecond = false;
            s_setMountSecond.Invoke(false);
            _mounted.FlushStateForTest();
            s_lifecycleOrder.Clear();
            UseFrameFakeClockHost.Ms += 16;
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert — A keeps firing on its own, undisturbed; B never fires again.
            Assert.That(s_lifecycleOrder, Is.EqualTo(new[] { "A" }));
        }

        [Test]
        public void Given_TwoSeparatePanels_When_OneTicks_Then_TheOtherPanelsSubscriberDoesNotFire()
        {
            // Arrange — two independent hosts/panels, each with its own fake clock, each mounting ONE
            // ticking component (LifecycleHostA on panel 1, LifecycleHostB on panel 2). UseFrameDispatcher's
            // per-panel ConditionalWeakTable means these must never share firing.
            using var hostPanel2 = new HeadlessEditorPanelHost();
            MountAndArm(V.Component(LifecycleHostA, key: "root-1"), _host);
            var mounted1 = _mounted;
            EditorPanelTestHelpers.SetPanelTimeFunction(hostPanel2.Panel, UseFrameFakeClockHost.ReadFakeClock);
            var mounted2 = V.Mount(hostPanel2.Root, V.Component(LifecycleHostB, key: "root-2"));
            mounted2.FlushEffectsForTest();
            EditorPanelTestHelpers.DriveSchedulerOnce(hostPanel2.Panel);
            try
            {
                s_lifecycleOrder.Clear();

                // Act — only panel 1's scheduler advances.
                UseFrameFakeClockHost.Ms += 16;
                EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

                // Assert — panel 2's host (subscribed to a DIFFERENT dispatcher instance) never fired.
                Assert.That(s_lifecycleOrder, Is.EqualTo(new[] { "A" }));
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
            MountAndArm(V.Component(LifecycleHostA, key: "root"), _host);
            s_lifecycleOrder.Clear();
            _mounted.Dispose();
            _mounted = null;

            // Act — a fresh host mounts on the SAME panel afterward.
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, UseFrameFakeClockHost.ReadFakeClock);
            _mounted = V.Mount(_host.Root, V.Component(LifecycleHostB, key: "root2"));
            _mounted.FlushEffectsForTest();
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel); // absorbs the zero-delta arm-time firing
            s_lifecycleOrder.Clear();
            UseFrameFakeClockHost.Ms += 16;
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert — the dispatcher re-armed its tick rather than staying paused forever.
            Assert.That(s_lifecycleOrder, Is.EqualTo(new[] { "B" }));
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
            // Arrange — LifecycleHostA mounts and ticks once (baseline established), then the clock jumps far
            // ahead WITHOUT driving the scheduler in between — simulating a panel that stalled (an
            // unfocused Editor window, a hitch) before a second, unrelated component mounts onto the
            // SAME panel and joins the SAME dispatcher LifecycleHostA is already subscribed to.
            MountAndArm(V.Component(LifecycleHostA, key: "root-1"), _host);
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
                // baselined — never the 500ms LifecycleHostA had already been stalled for before
                // DtRecordingHost even existed. A dt shared verbatim across every same-panel subscriber
                // would instead record a SECOND entry here too, from the first drive, clamped to
                // Time.maximumDeltaTime.
                Assert.That((s_observedDts.Count, s_observedDts.Count == 1 && s_observedDts[0] < 0.1f),
                    Is.EqualTo((1, true)));
            }
            finally
            {
                second.Dispose();
            }
        }

        // An order log, not a bare counter: a log rules out the confound "Victim simply fired before
        // Thrower this tick" — which would also produce a nonzero Victim count but would not demonstrate
        // a callback firing AFTER its owning component was synchronously unmounted.
        private static readonly List<string> s_log = new();
        private static bool s_shouldThrow;

        [Component]
        private static VNode ThrowerHost()
        {
            Hooks.UseFrame(_ =>
            {
                s_log.Add("Thrower-entered");
                if (s_shouldThrow)
                {
                    throw new System.InvalidOperationException("reentrancy probe");
                }
            });
            return V.Div(className: "w-[1px] h-[1px]");
        }

        [Component]
        private static VNode VictimHost()
        {
            Hooks.UseFrame(_ => s_log.Add("Victim-fired"));
            return V.Div(className: "w-[1px] h-[1px]");
        }

        // Mounted AFTER ThrowerHost (left-to-right Fragment children), so its UseFrame subscribes with a
        // LATER sequence number and sorts after the thrower's in the SAME tick — the ordering this test
        // needs to reach the thrower's boundary-triggering exception before Victim's own snapshot entry.
        [Component(IsErrorBoundary = true)]
        private static VNode Boundary()
        {
            Hooks.UseFallback(_ =>
            {
                s_log.Add("Fallback-shown");
                return V.Div(name: "fallback");
            });
            return V.Fragment(new VNode[]
            {
                V.Component(ThrowerHost, key: "thrower"),
                V.Component(VictimHost, key: "victim"),
            });
        }

        [Test]
        public void Given_SiblingThrowsMidTick_When_TheBoundarySwapUnmountsTheOtherSiblingSynchronously_Then_ItNeverFiresAfterward()
        {
            // Arrange — one ordinary tick first, proving BOTH siblings are genuinely subscribed and
            // ticking before the throw is armed (ruling out "Victim was never live to begin with" as an
            // alternate explanation for it never appearing in the log later).
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, UseFrameFakeClockHost.ReadFakeClock);
            _mounted = V.Mount(_host.Root, V.Component(Boundary, key: "root"));
            _mounted.FlushEffectsForTest();
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel); // absorbs the zero-delta arm-time firing
            s_log.Clear();
            UseFrameFakeClockHost.Ms += 16;
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            Assume.That(s_log, Is.EqualTo(new[] { "Thrower-entered", "Victim-fired" }),
                "Precondition: both siblings tick normally before the throw is armed");

            // Act — one more scheduler update, now with the throw armed: Thrower's callback runs and
            // throws, routing synchronously to the boundary's fallback swap (which disposes BOTH Thrower
            // and Victim), all before the dispatcher's own snapshot foreach for this same tick ever
            // reaches Victim's entry.
            s_log.Clear();
            s_shouldThrow = true;
            UseFrameFakeClockHost.Ms += 16;
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert — Victim never fires, in this tick or (since it is now fully unsubscribed) any later
            // one: the log holds only the thrower's entry and the fallback swap, never "Victim-fired".
            Assert.That(s_log, Is.EqualTo(new[] { "Thrower-entered", "Fallback-shown" }));
        }

        // Order log for ThreeHostReorderParent below: each host's UseFrame appends its own id, so a test
        // can observe firing order between DIFFERENT components' callbacks (UseFrameFakeClockHost.Calls is
        // a single counter and cannot distinguish which of several hosts fired). Static for the same
        // reason the fake clock is: [Component] methods must be static.
        private static readonly List<string> s_reorderOrder = new();

        [Component]
        private static VNode ReorderHostA()
        {
            Hooks.UseFrame(_ => s_reorderOrder.Add("A"));
            return V.Div(className: "w-[1px] h-[1px]");
        }

        [Component]
        private static VNode ReorderHostB()
        {
            Hooks.UseFrame(_ => s_reorderOrder.Add("B"));
            return V.Div(className: "w-[1px] h-[1px]");
        }

        [Component]
        private static VNode ReorderHostC()
        {
            Hooks.UseFrame(_ => s_reorderOrder.Add("C"));
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
            var a = V.Div(key: "a", children: new VNode[] { V.Component(ReorderHostA) });
            var b = V.Div(key: "b", children: new VNode[] { V.Component(ReorderHostB) });
            var c = V.Div(key: "c", children: new VNode[] { V.Component(ReorderHostC) });
            return V.Div(children: new VNode[]
            {
                V.Button(name: "reorder", onClick: () => setSwapped.Invoke(true)),
                V.Div(className: "flex-col", children: swapped
                    ? new VNode[] { b, a, c }
                    : new VNode[] { a, b, c }),
            });
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
            s_reorderOrder.Clear();
            UseFrameFakeClockHost.Ms += 16;
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            Assume.That(s_reorderOrder, Is.EqualTo(new[] { "A", "B", "C" }),
                "Precondition: the scheduler fires newly-mounted hosts in registration order");

            // Act — a keyed reorder (a real discrete click, which commits synchronously) puts B in front of
            // A (see ThreeHostReorderParent's own note on why only B's element is actually detached and
            // reinserted — A and C stay physically untouched), then the clock advances one more tick.
            _host.Root.Q<Button>("reorder").SimulateClick();
            s_reorderOrder.Clear();
            UseFrameFakeClockHost.Ms += 16;
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Assert — B's firing position is UNCHANGED despite its element being physically detached and
            // reinserted first in DOM/keyed order: UseFrameDispatcher subscribes per PANEL, not per host
            // element, so B's transient detach only flips its Subscription.Active off and back on — the
            // slot B already held in the dispatcher's ordered list is never vacated, unlike a plain
            // per-element IVisualElementScheduledItem (which UI Toolkit's own scheduler re-appends to the
            // end of its internal list on every re-attach; see UseFrameDispatcher's own remarks).
            Assert.That(s_reorderOrder, Is.EqualTo(new[] { "A", "B", "C" }));
        }

        [Test]
        public void Given_AMountedUseFrame_When_TheSchedulerUpdatesEveryFewMilliseconds_Then_EachSpacedUpdateTicks()
        {
            // Arrange — mount on the fake clock, run the passive effect that arms the tick, and
            // absorb the arm-time firing (its delta is zero on the frozen clock, so it never counts).
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, UseFrameFakeClockHost.ReadFakeClock);
            _mounted = V.Mount(_host.Root, V.Component(UseFrameFakeClockHost.CountingHost, key: "root"));
            _mounted.FlushEffectsForTest();
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);

            // Act — four updates two fake-milliseconds apart, all inside a single 16 ms window.
            for (var i = 0; i < 4; i++)
            {
                UseFrameFakeClockHost.Ms += 2;
                EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            }

            // Assert — per-update ticking: the spaced updates each invoked the callback; a 16 ms
            // minimum interval would have allowed none of them (only 8 ms elapsed in total).
            Assert.That(UseFrameFakeClockHost.Calls, Is.GreaterThanOrEqualTo(3));
        }

        // Order log each host's UseFrame appends its own id to — UseFrameFakeClockHost.Calls is a single
        // counter and cannot distinguish which of several hosts fired, or in what order.
        private static readonly List<string> s_priorityOrder = new();
        private static int s_priorityA;
        private static int s_priorityB;
        private static int s_priorityC;
        private static StateUpdater<int> s_setPriorityLive;

        [Component]
        private static VNode PriorityHostA()
        {
            Hooks.UseFrame(_ => s_priorityOrder.Add("A"), s_priorityA);
            return V.Div(className: "w-[1px] h-[1px]");
        }

        [Component]
        private static VNode PriorityHostB()
        {
            Hooks.UseFrame(_ => s_priorityOrder.Add("B"), s_priorityB);
            return V.Div(className: "w-[1px] h-[1px]");
        }

        [Component]
        private static VNode PriorityHostC()
        {
            Hooks.UseFrame(_ => s_priorityOrder.Add("C"), s_priorityC);
            return V.Div(className: "w-[1px] h-[1px]");
        }

        // Mounted C, B, A — deliberately the REVERSE of alphabetical/priority order, so a test asserting
        // firing order A, B, C can only be explained by priority, never by coincidentally matching mount
        // order too.
        [Component]
        private static VNode ThreeHostParent()
        {
            return V.Div(children: new VNode[]
            {
                V.Component(PriorityHostC, key: "c"),
                V.Component(PriorityHostB, key: "b"),
                V.Component(PriorityHostA, key: "a"),
            });
        }

        [Component]
        private static VNode LiveHost()
        {
            var (priority, setPriority) = Hooks.UseState(0);
            s_setPriorityLive = setPriority;
            Hooks.UseFrame(_ => s_priorityOrder.Add("Live"), priority);
            return V.Div(className: "w-[1px] h-[1px]");
        }

        [Component]
        private static VNode FixedHost()
        {
            Hooks.UseFrame(_ => s_priorityOrder.Add("Fixed"), 0);
            return V.Div(className: "w-[1px] h-[1px]");
        }

        // Mounted Fixed then Live, both at the default priority 0 — ties break by mount order, so the
        // baseline firing order is Fixed, Live until a test moves Live's priority below 0.
        [Component]
        private static VNode LiveParent()
        {
            return V.Div(children: new VNode[]
            {
                V.Component(FixedHost, key: "fixed"),
                V.Component(LiveHost, key: "live"),
            });
        }

        // Shared arrange: mount, run the passive effect that subscribes each host, absorb the zero-delta
        // arm-time firing (TimerState.start equals "now" on the very first fire, per the per-update cadence
        // contract below), then clear the log so a test's own Act starts from empty.
        private void MountAndArm(VNode root)
        {
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, UseFrameFakeClockHost.ReadFakeClock);
            _mounted = V.Mount(_host.Root, root);
            _mounted.FlushEffectsForTest();
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            s_priorityOrder.Clear();
        }

        private void Tick()
        {
            UseFrameFakeClockHost.Ms += 16;
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
        }

        [Test]
        public void Given_MountOrderCBA_When_PrioritiesAre123ForABC_Then_FiringFollowsPriorityOrder()
        {
            // Arrange
            s_priorityA = 1;
            s_priorityB = 2;
            s_priorityC = 3;
            MountAndArm(V.Component(ThreeHostParent, key: "root"));

            // Act
            Tick();

            // Assert — mounted C, B, A but fires A, B, C: priority order wins over mount order.
            Assert.That(s_priorityOrder, Is.EqualTo(new[] { "A", "B", "C" }));
        }

        [Test]
        public void Given_EqualPriorities_When_Ticked_Then_FiringFollowsMountOrder()
        {
            // Arrange — every host stays at the default priority 0 (SetUp).
            MountAndArm(V.Component(ThreeHostParent, key: "root"));

            // Act
            Tick();

            // Assert — ties fall back to subscription order, matching ThreeHostParent's own C, B, A
            // mount order exactly (no priority is pulling anything out of place).
            Assert.That(s_priorityOrder, Is.EqualTo(new[] { "C", "B", "A" }));
        }

        [Test]
        public void Given_APriorityLoweredAcrossARerender_When_TickedAgain_Then_TheNewPriorityAppliesLive()
        {
            // Arrange — Fixed then Live, both priority 0: ties break by mount order (Fixed first).
            MountAndArm(V.Component(LiveParent, key: "root"));
            Tick();
            Assume.That(s_priorityOrder, Is.EqualTo(new[] { "Fixed", "Live" }),
                "Precondition: equal priorities fire in mount order before Live's priority changes");

            // Act — Live's priority drops below Fixed's, live, with no remount involved.
            s_setPriorityLive.Invoke(-1);
            _mounted.FlushStateForTest();
            s_priorityOrder.Clear();
            Tick();

            // Assert — the very next tick already reflects the new priority.
            Assert.That(s_priorityOrder, Is.EqualTo(new[] { "Live", "Fixed" }));
        }
    }
}
