using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what an exception leaving an immediate-tier drain leaves behind: the tier still registers a
    /// frame-boundary drain for a later update, and for an update queued before the exception left.
    /// </summary>
    /// <remarks>
    /// The exception is thrown by an imperative-handle factory in the drain's layout commit, which nothing between
    /// it and the drain catches. Each assertion folds in that the drain did throw, so a change that stops the
    /// exception leaving reddens these cases rather than passing them with nothing measured.
    /// </remarks>
    [TestFixture]
    internal sealed class DrainExceptionRecoveryTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_setTick = default;
            s_setOther = default;
            s_queueOtherBeforeThrowing = false;
            s_handle.Set(null);
        }

        [Test]
        public void Given_AnExceptionThatLeftAnImmediateDrain_When_AnotherComponentUpdates_Then_AFrameBoundaryDrainIsRegistered()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            var scheduler = mounted.GetSchedulerForTest();
            s_setTick.Invoke(1);
            var threw = DrainThrows(scheduler);
            var callbacksBefore = scheduler.ScheduledCallbackCount;

            // Act
            s_setOther.Invoke(1);

            // Assert
            Assert.That((threw, scheduler.ScheduledCallbackCount - callbacksBefore), Is.EqualTo((true, 1)),
                "A drain that threw must not leave the immediate tier refusing to register the next one");
        }

        [Test]
        public void Given_AnUpdateQueuedInADrainBeforeAnExceptionLeftIt_When_TheExceptionLeaves_Then_AFrameBoundaryDrainIsRegisteredForIt()
        {
            // Arrange — the factory updates the other component, then throws
            using var mounted = V.Mount(_root, V.Component(HostRender, key: "host"));
            var scheduler = mounted.GetSchedulerForTest();
            s_queueOtherBeforeThrowing = true;
            s_setTick.Invoke(1);
            var callbacksBefore = scheduler.ScheduledCallbackCount;

            // Act
            var threw = DrainThrows(scheduler);

            // Assert
            Assert.That(
                (threw, scheduler.ImmediatePendingCount, scheduler.ScheduledCallbackCount - callbacksBefore),
                Is.EqualTo((true, 1, 1)),
                "An update the drain that threw never reached is left with a drain registered to commit it");
        }

        private static bool DrainThrows(FiberBatchScheduler scheduler)
        {
            try
            {
                scheduler.DrainImmediateForTest();
            }
            catch (TargetInvocationException thrown) when (thrown.InnerException?.Message == HandleFailure)
            {
                return true;
            }
            return false;
        }

        private const string HandleFailure = "imperative handle failed";

        private static StateUpdater<int> s_setTick;
        private static StateUpdater<int> s_setOther;
        private static bool s_queueOtherBeforeThrowing;
        private static readonly Ref<string> s_handle = new();

        private static VNode HostRender()
            => V.Div(children: new VNode[]
            {
                V.Component(HandleOwnerRender, key: "owner"),
                V.Component(OtherRender, key: "other"),
            });

        private static VNode HandleOwnerRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            Hooks.UseImperativeHandle(s_handle, () =>
            {
                if (tick == 0) return "handle";
                if (s_queueOtherBeforeThrowing) s_setOther.Invoke(1);
                throw new InvalidOperationException(HandleFailure);
            });
            return V.Label(text: "owner:" + tick);
        }

        private static VNode OtherRender()
        {
            var (value, setValue) = Hooks.UseState(0);
            s_setOther = setValue;
            return V.Label(text: "other:" + value);
        }
    }
}
