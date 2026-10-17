using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies what an AnimatePresence keeps of a key across a render that takes it out of the committed set: the
    /// drop of an exit that finished, and a mode="wait" render withholding a committed child while another child's
    /// exit runs.
    /// <list type="bullet">
    /// <item>Once that render commits, the presence keeps no exit anchor and no Motion element for the key, and a
    /// withheld key whose exit had finished gives its exited node back to the pool.</item>
    /// <item>Where an abort stopped that render before its removals, the presence puts back its committed children in
    /// their order, and a withheld key's exit, its pending settle and its initial: false record.</item>
    /// <item>Such a render stopped by an abort leaves a withheld key's descendant exit running, so the key's exit
    /// can still settle, and a tree disposed while an exit waits on the passive flush completes that exit first, as
    /// React flushes a root's pending passive effects before unmounting it, and an exit still playing completes
    /// once at the disposal, as Framer lets it run out and calls onExitComplete.</item>
    /// <item>A key coming back in a render an abort stopped keeps the node that render gave it.</item>
    /// <item>A key removed, added again and removed again before the passive flush completes its last exit with the
    /// onExitComplete the last removing render gave, and the quiet exit its return cut short completes nothing of an
    /// exit it plays after.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class PresenceKeyRecordTests
    {
        private static Action<int> s_setTick;
        private static Func<int, VNode[]> s_children;
        private static AnimatePresenceMode s_mode;
        private static bool s_initial;
        private static int s_abortAt;
        // The tick of the render each onExitComplete that ran was given by.
        private static List<int> s_exitCallbacks;

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_setTick = null;
            s_children = _ => Array.Empty<VNode>();
            s_mode = AnimatePresenceMode.Sync;
            s_initial = true;
            s_abortAt = int.MaxValue;
            s_exitCallbacks = new List<int>();
        }

        [Test]
        public void Given_TwoFinishedExitsWhoseDropAnAbortedRenderSkipped_When_TheDropIsPutBack_Then_TheCommittedChildrenKeepTheirOrder()
        {
            // Arrange — "a" and "b" leave with nothing to animate, so both exits finish in the passive flush
            s_children = tick => tick == 0 ? new[] { Plain("a"), Plain("b"), Plain("c") } : new[] { Plain("c") };
            s_abortAt = 2;
            using var mounted = Mount();
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Act — the render dropping both is the one the boundary aborts
            s_setTick.Invoke(2);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(CommittedKeys(mounted), Is.EqualTo("a,b,c"));
        }

        // GREEN_ON_BASE(characterization): the base rendered the staying children under mode="wait" instead.
        // It removed the leaving child at once and put that one back where it stood.
        [Test]
        public void Given_AWaitModeRenderWithholdingTwoChildrenThatAnAbortStopped_When_ItsRemovalsArePutBack_Then_TheCommittedChildrenKeepTheirOrder()
        {
            // Arrange — "k" leaves, so mode="wait" withholds "x" and "y" until its exit completes
            s_mode = AnimatePresenceMode.Wait;
            s_children = tick => tick == 0
                ? new[] { Plain("x"), Plain("y"), Plain("k") }
                : new[] { Plain("x"), Plain("y") };
            s_abortAt = 1;
            using var mounted = Mount();

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(CommittedKeys(mounted), Is.EqualTo("x,y,k"));
        }

        [TestCase("Exiting")]
        [TestCase("PendingSettles")]
        public void Given_AWaitModeRenderWithholdingAKeyStillLeavingThatAnAbortStopped_When_ItsRemovalsArePutBack_Then_ThePresenceHoldsTheKeysRecordAgain(
            string record)
        {
            // Arrange — "x" and "k" leave with nothing to animate, and both exits wait on a passive flush nothing here
            // runs
            s_mode = AnimatePresenceMode.Wait;
            s_children = tick => tick switch
            {
                0 => new[] { Plain("x"), Plain("k") },
                1 => Array.Empty<VNode>(),
                _ => new[] { Plain("x") },
            };
            s_abortAt = 2;
            using var mounted = Mount();
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Act — "x" comes back while "k" is still leaving, so the render withholds it, and the boundary aborts it
            s_setTick.Invoke(2);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Holds(mounted, record, "x"), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base rendered a staying child under mode="wait" rather than withholding it.
        // Its initial: false record was never taken.
        [Test]
        public void Given_AWaitModeRenderWithholdingAChildMountedUnderInitialFalseThatAnAbortStopped_When_ItsRemovalsArePutBack_Then_ThePresenceHoldsItsInitialFalseRecordAgain()
        {
            // Arrange — "k" leaves, so mode="wait" withholds "x"
            s_mode = AnimatePresenceMode.Wait;
            s_initial = false;
            s_children = tick => tick == 0 ? new[] { Plain("x"), Plain("k") } : new[] { Plain("x") };
            s_abortAt = 1;
            using var mounted = Mount();

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Holds(mounted, "InitialBlocked", "x"), Is.True);
        }

        // GREEN_ON_BASE(characterization): the base recorded no anchor for a child it removed at once.
        // It rendered a key coming back under mode="wait" as a re-entry, which drops the key's anchor.
        [TestCase("drop", "a")]
        [TestCase("withhold", "x")]
        public void Given_ARenderTakingAKeyOutOfTheCommittedSet_When_ItCommits_Then_ThePresenceKeepsNoExitAnchorForTheKey(
            string how, string key)
        {
            // Arrange
            if (how == "drop")
            {
                // "a" leaves with nothing to animate, so its exit finishes in the passive flush and the next render drops it
                s_children = tick => tick == 0 ? new[] { Plain("a"), Plain("c") } : new[] { Plain("c") };
            }
            else
            {
                // "x" comes back while "k" is still leaving, so mode="wait" withholds it
                s_mode = AnimatePresenceMode.Wait;
                s_children = WithholdOnTheThirdRender;
            }
            using var mounted = Mount();
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Act
            s_setTick.Invoke(2);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Holds(mounted, "ExitAnchors", key), Is.False);
        }

        [Test]
        public void Given_AWaitModeRenderWithholdingAKeyStillLeaving_When_ItCommits_Then_ThePresenceKeepsNoMotionElementForTheKey()
        {
            // Arrange
            s_mode = AnimatePresenceMode.Wait;
            s_children = WithholdOnTheThirdRender;
            using var mounted = Mount();
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Act — "x" comes back while "k" is still leaving, so mode="wait" withholds it
            s_setTick.Invoke(2);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Holds(mounted, "MotionElements", "x"), Is.False);
        }

        [Test]
        public void Given_AWaitModeKeyWhoseExitFinished_When_ItComesBackWhileAnotherKeyIsLeaving_Then_ItsExitedNodeGoesBackToThePool()
        {
            // Arrange — "x" leaves with nothing to animate and finishes in the passive flush, while "k" plays an exit
            s_mode = AnimatePresenceMode.Wait;
            s_children = tick => tick switch
            {
                0 => new[] { Plain("x"), Fading("k") },
                1 => Array.Empty<VNode>(),
                _ => new[] { Plain("x") },
            };
            using var mounted = Mount();
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();
            var exited = Presence(mounted)?.Committed.FirstOrDefault(entry => entry.key == "x").node;

            // Act — mode="wait" withholds "x" while "k" is leaving
            s_setTick.Invoke(2);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(exited is ElementNode { Props: { } props } && !VNodePoolTestAccess.IsRentedOutForTest(props),
                Is.True);
        }

        // GREEN_ON_BASE(characterization): the base completed each removal with the callback its own render gave.
        // It removed the key at once each time.
        [Test]
        public void Given_AKeyRemovedAddedAndRemovedAgainBeforeThePassiveFlush_When_TheFlushRuns_Then_TheLastExitCompletesWithItsOwnRendersCallback()
        {
            // Arrange — "a" leaves with nothing to animate on every odd tick and is back on every even one
            s_children = tick => tick % 2 == 0 ? new[] { Plain("a") } : Array.Empty<VNode>();
            using var mounted = Mount();
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();
            s_setTick.Invoke(2);
            mounted.FlushStateForTest();
            s_setTick.Invoke(3);
            mounted.FlushStateForTest();

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_exitCallbacks.LastOrDefault(), Is.EqualTo(3));
        }

        // GREEN_ON_BASE(characterization): the base removed the child at once where its exit had nothing to animate.
        // No settle of that exit was left to reach the later one.
        [Test]
        public void Given_AKeyWhoseQuietExitItsReturnCutShort_When_ItLeavesAgainWithAnExitAndThePassiveFlushRuns_Then_TheRunningExitKeepsItOnScreen()
        {
            // Arrange — "a" leaves with nothing to animate, is back before the passive flush, and leaves again with an
            // exit no frame here plays out
            s_children = tick => tick switch
            {
                0 => new VNode[]
                {
                    V.Motion(key: "a", transition: StyleTransitionConfig.None, children: new VNode[] { V.Label(text: "a") }),
                },
                2 => new[] { Fading("a") },
                _ => Array.Empty<VNode>(),
            };
            using var mounted = Mount();
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();
            s_setTick.Invoke(2);
            mounted.FlushStateForTest();
            s_setTick.Invoke(3);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Act
            s_setTick.Invoke(4);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(_root.Query<Label>().ToList().Any(label => label.text == "a"), Is.True);
        }

        // Its descendant's exit is what the key's exit waits on, so cancelling it leaves the key's exit nothing to
        // settle on.
        [Test]
        public void Given_AWaitModeRenderWithholdingAKeyWhoseDescendantIsLeavingThatAnAbortStopped_When_ItsRemovalsArePutBack_Then_TheDescendantsExitIsStillRunning()
        {
            // Arrange — "x" and "k" leave with exits no frame here plays out, "x" through a descendant of its own
            s_mode = AnimatePresenceMode.Wait;
            s_children = tick => tick switch
            {
                0 => new[] { WithLeavingDescendant("x"), Fading("k") },
                1 => Array.Empty<VNode>(),
                _ => new[] { WithLeavingDescendant("x") },
            };
            s_abortAt = 2;
            using var mounted = Mount();
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Act — "x" comes back while "k" is still leaving, so the render withholds it, and the boundary aborts it
            s_setTick.Invoke(2);
            mounted.FlushStateForTest();

            // Assert
            var descendant = _root.Q<VisualElement>("x-descendant");
            Assert.That(descendant != null && mounted.Root.Reconciler.Context.StyleAnimationScheduler.IsExiting(descendant),
                Is.True);
        }

        [Test]
        public void Given_AnExitWaitingOnThePassiveFlush_When_TheTreeIsDisposed_Then_OnExitCompleteRunsFirst()
        {
            // Arrange — "a" leaves with nothing to animate, so its exit waits on a passive flush nothing here runs
            s_children = tick => tick == 0 ? new[] { Plain("a") } : Array.Empty<VNode>();
            var mounted = Mount();
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(s_exitCallbacks.Count, Is.EqualTo(1));
        }

        // Framer lets the exit animation of a presence unmounted with its tree run out and then calls onExitComplete;
        // the disposal cancels the animation, so it completes the exit itself.
        [Test]
        public void Given_AKeyWhoseExitIsPlaying_When_TheTreeIsDisposed_Then_OnExitCompleteRuns()
        {
            // Arrange — "a" plays an exit no frame here plays out
            s_children = tick => tick == 0 ? new[] { Fading("a") } : Array.Empty<VNode>();
            var mounted = Mount();
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(s_exitCallbacks.Count, Is.EqualTo(1));
        }

        // The teardown of the descendant's element settles the same wait the disposal already settled.
        [Test]
        public void Given_AKeyLeavingThroughADescendantsExit_When_TheTreeIsDisposed_Then_OnExitCompleteRunsOnce()
        {
            // Arrange — "x" leaves through a descendant's exit no frame here plays out
            s_children = tick => tick == 0 ? new[] { WithLeavingDescendant("x") } : Array.Empty<VNode>();
            var mounted = Mount();
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            mounted.Dispose();

            // Assert
            Assert.That(s_exitCallbacks.Count, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the base mounted the returning key afresh in that render and kept its node.
        [Test]
        public void Given_AKeyComingBackInARenderAnAbortStopped_When_ThatRenderEnds_Then_ItsCommittedNodeKeepsItsPropsBag()
        {
            // Arrange — "a" leaves with nothing to animate, and its exit waits on a passive flush nothing here runs
            s_children = tick => tick == 1 ? Array.Empty<VNode>() : new[] { Plain("a") };
            s_abortAt = 2;
            using var mounted = Mount();
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Act — "a" comes back while still leaving, and the boundary aborts that render
            s_setTick.Invoke(2);
            mounted.FlushStateForTest();

            // Assert
            var committed = Presence(mounted)?.Committed.FirstOrDefault(entry => entry.key == "a").node;
            Assert.That(committed is ElementNode { Props: { } props } && VNodePoolTestAccess.IsRentedOutForTest(props),
                Is.True);
        }

        private MountedTree Mount() => V.Mount(_root, V.Component(HostRender, key: "host"), CaughtErrors.Unlogged);

        private static VNode Plain(string key) => V.Label(text: key, key: key);

        // Its exit never plays out here: the root is on no panel, so the exit waits to attach to one.
        private static VNode Fading(string key) => V.Motion(key: key, children: new VNode[] { V.Label(text: key) });

        private static readonly Dictionary<string, MotionVariant> s_fade = new()
        {
            ["hidden"] = "opacity-0",
            ["visible"] = "opacity-100",
        };

        // A Motion with nothing of its own to exit, around one whose exit pose no frame here plays out.
        private static VNode WithLeavingDescendant(string key) => V.Motion(key: key, transition: StyleTransitionConfig.None,
            children: new VNode[]
            {
                V.Motion(name: key + "-descendant", variants: s_fade, animate: "visible", exit: "hidden",
                    transition: new StyleTransitionConfig { DurationSec = 1f }),
            });

        // "x" and "k" leave with exits no frame here plays out, and "x" alone comes back.
        private static VNode[] WithholdOnTheThirdRender(int tick) => tick switch
        {
            0 => new[] { Fading("x"), Fading("k") },
            1 => Array.Empty<VNode>(),
            _ => new[] { Fading("x") },
        };

        private static ReconcilerContext.PresenceBoundaryState Presence(MountedTree mounted)
            => mounted.Root.Reconciler.Context.PresenceStates.Values.FirstOrDefault();

        private static string CommittedKeys(MountedTree mounted)
            => string.Join(",", Presence(mounted)?.Committed.Select(entry => entry.key) ?? Enumerable.Empty<string>());

        // Read by name, so a record the presence does not keep reads as not holding the key.
        private static bool Holds(MountedTree mounted, string record, string key)
        {
            var state = Presence(mounted);
            if (state == null) return false;
            return typeof(ReconcilerContext.PresenceBoundaryState).GetField(record)?.GetValue(state) switch
            {
                IDictionary map => map.Contains(key),
                ICollection<string> set => set.Contains(key),
                _ => false,
            };
        }

        // The boundary aborts the pass of the first render at s_abortAt or later, after the presence has expanded. It
        // is rendered only where a case sets s_abortAt: until it aborts, its ref's cleanup throws on any removal,
        // the tree's disposal included.
        [Component(Compiler = false)]
        private static VNode HostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(children: new VNode[]
            {
                V.Component(PresenceOwnerRender, tick, key: "p"),
                s_abortAt == int.MaxValue
                    ? null
                    : V.Component(AbortingBoundaryRender, tick >= s_abortAt ? 1 : 0, key: "o"),
            });
        }

        [Component(Compiler = false)]
        private static VNode PresenceOwnerRender(int tick)
            => V.AnimatePresence(key: "p", mode: s_mode, initial: s_initial, onExitComplete: () => s_exitCallbacks.Add(tick),
                children: s_children(tick));

        // Its ref is removed by the render moving its tick off 0, and the cleanup fails: not a render error, so the
        // boundary catches on the aborting path.
        [Component(Compiler = false)]
        private static VNode RefCleanupFailsRender(int tick)
            => V.Label(text: "child",
                refCallback: tick == 0 ? _ => () => throw new InvalidOperationException("Ref cleanup throw") : null);

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode AbortingBoundaryRender(int tick)
        {
            Hooks.UseFallback(_ => V.Label(text: "o-fallback"));
            return V.Component(RefCleanupFailsRender, tick, key: "child");
        }
    }
}
