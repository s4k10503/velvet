using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <see cref="V.ErrorBoundary(Func{Exception, ErrorBoundaryReset, VNode}, VNode[], object[], Action{ErrorBoundaryResetDetails}, string)"/>
    /// helper — react-error-boundary's <c>&lt;ErrorBoundary fallbackRender resetKeys onReset&gt;</c>.
    /// <list type="bullet">
    /// <item>The reset the fallback is handed renders the children again, and calls <c>onReset</c>.</item>
    /// <item>A reset key the parent changes renders the children again.</item>
    /// <item>A null <c>fallbackRender</c> or null children is rejected.</item>
    /// <item>A reset made in a transition is taken by the transition's render, not by an urgent one before it.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class VErrorBoundaryResetTests
    {
        private static bool s_throws;
        private static ErrorBoundaryReset s_reset;
        private static Action<int> s_setResetKey;
        private static Action<int> s_setTick;
        private static TransitionStarter s_start;
        private static List<string> s_resets;

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_throws = false;
            s_reset = null;
            s_setResetKey = null;
            s_setTick = null;
            s_start = default;
            s_resets = new List<string>();
            FiberStrictMode.Enabled = false;
        }

        private string Texts() => string.Join(",", _root.Query<Label>().ToList().Select(label => label.text));

        private static void Settle(MountedTree mounted, Action change)
        {
            change();
            mounted.FlushStateForTest();
            mounted.GetSchedulerForTest().DrainImmediateForTest();
        }

        private MountedTree MountCaught(Func<VNode> host)
        {
            s_throws = true;
            var mounted = V.Mount(_root, V.Component(host, key: "host"), CaughtErrors.Unlogged);
            s_throws = false;
            return mounted;
        }

        [Test]
        public void Given_AHelperBoundaryThatCaught_When_TheResetItsFallbackIsHandedIsInvoked_Then_ItRendersItsChildren()
        {
            // Arrange
            using var mounted = MountCaught(ResettableHostRender);
            var caught = Texts();

            // Act
            Settle(mounted, () => s_reset.Invoke());

            // Assert — the catch is folded in, since a boundary that never caught renders its children as well
            Assert.That(caught + "|" + Texts(), Is.EqualTo("failed:thrown|child"));
        }

        [Test]
        public void Given_AHelperBoundaryThatCaught_When_ItsResetIsInvoked_Then_OnResetIsHandedTheReason()
        {
            // Arrange
            using var mounted = MountCaught(ResettableHostRender);

            // Act
            Settle(mounted, () => s_reset.Invoke("again"));

            // Assert
            Assert.That(string.Join(";", s_resets), Is.EqualTo("ImperativeApi:again"));
        }

        [Test]
        public void Given_AHelperBoundaryThatCaught_When_ItsParentChangesItsResetKeys_Then_ItRendersItsChildren()
        {
            // Arrange
            using var mounted = MountCaught(ResettableHostRender);
            var caught = Texts();

            // Act
            Settle(mounted, () => s_setResetKey.Invoke(1));

            // Assert — the catch is folded in, since a boundary that never caught renders its children as well
            Assert.That(caught + "|" + Texts(), Is.EqualTo("failed:thrown|child"));
        }

        [Test]
        public void Given_AHelperBoundaryThatCaught_When_ItIsResetInATransitionAndAnUrgentUpdateRendersItFirst_Then_OnlyTheTransitionRendersItsChildren()
        {
            // Arrange
            using var mounted = MountCaught(ResettableHostRender);
            s_start.Invoke(() => s_reset.Invoke());
            s_setTick.Invoke(1);

            // Act
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            var afterUrgent = Texts();
            mounted.GetSchedulerForTest().DrainDelayedForTest();

            // Assert
            Assert.That(afterUrgent + "|" + Texts(), Is.EqualTo("failed:thrown|child"));
        }

        [Test]
        public void Given_ANullFallbackRender_When_AHelperBoundaryIsBuilt_Then_ItThrowsArgumentNullException()
        {
            // Act
            TestDelegate build = () => V.ErrorBoundary(
                fallbackRender: null, children: new VNode[] { V.Label(text: "x") });

            // Assert
            Assert.That(build, Throws.ArgumentNullException);
        }

        [Test]
        public void Given_NullChildren_When_AHelperBoundaryWithAFallbackRenderIsBuilt_Then_ItThrowsArgumentNullException()
        {
            // Act
            TestDelegate build = () => V.ErrorBoundary(
                fallbackRender: (_, _) => V.Label(text: "fb"), children: null);

            // Assert
            Assert.That(build, Throws.ArgumentNullException);
        }

        [Component(Compiler = false)]
        private static VNode ThrowerRender(int tick)
        {
            if (s_throws) throw new InvalidOperationException("thrown");
            return V.Label(text: "child");
        }

        [Component(Compiler = false)]
        private static VNode ResettableHostRender()
        {
            var (resetKey, setResetKey) = Hooks.UseState(0);
            var (tick, setTick) = Hooks.UseState(0);
            var (_, start) = Hooks.UseTransition();
            s_setResetKey = setResetKey;
            s_setTick = setTick;
            s_start = start;
            return V.Div(children: new VNode[]
            {
                V.ErrorBoundary(
                    fallbackRender: (error, reset) =>
                    {
                        s_reset = reset;
                        return V.Label(text: "failed:" + error.Message);
                    },
                    children: new VNode[] { V.Component(ThrowerRender, tick, key: "child") },
                    resetKeys: new object[] { resetKey },
                    onReset: details => s_resets.Add(details.Reason + ":" + string.Join(",", details.Args)),
                    key: "boundary"),
            });
        }
    }
}
