using System.Collections.Generic;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins UseFrame's <c>priority</c> parameter on the EditMode fake clock: lower runs earlier within a
    /// panel regardless of mount order, equal priorities fall back to subscription (mount) order, and a
    /// priority change on a live subscription applies on the very next tick without a remount. Complements
    /// <see cref="UseFrameKeyedReorderTests"/> (order stays put across a keyed reorder) — together they
    /// specify <see cref="UseFrameDispatcher"/>'s full ordering contract.
    /// </summary>
    internal sealed class UseFramePriorityTests
    {
        // Order log each host's UseFrame appends its own id to — UseFrameFakeClockHost.Calls is a single
        // counter and cannot distinguish which of several hosts fired, or in what order.
        private static readonly List<string> s_order = new();
        private static int s_priorityA;
        private static int s_priorityB;
        private static int s_priorityC;
        private static StateUpdater<int> s_setPriorityLive;

        [Component]
        private static VNode HostA()
        {
            Hooks.UseFrame(_ => s_order.Add("A"), s_priorityA);
            return V.Div(className: "w-[1px] h-[1px]");
        }

        [Component]
        private static VNode HostB()
        {
            Hooks.UseFrame(_ => s_order.Add("B"), s_priorityB);
            return V.Div(className: "w-[1px] h-[1px]");
        }

        [Component]
        private static VNode HostC()
        {
            Hooks.UseFrame(_ => s_order.Add("C"), s_priorityC);
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
                V.Component(HostC, key: "c"),
                V.Component(HostB, key: "b"),
                V.Component(HostA, key: "a"),
            });
        }

        [Component]
        private static VNode LiveHost()
        {
            var (priority, setPriority) = Hooks.UseState(0);
            s_setPriorityLive = setPriority;
            Hooks.UseFrame(_ => s_order.Add("Live"), priority);
            return V.Div(className: "w-[1px] h-[1px]");
        }

        [Component]
        private static VNode FixedHost()
        {
            Hooks.UseFrame(_ => s_order.Add("Fixed"), 0);
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

        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            UseFrameFakeClockHost.Reset();
            s_priorityA = 0;
            s_priorityB = 0;
            s_priorityC = 0;
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

        // Shared arrange: mount, run the passive effect that subscribes each host, absorb the zero-delta
        // arm-time firing (TimerState.start equals "now" on the very first fire, per
        // UseFramePerFrameContractTests), then clear the log so a test's own Act starts from empty.
        private void MountAndArm(VNode root)
        {
            EditorPanelTestHelpers.SetPanelTimeFunction(_host.Panel, UseFrameFakeClockHost.ReadFakeClock);
            _mounted = V.Mount(_host.Root, root);
            _mounted.FlushEffectsForTest();
            EditorPanelTestHelpers.DriveSchedulerOnce(_host.Panel);
            s_order.Clear();
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
            Assert.That(s_order, Is.EqualTo(new[] { "A", "B", "C" }));
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
            Assert.That(s_order, Is.EqualTo(new[] { "C", "B", "A" }));
        }

        [Test]
        public void Given_APriorityLoweredAcrossARerender_When_TickedAgain_Then_TheNewPriorityAppliesLive()
        {
            // Arrange — Fixed then Live, both priority 0: ties break by mount order (Fixed first).
            MountAndArm(V.Component(LiveParent, key: "root"));
            Tick();
            Assume.That(s_order, Is.EqualTo(new[] { "Fixed", "Live" }),
                "Precondition: equal priorities fire in mount order before Live's priority changes");

            // Act — Live's priority drops below Fixed's, live, with no remount involved.
            s_setPriorityLive.Invoke(-1);
            _mounted.FlushStateForTest();
            s_order.Clear();
            Tick();

            // Assert — the very next tick already reflects the new priority.
            Assert.That(s_order, Is.EqualTo(new[] { "Live", "Fixed" }));
        }
    }
}
