using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

using Velvet;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the post-animation contract of AnimatePresence and its style transition scheduler — the behaviour
    /// observable only once schedule.Execute / ExecuteLater advance. Previously this needed a live PlayMode loop;
    /// here a simulated panel (see the base) advances the clock and ticks the scheduler deterministically via
    /// <see cref="SimulatedPanelTestBase.Frame"/>, so every enter/exit/stagger phase is driven exactly and headless:
    /// <list type="bullet">
    /// <item>On mount, the enter-from class is applied immediately; on the next tick it is swapped for the enter-to
    /// class, firing the transition.</item>
    /// <item>When the enter transition completes, the enter-to class is removed and the inline transition styles
    /// (duration, timing function, delay) are cleared back to a clean state.</item>
    /// <item>When an element is removed, it stays in the DOM through the exit transition and is removed only after
    /// the exit completes.</item>
    /// <item>The enter and exit completions each invoke the supplied onComplete callback.</item>
    /// <item>With a stagger delay, the first child transitions on the next tick while later children are withheld;
    /// once every staggered transition completes, all enter-to classes and inline styles are cleared.</item>
    /// </list>
    /// These read inline style keywords and class lists only (no resolvedStyle), so no rendering pass is needed.
    /// </summary>
    [TestFixture]
    internal sealed class AnimatePresenceAnimationTests : SimulatedPanelTestBase
    {
        private const float DurationSec = 0.1f;

        private Reconciler _reconciler;

        protected override void OnSetUp()
        {
            _reconciler = new Reconciler();
            s_exitConfig = null;
            s_exitShow = null;
            s_exitVariantShow = null;
            s_waitConfig = null;
            s_waitKey = null;
            s_throwingExitConfig = null;
            s_throwingExitShow = null;
        }

        protected override void OnTearDown() => _reconciler?.Dispose();

        // One frame (replaces a PlayMode `yield return null`): ticks the scheduler, advancing a real-frame-sized step.
        private void Tick() => Frame(16);

        // Advance well past the given duration in real-frame-sized ticks, so multi-tick scheduling (startAction,
        // the completion timer, and the two-phase drop re-render) all fire. The +0.2s margin absorbs the internal
        // animation grace and the drop cycle without being coupled to their exact constants.
        private void AdvancePast(float seconds)
        {
            var steps = (int)((seconds + 0.2f) * 1000f / 16f) + 1;
            for (var i = 0; i < steps; i++) Frame(16);
        }

        #region Enter Animation Completion

        [Test]
        public void When_Mounted_Then_EnterFromClassIsAppliedImmediately()
        {
            // Arrange
            var config = NewConfig();
            var children = SingleMotion(config);

            // Act
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), children);
            var motionElement = Root.ElementAt(0);

            // Assert
            Assert.That(motionElement.ClassListContains("test-enter-from"), Is.True,
                "The enter-from class is applied immediately on mount");
        }

        // The two halves of the next-frame enter-class swap (enter-to added, enter-from removed) share one body and
        // differ only by which class is checked and its expected presence; named per case to keep the GWT Then.
        [TestCase("test-enter-to", true, TestName = "Given_MountedMotion_When_NextFrameRuns_Then_EnterToClassReplacesEnterFromClass")]
        [TestCase("test-enter-from", false, TestName = "Given_MountedMotion_When_NextFrameRuns_Then_EnterFromClassIsRemoved")]
        public void Given_MountedMotion_When_NextFrameRuns_Then_EnterClassesSwap(string className, bool expectedPresent)
        {
            // Arrange
            var config = NewConfig();
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), SingleMotion(config));
            var motionElement = Root.ElementAt(0);
            Assume.That(motionElement.ClassListContains("test-enter-from"), Is.True,
                "Precondition: the enter-from class is applied on mount");

            // Act
            Tick();

            // Assert
            Assert.That(motionElement.ClassListContains(className), Is.EqualTo(expectedPresent),
                "On the next frame the enter-to class is added and the enter-from class is removed, firing the transition");
        }

        [Test]
        public void Given_EnterTransition_When_Completed_Then_EnterToClassIsRemoved()
        {
            // Arrange
            var config = NewConfig();
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), SingleMotion(config));
            var motionElement = Root.ElementAt(0);
            Tick();
            Assume.That(motionElement.ClassListContains("test-enter-to"), Is.True,
                "Precondition: the enter-to class is applied during the transition");

            // Act
            AdvancePast(config.DurationSec);

            // Assert
            Assert.That(motionElement.ClassListContains("test-enter-to"), Is.False,
                "The enter-to class is removed once the enter transition completes");
        }

        // The three inline transition styles cleared on enter completion share one body and differ only by which
        // style keyword is read; a Func selector keeps the single Assert, named per case to keep the GWT Then.
        private static IEnumerable<TestCaseData> InlineTransitionStyleClearedCases()
        {
            yield return new TestCaseData("transition-duration",
                (Func<VisualElement, StyleKeyword>)(e => e.style.transitionDuration.keyword))
                .SetName("Given_EnterTransition_When_Completed_Then_InlineTransitionDurationIsCleared");
            yield return new TestCaseData("transition-timing-function",
                (Func<VisualElement, StyleKeyword>)(e => e.style.transitionTimingFunction.keyword))
                .SetName("Given_EnterTransition_When_Completed_Then_InlineTimingFunctionIsCleared");
            yield return new TestCaseData("transition-delay",
                (Func<VisualElement, StyleKeyword>)(e => e.style.transitionDelay.keyword))
                .SetName("Given_EnterTransition_When_Completed_Then_InlineTransitionDelayIsCleared");
        }

        [TestCaseSource(nameof(InlineTransitionStyleClearedCases))]
        public void Given_EnterTransition_When_Completed_Then_InlineTransitionStyleIsCleared(
            string property, Func<VisualElement, StyleKeyword> readKeyword)
        {
            // Arrange
            var config = NewConfig(delaySec: 0.02f);
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), SingleMotion(config));
            var motionElement = Root.ElementAt(0);
            Assume.That(readKeyword(motionElement), Is.Not.EqualTo(StyleKeyword.Null),
                "Precondition: the inline " + property + " is set during the animation");

            // Act
            AdvancePast(config.DelaySec + config.DurationSec);

            // Assert
            Assert.That(readKeyword(motionElement), Is.EqualTo(StyleKeyword.Null),
                "The inline " + property + " is cleared once the enter transition completes");
        }

        [Test]
        public void Given_EnterTransition_When_Completed_Then_OnCompleteCallbackIsInvoked()
        {
            // Arrange
            var callbackInvoked = false;
            var config = NewConfig();
            var scheduler = new StyleAnimationScheduler();
            try
            {
                var element = new VisualElement();
                Root.Add(element);
                Tick();
                scheduler.PlayEnter(element, config, onComplete: () => callbackInvoked = true);

                // Act
                AdvancePast(config.DurationSec);

                // Assert
                Assert.That(callbackInvoked, Is.True, "The enter completion invokes the onComplete callback");
            }
            finally
            {
                scheduler.CancelAll();
            }
        }

        #endregion

        #region Exit Animation Completion

        [Test]
        public void Given_ExitingElement_When_ExitTransitionRunning_Then_ElementRemainsInDom()
        {
            // Arrange
            var config = NewConfig();
            var withChild = SingleMotion(config);
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), withChild);
            var container = Root;
            AdvancePast(config.DurationSec);
            Assume.That(container.childCount, Is.EqualTo(1), "Precondition: the element entered and is present");

            // Act
            var withoutChild = new VNode[] { V.AnimatePresence(children: Array.Empty<VNode>()) };
            _reconciler.Reconcile(Root, withChild, withoutChild);

            // Assert
            Assert.That(container.childCount, Is.EqualTo(1),
                "The element remains in the DOM while the exit transition is running");
        }

        // By design AnimatePresence always lives inside the component tree, so a real owning fiber exists to
        // drive the exit-completion re-render that drops a finished child. This host mounts the presence under a
        // component and toggles the child via state, matching real usage.
        private static StyleTransitionConfig s_exitConfig;
        private static Action<bool> s_exitShow;

        [Component]
        private static VNode ExitRemovalHostRender()
        {
            var (show, setShow) = Hooks.UseState(true);
            s_exitShow = setShow;
            return V.AnimatePresence(children: show
                ? new VNode[] { V.Motion(key: "a", transition: s_exitConfig, children: new VNode[] { V.Label(text: "A") }) }
                : Array.Empty<VNode>());
        }

        [Test]
        public void Given_ExitingElement_When_ExitTransitionCompletes_Then_ElementIsRemovedFromDom()
        {
            // Arrange — the presence is owned by a component, so exit completion can re-render it.
            s_exitConfig = NewConfig();
            using var mounted = V.Mount(Root, V.Component(ExitRemovalHostRender, key: "exit-host"));
            AdvancePast(s_exitConfig.DurationSec);
            Assume.That(LabelTexts(), Is.EqualTo(new[] { "A" }), "Precondition: the child entered and is present");

            // Act — hide the child; its exit animation runs, then the owning component re-renders and drops it.
            s_exitShow.Invoke(false);
            AdvancePast(s_exitConfig.DurationSec);

            // Assert — once the exit transition completes, the element is removed from the DOM.
            Assert.That(LabelTexts(), Is.Empty,
                "The element is removed from the DOM once the exit transition completes");
        }

        [Test]
        public void Given_ExitTransition_When_Completed_Then_OnCompleteCallbackIsInvoked()
        {
            // Arrange
            var callbackInvoked = false;
            var config = NewConfig();
            var scheduler = new StyleAnimationScheduler();
            try
            {
                var element = new VisualElement();
                Root.Add(element);
                Tick();
                scheduler.PlayExit(element, config, onComplete: () => callbackInvoked = true);

                // Act
                AdvancePast(config.DurationSec);

                // Assert
                Assert.That(callbackInvoked, Is.True, "The exit completion invokes the onComplete callback");
            }
            finally
            {
                scheduler.CancelAll();
            }
        }

        [Test]
        public void Given_PresenceWithOnExitComplete_When_AllExitsFinish_Then_CallbackFiresExactlyOnce()
        {
            // Arrange — mount one child, settle its enter, then remove it so its exit runs to completion.
            var fireCount = 0;
            var config = NewConfig();
            VNode[] WithChild() => new VNode[]
            {
                V.AnimatePresence(onExitComplete: () => fireCount++, children: new VNode[]
                {
                    V.Motion(key: "a", transition: config, children: new VNode[] { V.Label(text: "A") }),
                }),
            };
            var withChild = WithChild();
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), withChild);
            AdvancePast(config.DurationSec);

            // Act — remove the only child; the exit animation runs and then drains the exiting set.
            var withoutChild = new VNode[]
            {
                V.AnimatePresence(onExitComplete: () => fireCount++, children: Array.Empty<VNode>()),
            };
            _reconciler.Reconcile(Root, withChild, withoutChild);
            AdvancePast(config.DurationSec);

            // Assert — fires exactly once (not zero, not doubled) as the last in-flight exit drained.
            Assert.That(fireCount, Is.EqualTo(1), "onExitComplete fires exactly once when the last in-flight exit completes");
        }

        [Test]
        public void Given_AnExitingChild_When_ItsKeyIsReAddedBeforeCompletion_Then_TheExitIsCancelledAndTheSameElementIsRetained()
        {
            // Arrange — a component-owned classic-transition child, entered; capture its live element.
            s_exitConfig = NewConfig();
            using var mounted = V.Mount(Root, V.Component(ExitRemovalHostRender, key: "exit-host"));
            AdvancePast(s_exitConfig.DurationSec);
            var original = Root.ElementAt(0);

            // Act — begin the exit, then re-add the key before it completes; advance past when it WOULD have completed.
            s_exitShow.Invoke(false);
            Tick();
            s_exitShow.Invoke(true);
            AdvancePast(s_exitConfig.DurationSec);

            // Assert — the exit was cancelled and the same element retained (not dropped and recreated).
            Assert.That(Root.ElementAt(0), Is.SameAs(original),
                "Re-adding a key mid-exit cancels the exit and retains the same element rather than dropping and recreating it");
        }

        // By design onExitComplete is a user-supplied callback; it must not be able to block the ghost-drop
        // re-render it sits beside by throwing. Reuses ExitRemovalHostRender's component-owned shape so the
        // drop re-render has a fiber to run on.
        private static StyleTransitionConfig s_throwingExitConfig;
        private static Action<bool> s_throwingExitShow;

        [Component]
        private static VNode ThrowingOnExitCompleteHostRender()
        {
            var (show, setShow) = Hooks.UseState(true);
            s_throwingExitShow = setShow;
            return V.AnimatePresence(
                onExitComplete: () => throw new InvalidOperationException("Test onExitComplete error"),
                children: show
                    ? new VNode[] { V.Motion(key: "a", transition: s_throwingExitConfig, children: new VNode[] { V.Label(text: "A") }) }
                    : Array.Empty<VNode>());
        }

        [Test]
        public void Given_OnExitCompleteThrows_When_ExitTransitionCompletes_Then_TheElementIsStillRemovedFromTheDom()
        {
            // Arrange
            s_throwingExitConfig = NewConfig();
            using var mounted = V.Mount(Root, V.Component(ThrowingOnExitCompleteHostRender, key: "throwing-exit-host"));
            AdvancePast(s_throwingExitConfig.DurationSec);
            Assume.That(LabelTexts(), Is.EqualTo(new[] { "A" }), "Precondition: the child entered and is present");
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: Test onExitComplete error");

            // Act — hide the child; its exit completes and onExitComplete throws.
            s_throwingExitShow.Invoke(false);
            AdvancePast(s_throwingExitConfig.DurationSec);

            // Assert — the ghost-drop re-render still ran despite the throw.
            Assert.That(LabelTexts(), Is.Empty,
                "The exited element is still removed from the DOM even though onExitComplete threw");
        }

        #endregion

        #region Mode Wait

        [Test]
        public void Given_WaitMode_When_KeySwaps_Then_NewChildIsWithheldWhileOldExits()
        {
            // Arrange — a wait-mode presence showing key "a", fully entered.
            var config = NewConfig();
            var showA = WaitPresence("a", config);
            var showB = WaitPresence("b", config);
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), showA);
            AdvancePast(config.DurationSec);
            Assume.That(LabelTexts(), Is.EqualTo(new[] { "A" }), "Precondition: A entered and is present");

            // Act — swap the key to "b".
            _reconciler.Reconcile(Root, showA, showB);

            // Assert — mode=wait holds B back while A exits in a live panel, so only the exiting A is present.
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "A" }));
        }

        [Test]
        public void Given_WaitMode_When_OldChildExitCompletes_Then_TheWithheldNewChildEnters()
        {
            // Arrange — a component-owned wait-mode presence showing A, fully entered.
            s_waitConfig = NewConfig();
            using var mounted = V.Mount(Root, V.Component(WaitHostRender, key: "wait-host"));
            AdvancePast(s_waitConfig.DurationSec);
            Assume.That(LabelTexts(), Is.EqualTo(new[] { "A" }), "Precondition: A entered and is present");
            // Swap to B: A begins exiting and B is withheld (wait mode).
            s_waitKey.Invoke("b");
            Tick();
            Assume.That(LabelTexts(), Is.EqualTo(new[] { "A" }), "Precondition: B is withheld while A exits");

            // Act — let A's exit complete; the withheld B is released and enters (the symmetric release the
            // headless PlayMode loop could not drive, now deterministic via the simulator).
            AdvancePast(s_waitConfig.DurationSec);

            // Assert — A is gone and the previously-withheld B is now the live child.
            Assert.That(LabelTexts(), Is.EqualTo(new[] { "B" }));
        }

        private static StyleTransitionConfig s_waitConfig;
        private static Action<string> s_waitKey;

        [Component]
        private static VNode WaitHostRender()
        {
            var (key, setKey) = Hooks.UseState("a");
            s_waitKey = setKey;
            return V.AnimatePresence(mode: AnimatePresenceMode.Wait, children: new VNode[]
            {
                V.Motion(key: key, transition: s_waitConfig,
                    children: new VNode[] { V.Label(text: key.ToUpperInvariant()) }),
            });
        }

        #endregion

        #region Variant initial (initial → animate enter)

        private static readonly System.Collections.Generic.Dictionary<string, string> Fade = new()
        {
            ["visible"] = "opacity-100",
            ["hidden"] = "opacity-0",
        };

        [Test]
        public void Given_AMotionWithInitial_When_EnterCompletes_Then_ItRestsAtTheAnimateVariant()
        {
            // Arrange — a direct-child Motion entering from variants[initial]=hidden toward variants[animate]=visible.
            var tree = new VNode[]
            {
                V.AnimatePresence(children: new VNode[]
                {
                    V.Motion(key: "a", variants: Fade, initial: "hidden", animate: "visible",
                        transition: new StyleTransitionConfig { DurationSec = DurationSec }),
                }),
            };
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), tree);
            var motionElement = Root.ElementAt(0);
            Assume.That(motionElement.ClassListContains("opacity-0"), Is.True,
                "Precondition: the enter starts at variants[initial]");

            // Act — let the enter transition complete.
            AdvancePast(DurationSec);

            // Assert — it rests at variants[animate] PERSISTENTLY (the animate class is kept on completion, unlike
            // the classic transition's transient to-class).
            Assert.That(
                (motionElement.ClassListContains("opacity-100"), motionElement.ClassListContains("opacity-0")),
                Is.EqualTo((true, false)));
        }

        private static Action<bool> s_exitVariantShow;

        [Component]
        private static VNode ExitVariantHostRender()
        {
            var (show, setShow) = Hooks.UseState(true);
            s_exitVariantShow = setShow;
            return V.AnimatePresence(children: show
                ? new VNode[]
                {
                    V.Motion(key: "a", name: "exit-leaf", variants: Fade, animate: "visible", exit: "hidden",
                        transition: new StyleTransitionConfig { DurationSec = DurationSec },
                        children: new VNode[] { V.Label(text: "A") }),
                }
                : Array.Empty<VNode>());
        }

        [Test]
        public void Given_AMotionWithExit_When_Removed_Then_ItAnimatesToTheExitVariant()
        {
            // Arrange — the presence is owned by a component (so exit completion can re-render it), entered at visible.
            using var mounted = V.Mount(Root, V.Component(ExitVariantHostRender, key: "exit-host"));
            AdvancePast(DurationSec);
            var leaf = Root.Q("exit-leaf");
            Assume.That(leaf != null && leaf.ClassListContains("opacity-100"), Is.True,
                "Precondition: the child entered and rests at variants[animate]");

            // Act — remove the child; its exit animates from variants[animate] to variants[exit] before unmount.
            s_exitVariantShow.Invoke(false);
            Tick();
            Tick();

            // Assert — during the exit it shows variants[exit] (and is still mounted as a ghost, not yet removed).
            Assert.That(Root.Q("exit-leaf")?.ClassListContains("opacity-0"), Is.True);
        }

        [Test]
        public void Given_AnExitingVariantMotion_When_ReAddedMidExit_Then_ItReturnsToTheRestingAnimateVariant()
        {
            // Arrange — entered and resting at variants[animate]=opacity-100.
            using var mounted = V.Mount(Root, V.Component(ExitVariantHostRender, key: "exit-host"));
            AdvancePast(DurationSec);
            Assume.That(Root.Q("exit-leaf")?.ClassListContains("opacity-100"), Is.True,
                "Precondition: entered and rests at variants[animate]");

            // Act — start the exit and let it begin animating (swap to variants[exit]), then re-add the key
            // mid-exit (an interrupt). The exit is cancelled before it completes.
            s_exitVariantShow.Invoke(false);
            Tick();
            Tick();
            Assume.That(Root.Q("exit-leaf")?.ClassListContains("opacity-0"), Is.True,
                "Precondition: the exit is in flight (showing variants[exit]) and not yet removed");
            s_exitVariantShow.Invoke(true);
            Tick();
            Tick();

            // Assert — cancelling the exit returns the element to its resting variant (opacity-100); it must NOT be
            // left without it.
            var leaf = Root.Q("exit-leaf");
            Assume.That(leaf, Is.Not.Null, "the re-added child is mounted");
            Assert.That(
                (leaf.ClassListContains("opacity-100"), leaf.ClassListContains("opacity-0")),
                Is.EqualTo((true, false)));
        }

        #endregion

        #region Stagger Animation

        // In the first stagger window the no-delay first child has transitioned while the staggered second child is
        // still withheld; both halves share one body and differ only by child index + expected presence.
        [TestCase(0, true, TestName = "Given_StaggeredChildren_When_FirstFramePasses_Then_FirstChildHasEnterToClass")]
        [TestCase(1, false, TestName = "Given_StaggeredChildren_When_FirstFramePasses_Then_SecondChildIsStillWithheld")]
        public void Given_StaggeredChildren_When_FirstFramePasses_Then_OnlyTheNoDelayChildHasEnterToClass(
            int childIndex, bool expectedHasEnterTo)
        {
            // Arrange
            var config = NewConfig();
            const float staggerSec = 0.1f;
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), StaggeredMotions(config, staggerSec));
            var child = Root.ElementAt(childIndex);

            // Act — advance one tick plus half the stagger window, before the second child's stagger delay elapses.
            Tick();
            Frame((long)(staggerSec * 0.5f * 1000));

            // Assert
            Assert.That(child.ClassListContains("test-enter-to"), Is.EqualTo(expectedHasEnterTo),
                "On the first frame the no-delay first child transitions while the staggered second child is withheld");
        }

        [Test]
        public void Given_StaggeredExit_When_FirstStaggerWindow_Then_FirstGhostSwappedButSecondWithheld()
        {
            // Arrange — three children entered and settled, then all removed so they exit as staggered ghosts.
            var config = NewConfig();
            const float staggerSec = 0.1f;
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), StaggeredMotions(config, staggerSec));
            AdvancePast(config.DurationSec);
            var first = Root.ElementAt(0);
            var second = Root.ElementAt(1);

            // Act — remove all children; exits begin, staggered by staggerSec × index. Observe before the second
            // ghost's stagger delay elapses.
            _reconciler.Reconcile(Root, StaggeredMotions(config, staggerSec), new VNode[]
            {
                V.AnimatePresence(staggerSec: staggerSec, children: Array.Empty<VNode>()),
            });
            Tick();
            Frame((long)(staggerSec * 0.5f * 1000));

            // Assert — the first ghost (no stagger delay) has swapped to the exit-to class; the second is still
            // withheld by its stagger delay (exit staggers like enter).
            Assert.That(
                (first.ClassListContains("test-exit-to"), second.ClassListContains("test-exit-to")),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_StaggeredChildren_When_AllTransitionsComplete_Then_LastChildEnterToClassIsRemoved()
        {
            // Arrange
            var config = NewConfig();
            const float staggerSec = 0.1f;
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), StaggeredMotions(config, staggerSec));
            var thirdChild = Root.ElementAt(2);

            // Act — stagger(0.2s) + duration covers the last child's full timeline.
            AdvancePast(staggerSec * 2 + config.DurationSec);

            // Assert
            Assert.That(thirdChild.ClassListContains("test-enter-to"), Is.False,
                "The last staggered child's enter-to class is removed once its transition completes");
        }

        [Test]
        public void Given_StaggeredChildren_When_AllTransitionsComplete_Then_LastChildInlineStylesAreCleared()
        {
            // Arrange
            var config = NewConfig();
            const float staggerSec = 0.1f;
            _reconciler.Reconcile(Root, Array.Empty<VNode>(), StaggeredMotions(config, staggerSec));
            var thirdChild = Root.ElementAt(2);

            // Act
            AdvancePast(staggerSec * 2 + config.DurationSec);

            // Assert
            Assert.That(thirdChild.style.transitionDuration.keyword, Is.EqualTo(StyleKeyword.Null),
                "The last staggered child's inline transition-duration is cleared once its transition completes");
        }

        #endregion

        #region Helpers

        private static StyleTransitionConfig NewConfig(float delaySec = 0f, EasingMode easing = EasingMode.EaseOut)
            => new()
            {
                EnterFromClass = "test-enter-from",
                EnterToClass = "test-enter-to",
                ExitFromClass = "test-exit-from",
                ExitToClass = "test-exit-to",
                DurationSec = DurationSec,
                DelaySec = delaySec,
                Easing = easing,
            };

        private static VNode[] SingleMotion(StyleTransitionConfig config) => new VNode[]
        {
            V.AnimatePresence(children: new VNode[]
            {
                V.Motion(key: "a", transition: config, children: new VNode[] { V.Label(text: "A") }),
            }),
        };

        private static VNode[] WaitPresence(string key, StyleTransitionConfig config) => new VNode[]
        {
            V.AnimatePresence(mode: AnimatePresenceMode.Wait, children: new VNode[]
            {
                V.Motion(key: key, transition: config, children: new VNode[] { V.Label(text: key.ToUpperInvariant()) }),
            }),
        };

        // Text of every Label currently under the root — robust to the DOM-less presence structure.
        private string[] LabelTexts() => Root.Query<Label>().ToList().Select(l => l.text).ToArray();

        private static VNode[] StaggeredMotions(StyleTransitionConfig config, float staggerSec) => new VNode[]
        {
            V.AnimatePresence(staggerSec: staggerSec, children: new VNode[]
            {
                V.Motion(key: "a", transition: config, children: new VNode[] { V.Label(text: "A") }),
                V.Motion(key: "b", transition: config, children: new VNode[] { V.Label(text: "B") }),
                V.Motion(key: "c", transition: config, children: new VNode[] { V.Label(text: "C") }),
            }),
        };

        #endregion
    }
}
