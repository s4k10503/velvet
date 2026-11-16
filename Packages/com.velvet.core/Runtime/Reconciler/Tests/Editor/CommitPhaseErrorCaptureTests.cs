using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies where an error goes that a layout effect, a ref setup or a passive effect throws while its commit
    /// runs, or a ref's cleanup throws as a render removes its element, as React's captureCommitPhaseError sends it.
    /// <list type="bullet">
    /// <item>The commit goes on: the effects and ref setups after the throwing one, its siblings' among them, still
    /// run.</item>
    /// <item>The nearest boundary above renders its fallback in a follow-up commit, after those, and not inside the
    /// commit that threw.</item>
    /// <item>With no boundary above, the root V.Mount made renders nothing and reports the error through
    /// <see cref="MountOptions.OnUncaughtError"/>, which logs it by default.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class CommitPhaseErrorCaptureTests
    {
        private static readonly List<string> s_log = new();

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_log.Clear();
            s_setHost = default;
            s_button = null;
        }

        [Test]
        public void Given_ABoundaryOverTwoSiblingsWhoseFirstLayoutEffectThrows_When_Mounted_Then_TheSecondsLayoutEffectRunsBeforeTheFallbacks()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(LayoutBoundary, key: "boundary"), CaughtErrors.Unlogged);

            // Assert
            Assert.That(string.Join(", ", s_log), Is.EqualTo("a layout, b layout, fallback layout"));
        }

        [Test]
        public void Given_ABoundaryOverTwoSiblingsWhoseFirstRefSetupThrows_When_Mounted_Then_TheSecondsRefSetupRunsBeforeTheFallbacksLayoutEffect()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(RefBoundary, key: "boundary"), CaughtErrors.Unlogged);

            // Assert
            Assert.That(string.Join(", ", s_log), Is.EqualTo("a ref, b ref, fallback layout"));
        }

        [Test]
        public void Given_ABoundaryOverTwoSiblingsWhoseFirstPassiveEffectThrows_When_TheEffectsRun_Then_TheSecondsPassiveEffectRunsBeforeTheFallbacksLayoutEffect()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(PassiveBoundary, key: "boundary"), CaughtErrors.Unlogged);

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(string.Join(", ", s_log), Is.EqualTo("a passive, b passive, fallback layout"));
        }

        [Test]
        public void Given_NoBoundary_When_ALayoutEffectThrowsBesideASibling_Then_TheSiblingsLayoutEffectRunsAndTheTargetHoldsNothing()
        {
            // Arrange
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: a boom");

            // Act
            using var mounted = V.Mount(_root, V.Div(children: new VNode[]
            {
                V.Component(ThrowingLayoutRender, "a", key: "a"),
                V.Component(LoggingLayoutRender, "b", key: "b"),
            }));

            // Assert
            Assert.That($"{string.Join(", ", s_log)} | {_root.childCount}", Is.EqualTo("a layout, b layout | 0"));
        }

        [Test]
        public void Given_ALayoutEffectThatDispatchesAClickAfterASiblingsLayoutEffectThrew_When_Mounted_Then_TheBoundaryRendersItsFallbackOnlyAfterTheCommit()
        {
            // Act
            using var mounted = V.Mount(_root, V.Component(ClickingBoundary, key: "boundary"), CaughtErrors.Unlogged);

            // Assert
            Assert.That(
                string.Join(", ", s_log),
                Is.EqualTo("a layout, b layout, b clicked, fallback factory, fallback layout"));
        }

        // GREEN_ON_BASE(characterization): the base shows the fallback by a swap inside the throwing effect, which ends
        // in the same children this case reads.
        [Test]
        public void Given_ABoundaryWhoseParentRendersAgainInTheDrainThatShowsItsFallback_When_ALayoutEffectBelowItThrows_Then_OnlyTheFallbackIsLeft()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(WalkingHost, key: "host"), CaughtErrors.Unlogged);

            // Act
            s_setHost.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(string.Join(",", _root.Q("host").Children().Select(child => child.name)), Is.EqualTo("fallback"));
        }

        // GREEN_ON_BASE(characterization): the base shows the fallback by a swap inside the passive flush, which ends
        // in the same children this case reads.
        [Test]
        public void Given_AClickThatRendersAChildWhosePassiveEffectThrows_When_TheClicksDrainReturns_Then_TheFallbackIsShown()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(ClickHost, key: "host"), CaughtErrors.Unlogged);

            // Act
            _root.Q<Button>("go").SimulateClick();

            // Assert
            Assert.That(_root.Q<Label>("fallback") != null, Is.True);
        }

        [Test]
        public void Given_ARenderThatRemovesAnElementWhoseRefCleanupThrowsAheadOfASiblingItUpdates_When_ItCommits_Then_TheSiblingsLayoutEffectRunsBeforeTheFallbacks()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(RemovingHost, key: "host"), CaughtErrors.Unlogged);
            s_log.Clear();

            // Act
            s_setRemovingHost.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(string.Join(", ", s_log), Is.EqualTo("after 1 layout, fallback layout"));
        }

        private static StateUpdater<int> s_setHost;
        private static Button s_button;

        [Component(IsErrorBoundary = true)]
        private static VNode ClickingBoundary()
        {
            Hooks.UseFallback(_ =>
            {
                s_log.Add("fallback factory");
                return V.Component(FallbackRender, key: "fallback");
            });
            return V.Fragment(children: new VNode[]
            {
                V.Component(ThrowingLayoutRender, "a", key: "a"),
                V.Component(ClickingLayoutRender, key: "b"),
            });
        }

        [Component]
        private static VNode ClickingLayoutRender()
        {
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_log.Add("b layout");
                s_button.SimulateClick();
                s_log.Add("b clicked");
                return null;
            }), Array.Empty<object>());
            return V.Button(name: "trigger", onClick: () => { }, refCallback: element =>
            {
                s_button = (Button)element;
                return null;
            });
        }

        [Component]
        private static VNode WalkingHost()
        {
            var (n, setN) = Hooks.UseState(0);
            s_setHost = setN;
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                if (n == 1) setN.Invoke(2);
                return null;
            }), new object[] { n });
            return V.Div(name: "host", children: new VNode[] { V.Component(WalkingBoundary, n, key: "boundary") });
        }

        [Component(IsErrorBoundary = true)]
        private static VNode WalkingBoundary(int n)
        {
            Hooks.UseFallback(_ => V.Label(name: "fallback", text: "fallback"));
            return V.Component(ThrowingFromTickRender, n, key: "child");
        }

        [Component]
        private static VNode ThrowingFromTickRender(int n)
        {
            Hooks.UseLayoutEffect(
                (Func<Action>)(() => n == 1 ? throw new InvalidOperationException("boom") : (Action)null),
                new object[] { n });
            return V.Label(name: "child", text: "child");
        }

        [Component]
        private static VNode ClickHost()
        {
            var (show, setShow) = Hooks.UseState(false);
            return V.Div(children: new VNode[]
            {
                V.Button(name: "go", onClick: () => setShow.Invoke(true)),
                V.Component(ShowBoundary, show, key: "boundary"),
            });
        }

        [Component(IsErrorBoundary = true)]
        private static VNode ShowBoundary(bool show)
        {
            Hooks.UseFallback(_ => V.Label(name: "fallback", text: "fallback"));
            return show ? V.Component(ThrowingPassiveRender, "a", key: "a") : V.Label(text: "idle");
        }

        private static StateUpdater<int> s_setRemovingHost;

        // One instance, so the render that keeps the element leaves its installed ref alone.
        private static readonly Func<VisualElement, Action> s_throwingCleanupRef =
            static _ => () => throw new InvalidOperationException("cleanup boom");

        // Its render at 1 walks the boundary, removes the element below it and then updates the sibling after it.
        [Component]
        private static VNode RemovingHost()
        {
            var (n, setN) = Hooks.UseState(0);
            s_setRemovingHost = setN;
            return V.Div(children: new VNode[]
            {
                V.Component(RemovingBoundary, n, key: "boundary"),
                V.Component(LoggingTickRender, n, key: "after"),
            });
        }

        [Component(IsErrorBoundary = true)]
        private static VNode RemovingBoundary(int n)
        {
            Hooks.UseFallback(_ => V.Component(FallbackRender, key: "fallback"));
            return V.Component(ElementRemovingRender, n, key: "remover");
        }

        [Component]
        private static VNode ElementRemovingRender(int n)
            => V.Div(children: n == 0 ? new VNode[] { V.Label(text: "removed", refCallback: s_throwingCleanupRef) } : Array.Empty<VNode>());

        [Component]
        private static VNode LoggingTickRender(int n)
        {
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_log.Add("after " + n + " layout");
                return null;
            }), new object[] { n });
            return V.Label(text: "after");
        }

        [Component(IsErrorBoundary = true)]
        private static VNode LayoutBoundary()
        {
            Hooks.UseFallback(_ => V.Component(FallbackRender, key: "fallback"));
            return V.Fragment(children: new VNode[]
            {
                V.Component(ThrowingLayoutRender, "a", key: "a"),
                V.Component(LoggingLayoutRender, "b", key: "b"),
            });
        }

        [Component(IsErrorBoundary = true)]
        private static VNode RefBoundary()
        {
            Hooks.UseFallback(_ => V.Component(FallbackRender, key: "fallback"));
            return V.Fragment(children: new VNode[]
            {
                V.Component(ThrowingRefRender, key: "a"),
                V.Component(LoggingRefRender, key: "b"),
            });
        }

        [Component(IsErrorBoundary = true)]
        private static VNode PassiveBoundary()
        {
            Hooks.UseFallback(_ => V.Component(FallbackRender, key: "fallback"));
            return V.Fragment(children: new VNode[]
            {
                V.Component(ThrowingPassiveRender, "a", key: "a"),
                V.Component(LoggingPassiveRender, "b", key: "b"),
            });
        }

        [Component]
        private static VNode FallbackRender()
        {
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_log.Add("fallback layout");
                return null;
            }), Array.Empty<object>());
            return V.Label(name: "fallback", text: "fallback");
        }

        [Component]
        private static VNode ThrowingLayoutRender(string name)
        {
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_log.Add(name + " layout");
                throw new InvalidOperationException(name + " boom");
            }), Array.Empty<object>());
            return V.Label(text: name);
        }

        [Component]
        private static VNode LoggingLayoutRender(string name)
        {
            Hooks.UseLayoutEffect((Func<Action>)(() =>
            {
                s_log.Add(name + " layout");
                return null;
            }), Array.Empty<object>());
            return V.Label(text: name);
        }

        [Component]
        private static VNode ThrowingPassiveRender(string name)
        {
            Hooks.UseEffect((Func<Action>)(() =>
            {
                s_log.Add(name + " passive");
                throw new InvalidOperationException(name + " boom");
            }), Array.Empty<object>());
            return V.Label(text: name);
        }

        [Component]
        private static VNode LoggingPassiveRender(string name)
        {
            Hooks.UseEffect((Func<Action>)(() =>
            {
                s_log.Add(name + " passive");
                return null;
            }), Array.Empty<object>());
            return V.Label(text: name);
        }

        [Component]
        private static VNode ThrowingRefRender()
            => V.Label(text: "a", refCallback: _ =>
            {
                s_log.Add("a ref");
                throw new InvalidOperationException("a boom");
            });

        [Component]
        private static VNode LoggingRefRender()
            => V.Label(text: "b", refCallback: _ =>
            {
                s_log.Add("b ref");
                return null;
            });
    }
}
