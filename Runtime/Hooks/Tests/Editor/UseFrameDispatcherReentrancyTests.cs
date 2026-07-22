using System.Collections.Generic;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins <see cref="UseFrameDispatcher"/>'s reentrancy guard on the EditMode fake clock: an error
    /// boundary's fallback swap, triggered synchronously from inside one subscriber's callback, can
    /// dispose a LATER-sorted sibling before <c>Tick</c>'s own snapshot foreach ever reaches that
    /// sibling's entry — <c>Unsubscribe</c> must clear <c>Active</c> immediately (not just remove the
    /// entry from the list) so the stale snapshot reference is skipped rather than firing once more on an
    /// already-disposed component.
    /// </summary>
    internal sealed class UseFrameDispatcherReentrancyTests
    {
        // An order log, not a bare counter: a log rules out the confound "Victim simply fired before
        // Thrower this tick" — which would also produce a nonzero Victim count but would not demonstrate
        // a callback firing AFTER its owning component was synchronously unmounted.
        private static readonly List<string> s_log = new();
        private static bool s_shouldThrow;

        private HeadlessEditorPanelHost _host;
        private MountedTree _mounted;

        [SetUp]
        public void SetUp()
        {
            _host = new HeadlessEditorPanelHost();
            UseFrameFakeClockHost.Reset();
            s_shouldThrow = false;
            s_log.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            _mounted?.Dispose();
            _mounted = null;
            _host?.Dispose();
            _host = null;
        }

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
    }
}
