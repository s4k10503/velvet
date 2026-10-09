using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies how much of a pass an error boundary's catch of a render error replaces when the boundary
    /// renders inside another component's pass: the boundary's own output, and nothing else.
    /// <list type="bullet">
    /// <item>A sibling on either side of the boundary renders beside the fallback on mount, whether the two
    /// sit in an element or in a fragment, and its passive effect runs.</item>
    /// <item>On an update, a sibling behind the boundary renders what that update gave it.</item>
    /// <item>A component the failed output mounted never runs its effect, the boundary's own update included. One it had already mounted is
    /// cleaned up once, in the commit, after the rest of the pass has rendered.</item>
    /// <item>A boundary with no fallback passes the error on, and the boundary above replaces its own whole
    /// output with its fallback.</item>
    /// <item>The fallback never takes over an element of the boundary's output, one the failed output never
    /// reached included, so nothing that output wrote to it stays and a row of the boundary's own is replaced,
    /// while a sibling's element is kept. The failed output's
    /// pooled nodes go back to the pool, and a boundary catches again on a later update.</item>
    /// <item>Where the fallback's own content throws, it renders once, and the content's error goes to the
    /// boundary above whether that boundary catches in the walk or aborts its own render. The original error
    /// goes on after it where no boundary above caught the content's — to a boundary that declined the
    /// content's error, with the component stack starting at the component that threw, or to the log.</item>
    /// <item>What the failed output left behind goes with it: an AnimatePresence it rendered keeps no state
    /// of it, an enter it completed without playing reports no completion while one ahead of the boundary
    /// still reports its own, a Suspense fallback it showed is no longer recorded as shown, and a Portal target
    /// keeps none of the rows it inserted.</item>
    /// <item>An element callback's error below a boundary leaves no effect set up under that element.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class BoundaryCatchScopeTests
    {
        private static bool s_throws;
        private static int s_siblingEffects;
        private static int s_innerEffects;
        private static int s_innerCleanups;
        private static Action<int> s_setTick;

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_throws = false;
            s_siblingEffects = 0;
            s_innerEffects = 0;
            s_innerCleanups = 0;
            s_setTick = null;
            s_setOwnRecordTick = null;
            s_brokenFallbackRenders = 0;
            s_setOwnerTick = null;
            s_enterCompletions = 0;
            s_setOwnEnterTick = null;
            s_setSuspendingOwnTick = null;
            s_setOuterCatchTick = null;
            s_outerCatchFactoryRuns = 0;
            s_setOwnRowsTick = null;
            s_setSiblingCount = null;
            s_outerSaw = null;
            s_caughtStacks.Clear();
            s_target = new VisualElement { name = "target" };
            s_callbackSetups = 0;
            s_callbackCleanups = 0;
        }

        private string Texts() => string.Join(",", _root.Query<Label>().ToList().Select(label => label.text));

        #region A sibling of the boundary

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_ASiblingAheadOfABoundaryInAnElement_When_TheBoundaryCatchesOnMount_Then_TheSiblingRendersBesideTheFallback()
        {
            // Arrange
            s_throws = true;

            // Act
            using var mounted = V.Mount(_root, V.Component(SiblingAheadInElementRender, key: "host"), CaughtErrors.Unlogged);

            // Assert
            Assert.That(Texts(), Is.EqualTo("sibling,fallback"));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_ASiblingBehindABoundaryInAnElement_When_TheBoundaryCatchesOnMount_Then_TheSiblingRendersBesideTheFallback()
        {
            // Arrange
            s_throws = true;

            // Act
            using var mounted = V.Mount(_root, V.Component(SiblingBehindInElementRender, key: "host"), CaughtErrors.Unlogged);

            // Assert
            Assert.That(Texts(), Is.EqualTo("fallback,sibling"));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_ASiblingAheadOfABoundaryInAFragment_When_TheBoundaryCatchesOnMount_Then_TheSiblingRendersBesideTheFallback()
        {
            // Arrange
            s_throws = true;

            // Act
            using var mounted = V.Mount(_root, V.Component(SiblingAheadInFragmentRender, key: "host"), CaughtErrors.Unlogged);

            // Assert
            Assert.That(Texts(), Is.EqualTo("sibling,fallback"));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_ASiblingWithAPassiveEffectAheadOfABoundary_When_TheBoundaryCatchesOnMount_Then_TheEffectRunsOnce()
        {
            // Arrange
            s_throws = true;
            using var mounted = V.Mount(_root, V.Component(SiblingAheadInElementRender, key: "host"), CaughtErrors.Unlogged);

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_siblingEffects, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_ASiblingBehindABoundaryTakingItsParentsState_When_TheBoundaryCatchesInTheParentsUpdate_Then_TheSiblingRendersTheUpdate()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(UpdatingHostRender, key: "host"), CaughtErrors.Unlogged);
            s_throws = true;

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Texts(), Is.EqualTo("fallback,sibling:1"));
        }

        [Component(Compiler = false)]
        private static VNode ThrowerRender()
        {
            if (s_throws) throw new InvalidOperationException("Boundary catch scope throw");
            return V.Label(text: "child");
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode BoundaryRender()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Component(ThrowerRender, key: "thrower");
        }

        [Component(Compiler = false)]
        private static VNode SiblingRender()
        {
            Hooks.UseEffect(() =>
            {
                s_siblingEffects++;
                return (Action)null;
            }, Array.Empty<object>());
            return V.Label(text: "sibling");
        }

        [Component(Compiler = false)]
        private static VNode SiblingAheadInElementRender()
            => V.Div(children: new VNode[]
            {
                V.Component(SiblingRender, key: "sibling"),
                V.Component(BoundaryRender, key: "boundary"),
            });

        [Component(Compiler = false)]
        private static VNode SiblingBehindInElementRender()
            => V.Div(children: new VNode[]
            {
                V.Component(BoundaryRender, key: "boundary"),
                V.Component(SiblingRender, key: "sibling"),
            });

        [Component(Compiler = false)]
        private static VNode SiblingAheadInFragmentRender()
            => V.Fragment(new VNode[]
            {
                V.Component(SiblingRender, key: "sibling"),
                V.Component(BoundaryRender, key: "boundary"),
            });

        [Component(Compiler = false)]
        private static VNode TickedSiblingRender(int tick) => V.Label(text: "sibling:" + tick);

        [Component(Compiler = false)]
        private static VNode UpdatingHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(children: new VNode[]
            {
                V.Component(BoundaryRender, key: "boundary"),
                V.Component(TickedSiblingRender, tick, key: "sibling"),
            });
        }

        #endregion

        #region What the failed output leaves

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        // Before that, as characterization: the merge base sweeps this component in the fallback's own reconcile.
        // The case pins that the catch taken inside the walk disposes a component the failed output mounted.
        [Test]
        public void Given_AComponentMountedAheadOfTheThrowInsideTheBoundary_When_TheBoundaryCatches_Then_ItsEffectNeverRuns()
        {
            // Arrange
            s_throws = true;
            using var mounted = V.Mount(_root, V.Component(InnerHostRender, key: "host"), CaughtErrors.Unlogged);

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_innerEffects, Is.EqualTo(1 - 1));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_AComponentMountingAheadOfTheThrowInTheBoundarysOwnUpdate_When_TheBoundaryCatches_Then_ItsEffectNeverRuns()
        {
            // Arrange — the boundary's own state brings the component in, so the render that fails is the
            // boundary's own
            using var mounted = V.Mount(_root, V.Component(OwnUpdateHostRender, key: "host"), CaughtErrors.Unlogged);
            mounted.FlushEffectsForTest();
            s_throws = true;

            // Act
            s_setOwnBoundaryTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert — the fallback is read with the count, since a boundary that never caught runs the effect rightly
            Assert.That(Texts() + ", effects " + s_innerEffects, Is.EqualTo("fallback, effects 0"));
        }

        private static Action<int> s_setOwnBoundaryTick;

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode OwnUpdateBoundaryRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setOwnBoundaryTick = setTick;
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Fragment(new VNode[]
            {
                tick > 0 ? V.Component(InnerRender, key: "inner") : null,
                V.Component(ThrowerRender, key: "thrower"),
            });
        }

        [Component(Compiler = false)]
        private static VNode OwnUpdateHostRender() => V.Div(children: new VNode[] { V.Component(OwnUpdateBoundaryRender, key: "boundary") });

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        // Before that, as characterization: the merge base sweeps this component in the fallback's own reconcile.
        // The case pins that the catch taken inside the walk hands an already mounted one to the pass's sweep.
        [Test]
        public void Given_AComponentAheadOfTheThrowInsideTheBoundary_When_TheBoundaryCatchesOnAnUpdate_Then_ItsCleanupRunsOnce()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(InnerHostRender, key: "host"), CaughtErrors.Unlogged);
            mounted.FlushEffectsForTest();
            s_throws = true;

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_innerCleanups, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        // Before that, as characterization: the merge base stops the pass before the observer renders again.
        // The case pins that the catch taken inside the walk leaves the cleanup to the commit, as React does.
        [Test]
        public void Given_AComponentAheadOfTheThrowInsideTheBoundary_When_TheBoundaryCatchesOnAnUpdate_Then_ARenderLaterInThePassSeesNoCleanup()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(InnerHostRender, key: "host"), CaughtErrors.Unlogged);
            mounted.FlushEffectsForTest();
            s_throws = true;

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Texts(), Is.EqualTo("fallback,cleanups:0"));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_ABoundaryWithoutAFallbackInsideAnother_When_ItsChildThrowsOnMount_Then_TheOuterFallbackReplacesTheOutersWholeOutput()
        {
            // Arrange
            s_throws = true;

            // Act
            using var mounted = V.Mount(_root, V.Component(NestedHostRender, key: "host"), CaughtErrors.Unlogged);

            // Assert
            Assert.That(Texts(), Is.EqualTo("outer-fallback"));
        }

        [Component(Compiler = false)]
        private static VNode InnerRender()
        {
            Hooks.UseEffect(() =>
            {
                s_innerEffects++;
                return () => s_innerCleanups++;
            }, Array.Empty<object>());
            return V.Label(text: "inner");
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode InnerBoundaryRender()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Fragment(new VNode[]
            {
                V.Component(InnerRender, key: "inner"),
                V.Component(ThrowerRender, key: "thrower"),
            });
        }

        [Component(Compiler = false)]
        private static VNode CleanupObserverRender(int tick) => V.Label(text: "cleanups:" + s_innerCleanups);

        [Component(Compiler = false)]
        private static VNode InnerHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(children: new VNode[]
            {
                V.Component(InnerBoundaryRender, key: "boundary"),
                V.Component(CleanupObserverRender, tick, key: "observer"),
            });
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode BoundaryWithoutFallbackRender() => V.Component(ThrowerRender, key: "thrower");

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode OuterBoundaryRender()
        {
            Hooks.UseFallback(_ => V.Label(text: "outer-fallback"));
            return V.Fragment(new VNode[]
            {
                V.Component(BoundaryWithoutFallbackRender, key: "inner"),
                V.Label(text: "outer-sibling"),
            });
        }

        [Component(Compiler = false)]
        private static VNode NestedHostRender() => V.Div(children: new VNode[] { V.Component(OuterBoundaryRender, key: "outer") });

        #endregion
        #region The fallback and what it replaces

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        // Before that, as characterization: the merge base diffs the fallback against the failed output's rows.
        // The catch taken in the walk builds the fallback afresh instead, and that it writes the row's name is what this pins.
        [Test]
        public void Given_AFailedOutputThatPatchedItsRow_When_TheBoundaryCatchesOnAnUpdate_Then_TheFallbackRowCarriesOnlyItsOwnProps()
        {
            // Arrange — the failed output renames the row the fallback also names "row", then throws
            using var mounted = V.Mount(_root, V.Component(RowHostRender, key: "host"), CaughtErrors.Unlogged);
            s_throws = true;

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                _root.Query<Label>().ToList().Where(label => label.text == "fallback").Select(label => label.name)
                    .FirstOrDefault(),
                Is.EqualTo("row"));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_AFallbackRowAtThePositionOfARowTheFailedOutputPatched_When_TheBoundaryCatchesOnAnUpdate_Then_TheFallbackRowIsAnotherElement()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SamePositionHostRender, key: "host"), CaughtErrors.Unlogged);
            var rowBefore = _root.Query<Label>().ToList().FirstOrDefault(label => label.text == "t0");
            s_throws = true;

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the row is read as found, since a row never found differs from every element
            var fallbackRow = _root.Query<Label>().ToList().FirstOrDefault(label => label.text == "fallback");
            Assert.That((rowBefore != null, fallbackRow != null && !ReferenceEquals(fallbackRow, rowBefore)),
                Is.EqualTo((true, true)));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        // Before that, as characterization: the merge base shows the fallback in a reconcile of its own, whose
        // key set holds none of the failed output's keys. What this pins is that the catch taken in the walk
        // takes the failed output's keys back before the fallback commits.
        [Test]
        public void Given_AFallbackRowKeyedLikeARowTheFailedOutputCommitted_When_TheBoundaryCatchesOnAnUpdate_Then_NoDuplicateKeyIsReported()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SamePositionHostRender, key: "host"), CaughtErrors.Unlogged);
            s_throws = true;
            var duplicateWarnings = 0;
            void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Warning && condition.Contains("Duplicate key detected")) duplicateWarnings++;
            }

            // Act
            Application.logMessageReceived += OnLog;
            try
            {
                s_setTick.Invoke(1);
                mounted.FlushStateForTest();
            }
            finally
            {
                Application.logMessageReceived -= OnLog;
            }

            // Assert — the fallback is read beside the count, since a pass that never caught reports nothing either
            Assert.That((Texts(), duplicateWarnings), Is.EqualTo(("fallback", 0)));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_AFallbackRowKeyedLikeARowTheFailedOutputNeverReached_When_TheBoundaryCatchesOnAnUpdate_Then_TheFallbackRowIsAnotherElement()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SameKeyHostRender, key: "host"), CaughtErrors.Unlogged);
            var rowBefore = _root.Query<Label>().ToList().FirstOrDefault(label => label.text == "t0");
            s_throws = true;

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the row is read as found, since a row never found differs from every element
            var fallbackRow = _root.Query<Label>().ToList().FirstOrDefault(label => label.text == "fallback");
            Assert.That((rowBefore != null, fallbackRow != null && !ReferenceEquals(fallbackRow, rowBefore)),
                Is.EqualTo((true, true)));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        // Before that, as characterization: the merge base stops the pass short of this sibling and leaves it alone.
        // The catch taken in the walk patches it in place, and that its element is kept is what this pins.
        [Test]
        public void Given_ASiblingBehindABoundary_When_TheBoundaryCatchesOnAnUpdate_Then_TheSiblingKeepsItsElement()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(UpdatingHostRender, key: "host"), CaughtErrors.Unlogged);
            var siblingBefore = _root.Query<Label>().ToList().FirstOrDefault(label => label.text == "sibling:0");
            s_throws = true;

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(
                ReferenceEquals(_root.Query<Label>().ToList().FirstOrDefault(label => label.text.StartsWith("sibling")),
                    siblingBefore) && siblingBefore != null,
                Is.True);
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        // Before that, as characterization: the merge base retires the failed tree in the fallback's own reconcile.
        // The catch taken in the walk retires it there instead, which this pins.
        [Test]
        public void Given_AFailedOutputHoldingPooledNodes_When_TheBoundaryCatchesOnAnUpdateAndTheMountIsDisposed_Then_ThePoolCountsNothingRentedOut()
        {
            // Arrange
            var before = Rented();
            var mounted = V.Mount(_root, V.Component(PooledHostRender, key: "host"), CaughtErrors.Unlogged);
            s_throws = true;
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            mounted.Dispose();

            // Assert
            var after = Rented();
            Assert.That(
                (after.Props - before.Props, after.EventArrays - before.EventArrays, after.NodeArrays - before.NodeArrays),
                Is.EqualTo((0, 0, 0)));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_ABoundaryThatCaughtInItsParentsUpdate_When_TheParentUpdatesAgain_Then_TheBoundaryKeepsItsFallbackAndTheSiblingUpdates()
        {
            // Arrange — the child no longer throws, so a boundary that rendered it again would show it
            using var mounted = V.Mount(_root, V.Component(UpdatingHostRender, key: "host"), CaughtErrors.Unlogged);
            s_throws = true;
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();
            s_throws = false;

            // Act
            s_setTick.Invoke(2);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Texts(), Is.EqualTo("fallback,sibling:2"));
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode RowBoundaryRender(int tick)
        {
            Hooks.UseFallback(_ => V.Fragment(new VNode[] { V.Label(name: "row", text: "fallback") }));
            return V.Fragment(new VNode[]
            {
                V.Label(name: tick == 0 ? "row" : "failed", text: "t" + tick),
                V.Component(ThrowerRender, key: "thrower"),
            });
        }

        [Component(Compiler = false)]
        private static VNode RowHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(children: new VNode[] { V.Component(RowBoundaryRender, tick, key: "boundary") });
        }

        // The fallback row is unwrapped, so it carries the key of the row the failed output patches first.
        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode SamePositionBoundaryRender(int tick)
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Fragment(new VNode[]
            {
                V.Label(text: "t" + tick),
                V.Component(ThrowerRender, key: "thrower"),
            });
        }

        [Component(Compiler = false)]
        private static VNode SamePositionHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(children: new VNode[] { V.Component(SamePositionBoundaryRender, tick, key: "boundary") });
        }

        // The thrower comes first, so the failed output reaches none of the rows, and the fallback row at
        // another position carries the key of the row behind it.
        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode SameKeyBoundaryRender(int tick)
        {
            Hooks.UseFallback(_ => V.Label(key: "row", text: "fallback"));
            return V.Fragment(new VNode[]
            {
                V.Component(ThrowerRender, key: "thrower"),
                V.Label(key: "row", text: "t" + tick),
            });
        }

        [Component(Compiler = false)]
        private static VNode SameKeyHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(children: new VNode[] { V.Component(SameKeyBoundaryRender, tick, key: "boundary") });
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode PooledBoundaryRender(int tick)
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Fragment(new VNode[]
            {
                V.Div(children: V.List(new[] { "open" }, id => id, id => V.Button(text: id, onClick: () => { }))),
                V.Component(ThrowerRender, key: "thrower"),
            });
        }

        [Component(Compiler = false)]
        private static VNode PooledHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(children: new VNode[] { V.Component(PooledBoundaryRender, tick, key: "boundary") });
        }

        // Read by reflection for the reason MemoCacheCollisionTests reads the same sets.
        private static (int Props, int EventArrays, int NodeArrays) Rented()
            => VNodePoolTestAccess.RentedOutCountsForTest();

        #endregion

        #region A fallback whose own content throws

        private static int s_brokenFallbackRenders;
        private static Action<int> s_setOwnerTick;

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_AFallbackWhoseContentThrowsInsideABoundaryCatchingInTheWalk_When_ItsChildThrowsOnMount_Then_TheOuterFallbackShowsAndTheContentRendersOnce()
        {
            // Arrange
            s_throws = true;

            // Act
            using var mounted = V.Mount(_root, V.Component(BrokenNestedHostRender, key: "host"), CaughtErrors.Unlogged);

            // Assert — the render count is folded in, since a fallback attempted twice shows the outer one too
            Assert.That((Texts(), s_brokenFallbackRenders), Is.EqualTo(("outer-fallback", 1)));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        // Before that, as characterization: the merge base takes both errors on the aborting path to the same end.
        // The catch taken in the walk leaves the original error with the boundary the aborting path disposed, which this pins.
        [Test]
        public void Given_AFallbackWhoseContentThrowsUnderABoundaryRenderingItself_When_TheInnerChildThrows_Then_TheOwnersFallbackShowsWithNothingLogged()
        {
            // Arrange — the owner is the boundary whose own update is the pass, so it catches on the aborting path
            using var mounted = V.Mount(_root, V.Component(OwnerBoundaryRender, key: "owner"), CaughtErrors.Unlogged);
            s_throws = true;

            // Act
            s_setOwnerTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — an uncaught error logged on the way fails the case as well
            Assert.That(Texts(), Is.EqualTo("owner-fallback"));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        // Before that, as characterization: the merge base logs both errors on the aborting path.
        // The catch taken in the walk passes the original error on after the fallback's own, which this pins.
        [Test]
        public void Given_AFallbackWhoseContentThrowsWithNoBoundaryAbove_When_ItsChildThrowsOnMount_Then_TheOriginalErrorIsLoggedToo()
        {
            // Arrange
            s_throws = true;
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: Broken fallback content");
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: Boundary catch scope throw");

            // Act
            using var mounted = V.Mount(_root, V.Component(BrokenAloneHostRender, key: "host"), CaughtErrors.Unlogged);

            // Assert
            LogAssert.NoUnexpectedReceived();
        }

        [Component(Compiler = false)]
        private static VNode BrokenFallbackContentRender()
        {
            s_brokenFallbackRenders++;
            if (s_throws) throw new InvalidOperationException("Broken fallback content");
            return V.Label(text: "broken-content");
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode BrokenFallbackBoundaryRender()
        {
            Hooks.UseFallback(_ => V.Component(BrokenFallbackContentRender, key: "content"));
            return V.Component(ThrowerRender, key: "thrower");
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode OuterOfBrokenRender()
        {
            Hooks.UseFallback(_ => V.Label(text: "outer-fallback"));
            return V.Component(BrokenFallbackBoundaryRender, key: "inner");
        }

        [Component(Compiler = false)]
        private static VNode BrokenNestedHostRender() => V.Div(children: new VNode[] { V.Component(OuterOfBrokenRender, key: "outer") });

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode OwnerBoundaryRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setOwnerTick = setTick;
            Hooks.UseFallback(_ => V.Label(text: "owner-fallback"));
            return V.Fragment(new VNode[] { V.Label(text: "owner:" + tick), V.Component(BrokenFallbackBoundaryRender, key: "inner") });
        }

        [Component(Compiler = false)]
        private static VNode BrokenAloneHostRender()
            => V.Div(children: new VNode[] { V.Component(BrokenFallbackBoundaryRender, key: "boundary") });

        #endregion
        #region What the failed output leaves behind

        private static int s_enterCompletions;
        private static VisualElement s_target;

        private static readonly System.Collections.Generic.Dictionary<string, MotionVariant> s_fade = new()
        {
            ["visible"] = "opacity-100",
            ["hidden"] = "opacity-0",
        };

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_ASuspenseInTheFailedOutputOfTheBoundarysOwnUpdate_When_TheBoundaryCatches_Then_NoSuspenseFallbackIsRecordedAsShown()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(OwnSuspenseHostRender, key: "host"), CaughtErrors.Unlogged);
            s_throws = true;

            // Act
            s_setOwnRecordTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the screen is read beside the record, since a boundary that never caught records its
            // Suspense too
            Assert.That(Texts() + "|" + mounted.Root.Reconciler.Context.AnyBoundaryShowingFallback,
                Is.EqualTo("outside,fallback|False"));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_APresenceInTheFailedOutputOfTheBoundarysOwnUpdate_When_TheBoundaryCatches_Then_NoStateOfItIsKept()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(NewPresenceHostRender, key: "host"), CaughtErrors.Unlogged);
            s_throws = true;

            // Act
            s_setOwnRecordTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the screen is read beside the count, since a boundary that never caught keeps its
            // presence's state too
            Assert.That(Texts() + "|" + mounted.Root.Reconciler.Context.PresenceStates.Count,
                Is.EqualTo("outside,fallback|0"));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_APresenceTheHostRendersBesideTheBoundary_When_TheBoundarysOwnUpdateFailsAndIsCaught_Then_TheHostsPresenceKeepsItsState()
        {
            // Arrange — the boundary's failed render adds a presence of its own, which the catch drops
            using var mounted = V.Mount(_root, V.Component(HostPresenceHostRender, key: "host"), CaughtErrors.Unlogged);
            s_throws = true;

            // Act
            s_setOwnRecordTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the fallback is read beside the count, since a boundary that never caught keeps its own
            // presence's state as well
            Assert.That(Texts() + "|" + mounted.Root.Reconciler.Context.PresenceStates.Count,
                Is.EqualTo("outside,fallback|1"));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_ABoundaryWithAPresenceWhoseOwnUpdateAddsASecondAndFails_When_TheBoundaryCatches_Then_TheFallbackReplacesTheFirstAndNeitherStateIsKept()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TwoPresenceHostRender, key: "host"), CaughtErrors.Unlogged);
            s_throws = true;

            // Act
            s_setOwnRecordTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            var items = _root.Q("host").Children().Count(child => child.name.StartsWith("item-"));
            Assert.That(Texts() + "|" + items + "|" + mounted.Root.Reconciler.Context.PresenceStates.Count,
                Is.EqualTo("outside,fallback|0|0"));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_ABoundaryShowingItsOwnSuspensesFallback_When_ItsOwnUpdateThrows_Then_TheFallbackReplacesThoseRowsAndNothingIsRecordedAsShown()
        {
            // Arrange — the boundary's committed output is its Suspense showing its own fallback
            using var mounted = V.Mount(_root, V.Component(ShownSuspenseHostRender, key: "host"), CaughtErrors.Unlogged);
            var before = Texts();
            s_throws = true;

            // Act
            s_setOwnRecordTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(before + "|" + Texts() + "|" + mounted.Root.Reconciler.Context.AnyBoundaryShowingFallback,
                Is.EqualTo("outside,loading|outside,fallback|False"));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_APresenceInTheBoundarysOwnUpdate_When_TheBoundaryCatches_Then_ItsChildrenLeaveAndNoStateOfItIsKept()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(OwnPresenceHostRender, key: "host"), CaughtErrors.Unlogged);
            s_throws = true;

            // Act
            s_setOwnRecordTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            var items = _root.Q("host").Children().Count(child => child.name.StartsWith("item-"));
            Assert.That(Texts() + "|" + items + "|" + mounted.Root.Reconciler.Context.PresenceStates.Count,
                Is.EqualTo("outside,fallback|0|0"));
        }

        private static Action<int> s_setOwnRecordTick;

        // Renders no Suspense until its own update, whose render is the one that fails.
        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode OwnSuspenseBoundaryRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setOwnRecordTick = setTick;
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return tick == 0
                ? V.Label(text: "ok")
                : V.Fragment(new VNode[]
                {
                    V.Suspense(
                        fallback: V.Label(text: "loading"),
                        children: new VNode[] { V.Component(PendingReaderRender, key: "reader") }),
                    V.Component(ThrowerRender, key: "thrower"),
                });
        }

        [Component(Compiler = false)]
        private static VNode OwnSuspenseHostRender()
            => V.Div(children: new VNode[]
            {
                V.Label(text: "outside"),
                V.Component(OwnSuspenseBoundaryRender, key: "boundary"),
            });

        // Renders no AnimatePresence until its own update, whose render is the one that fails.
        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode NewPresenceBoundaryRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setOwnRecordTick = setTick;
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return tick == 0
                ? V.Label(text: "ok")
                : V.Fragment(new VNode[]
                {
                    V.AnimatePresence(key: "presence", children: new VNode[]
                    {
                        V.Motion(name: "item-a", key: "a", variants: s_fade, animate: "visible", exit: "hidden",
                            transition: new StyleTransitionConfig { DurationSec = 0.3f }),
                    }),
                    V.Component(ThrowerRender, key: "thrower"),
                });
        }

        [Component(Compiler = false)]
        private static VNode NewPresenceHostRender()
            => V.Div(children: new VNode[]
            {
                V.Label(text: "outside"),
                V.Component(NewPresenceBoundaryRender, key: "boundary"),
            });

        [Component(Compiler = false)]
        private static VNode HostPresenceHostRender()
            => V.Div(children: new VNode[]
            {
                V.AnimatePresence(key: "host-presence", children: new VNode[]
                {
                    V.Motion(name: "host-item", key: "h", variants: s_fade, animate: "visible", exit: "hidden",
                        transition: new StyleTransitionConfig { DurationSec = 0.3f }),
                }),
                V.Label(text: "outside"),
                V.Component(NewPresenceBoundaryRender, key: "boundary"),
            });

        // Keeps its first presence across its own update, whose render adds a second and then fails.
        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode TwoPresenceBoundaryRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setOwnRecordTick = setTick;
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            var first = V.AnimatePresence(key: "first", children: new VNode[]
            {
                V.Motion(name: "item-a", key: "a", variants: s_fade, animate: "visible", exit: "hidden",
                    transition: new StyleTransitionConfig { DurationSec = 0.3f }),
            });
            return tick == 0
                ? V.Fragment(new VNode[] { first })
                : V.Fragment(new VNode[]
                {
                    first,
                    V.AnimatePresence(key: "second", children: new VNode[]
                    {
                        V.Motion(name: "item-b", key: "b", variants: s_fade, animate: "visible", exit: "hidden",
                            transition: new StyleTransitionConfig { DurationSec = 0.3f }),
                    }),
                    V.Component(ThrowerRender, key: "thrower"),
                });
        }

        [Component(Compiler = false)]
        private static VNode TwoPresenceHostRender()
            => V.Div(name: "host", children: new VNode[]
            {
                V.Label(text: "outside"),
                V.Component(TwoPresenceBoundaryRender, key: "boundary"),
            });

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode ShownSuspenseBoundaryRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setOwnRecordTick = setTick;
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Fragment(new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[] { V.Component(PendingReaderRender, key: "reader") }),
                tick == 0 ? null : V.Component(ThrowerRender, key: "thrower"),
            });
        }

        [Component(Compiler = false)]
        private static VNode ShownSuspenseHostRender()
            => V.Div(children: new VNode[]
            {
                V.Label(text: "outside"),
                V.Component(ShownSuspenseBoundaryRender, key: "boundary"),
            });

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode OwnPresenceBoundaryRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setOwnRecordTick = setTick;
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            var key = tick == 0 ? "a" : "b";
            return V.Fragment(new VNode[]
            {
                V.AnimatePresence(key: "presence", children: new VNode[]
                {
                    V.Motion(name: "item-" + key, key: key, variants: s_fade, animate: "visible", exit: "hidden",
                        transition: new StyleTransitionConfig { DurationSec = 0.3f }),
                }),
                tick == 0 ? null : V.Component(ThrowerRender, key: "thrower"),
            });
        }

        [Component(Compiler = false)]
        private static VNode OwnPresenceHostRender()
            => V.Div(name: "host", children: new VNode[]
            {
                V.Label(text: "outside"),
                V.Component(OwnPresenceBoundaryRender, key: "boundary"),
            });

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        // Before that, as characterization: the merge base's catch in the walk prunes the presence's state too. The case
        // reads it on the mount alone because the boundary no longer renders its children on its parent's next
        // render, which the form it replaces went through.
        [Test]
        public void Given_APresenceInAFailedOutput_When_TheBoundaryCatchesOnMount_Then_NoStateOfItIsKept()
        {
            // Arrange
            s_throws = true;

            // Act
            using var mounted = V.Mount(_root, V.Component(GhostHostRender, key: "host"), CaughtErrors.Unlogged);

            // Assert — the fallback is read with the count, since a mount that rendered nothing records nothing
            Assert.That(Texts() + ", states " + mounted.Root.Reconciler.Context.PresenceStates.Count,
                Is.EqualTo("fallback, states 0"));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        // Before that, as characterization: the merge base takes this catch on the aborting path, which drops the enters
        // the failed render queued too. What this pins is that the catch the boundary's own reconcile takes drops them.
        [Test]
        public void Given_AnEnterWithNothingToPlayInTheFailedRenderOfTheBoundarysOwnUpdate_When_TheBoundaryCatches_Then_ItsCompletionNeverRuns()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(OwnUpdateEnterHostRender, key: "host"), CaughtErrors.Unlogged);
            s_throws = true;

            // Act
            s_setOwnEnterTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the fallback is read with the count, since an update that never rendered the presence
            // completes nothing either
            Assert.That(Texts() + ",enters " + s_enterCompletions, Is.EqualTo("fallback,enters 0"));
        }

        private static Action<int> s_setOwnEnterTick;

        // Renders the presence only from its own update on, so the render that queues the enter is that update's.
        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode OwnUpdateEnterBoundaryRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setOwnEnterTick = setTick;
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return tick == 0
                ? V.Label(text: "ok")
                : V.Fragment(new VNode[]
                {
                    V.AnimatePresence(key: "presence", initial: false, children: new VNode[]
                    {
                        V.Motion(name: "item-a", key: "a", transition: StyleTransition.Fade,
                            onEnterComplete: () => s_enterCompletions++),
                    }),
                    V.Component(ThrowerRender, key: "thrower"),
                });
        }

        [Component(Compiler = false)]
        private static VNode OwnUpdateEnterHostRender()
            => V.Div(children: new VNode[] { V.Component(OwnUpdateEnterBoundaryRender, key: "boundary") });

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        // Before that, as characterization: the merge base takes this catch on the aborting path. What this pins is
        // that the catch the boundary's own reconcile takes leaves the sibling on its own row as well.
        [Test]
        public void Given_AnInlineSiblingBehindABoundaryWhoseOwnUpdateInsertedARowBeforeFailing_When_TheSiblingUpdates_Then_ItRewritesOnlyItsOwnRow()
        {
            // Arrange — the failed update's leaf list takes the fast path and inserts a row before the throw
            using var mounted = V.Mount(_root, V.Component(RowsHostRender, key: "host"), CaughtErrors.Unlogged);
            s_throws = true;
            s_setOwnRowsTick.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            s_setSiblingCount.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the fallback is read beside the sibling's rows, since a boundary that never caught leaves
            // the sibling one row too
            var labels = Texts().Split(',');
            Assert.That(labels.Contains("fallback") + "|" + string.Join(",", labels.Where(text => text.StartsWith("s:"))),
                Is.EqualTo("True|s:1"));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        // Before that, as characterization: the merge base takes this catch on the aborting path. What this pins is
        // that the catch the boundary's own reconcile takes leaves the sibling on its own row as well.
        [Test]
        public void Given_AnInlineSiblingBehindABoundaryWhoseOwnUpdateMountsAPortalWhoseContentThrows_When_TheSiblingUpdates_Then_ItRewritesOnlyItsOwnRow()
        {
            // Arrange — the Portal's content mounts in the drain that ends the boundary's own pass
            using var mounted = V.Mount(_root, V.Component(PortalRowsHostRender, key: "host"), CaughtErrors.Unlogged);
            s_throws = true;
            s_setOwnRowsTick.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            s_setSiblingCount.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — the fallback is read beside the sibling's rows, since a boundary that never caught leaves
            // the sibling one row too
            var labels = Texts().Split(',');
            Assert.That(labels.Contains("fallback") + "|" + string.Join(",", labels.Where(text => text.StartsWith("s:"))),
                Is.EqualTo("True|s:1"));
        }

        private static Action<int> s_setOwnRowsTick;
        private static Action<int> s_setSiblingCount;

        [Component(Compiler = false)]
        private static VNode RowsSiblingRender()
        {
            var (count, setCount) = Hooks.UseState(0);
            s_setSiblingCount = setCount;
            return V.Label(text: "s:" + count);
        }

        // Its output is host leaves only, so its own reconcile takes the fast path.
        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode RowsBoundaryRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setOwnRowsTick = setTick;
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return tick == 0
                ? V.Fragment(new VNode[] { V.Label(text: "a") })
                : V.Fragment(new VNode[]
                {
                    V.Label(text: "a"),
                    V.Label(text: "b"),
                    V.Div(children: new VNode[] { V.Component(ThrowerRender, key: "thrower") }),
                });
        }

        [Component(Compiler = false)]
        private static VNode RowsHostRender()
            => V.Div(children: new VNode[]
            {
                V.Component(RowsBoundaryRender, key: "boundary"),
                V.Component(RowsSiblingRender, key: "sibling"),
            });

        // Its first row is the same Button before and after the update, since the fallback is diffed against the rows
        // before the update.
        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode PortalRowsBoundaryRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setOwnRowsTick = setTick;
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return tick == 0
                ? V.Button(text: "open", onClick: () => { })
                : V.Fragment(new VNode[]
                {
                    V.Button(text: "open", onClick: () => { }),
                    V.Portal(s_target, new VNode[] { V.Component(ThrowerRender, key: "dialog") }),
                });
        }

        [Component(Compiler = false)]
        private static VNode PortalRowsHostRender()
            => V.Div(children: new VNode[]
            {
                V.Component(PortalRowsBoundaryRender, key: "boundary"),
                V.Component(RowsSiblingRender, key: "sibling"),
            });

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        // Before that, as characterization: the merge base records the Suspense this update suspends as well. What
        // this pins is that the catch armed on the boundary's own reconcile puts no record back when it caught nothing.
        [Test]
        public void Given_ABoundaryWhoseOwnUpdateSuspendsItsSuspenseWithoutThrowing_When_ThatUpdateCommits_Then_TheSuspenseIsRecordedAsShowingItsFallback()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SuspendingOwnUpdateHostRender, key: "host"), CaughtErrors.Unlogged);

            // Act
            s_setSuspendingOwnTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Texts() + "|" + mounted.Root.Reconciler.Context.AnyBoundaryShowingFallback,
                Is.EqualTo("loading|True"));
        }

        private static Action<int> s_setSuspendingOwnTick;

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode SuspendingOwnUpdateBoundaryRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setSuspendingOwnTick = setTick;
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[] { tick == 0 ? V.Label(text: "ready") : V.Component(PendingReaderRender, key: "reader") });
        }

        [Component(Compiler = false)]
        private static VNode SuspendingOwnUpdateHostRender()
            => V.Div(children: new VNode[] { V.Component(SuspendingOwnUpdateBoundaryRender, key: "boundary") });

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_AnEnterWithNothingToPlayInAFailedOutput_When_TheBoundaryCatchesOnMount_Then_ItsCompletionNeverRuns()
        {
            // Arrange
            s_throws = true;

            // Act
            using var mounted = V.Mount(_root, V.Component(EnterHostRender, key: "host"), CaughtErrors.Unlogged);

            // Assert — the fallback is read with the count, since a mount that rendered nothing completes nothing
            Assert.That(Texts() + ",enters " + s_enterCompletions, Is.EqualTo("fallback,enters 0"));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        // Before that, as characterization: the merge base completes this enter inside the walk of a render that commits.
        // The completion now waits for the end of the pass, and that it still runs there is what this pins.
        [Test]
        public void Given_AnEnterWithNothingToPlayInAnOutputThatCommits_When_ItMounts_Then_ItsCompletionRunsOnce()
        {
            // Arrange
            s_throws = false;

            // Act
            using var mounted = V.Mount(_root, V.Component(EnterHostRender, key: "host"), CaughtErrors.Unlogged);

            // Assert
            Assert.That(s_enterCompletions, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_AnEnterWithNothingToPlayAheadOfABoundary_When_TheBoundaryCatchesOnMount_Then_ThatEnterCompletesOnce()
        {
            // Arrange
            s_throws = true;

            // Act
            using var mounted = V.Mount(_root, V.Component(EnterAheadHostRender, key: "host"), CaughtErrors.Unlogged);

            // Assert — the fallback is read with the count, since a boundary that never caught takes nothing back
            Assert.That(Texts() + ",enters " + s_enterCompletions, Is.EqualTo("fallback,enters 1"));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_AnInheritingEnterBlockedInAFailedOutput_When_TheBoundaryCatchesOnMount_Then_ItsCompletionNeverRuns()
        {
            // Arrange
            s_throws = true;

            // Act
            using var mounted = V.Mount(
                _root, V.Component(InheritedEnterHostRender, key: "host"), CaughtErrors.Unlogged);

            // Assert — the fallback is read with the count, since a mount that rendered nothing completes nothing
            Assert.That(Texts() + ",enters " + s_enterCompletions, Is.EqualTo("fallback,enters 0"));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        // Before that, as characterization: the merge base completes this enter inside the walk of a render that commits.
        // The completion now waits for the end of the pass, and that it still runs there is what this pins.
        [Test]
        public void Given_AnInheritingEnterBlockedInAnOutputThatCommits_When_ItMounts_Then_ItsCompletionRunsOnce()
        {
            // Arrange
            s_throws = false;

            // Act
            using var mounted = V.Mount(
                _root, V.Component(InheritedEnterHostRender, key: "host"), CaughtErrors.Unlogged);

            // Assert
            Assert.That(s_enterCompletions, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_ASuspenseShowingItsFallbackInAFailedOutput_When_TheBoundaryCatchesOnMount_Then_NoSuspenseFallbackIsRecordedAsShown()
        {
            // Arrange
            s_throws = true;

            // Act
            using var mounted = V.Mount(
                _root, V.Component(SuspenseFailedHostRender, key: "host"), CaughtErrors.Unlogged);

            // Assert — the screen is read beside the record, since a mount that rendered nothing records nothing
            Assert.That(Texts() + "|" + mounted.Root.Reconciler.Context.AnyBoundaryShowingFallback,
                Is.EqualTo("fallback|False"));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_APortalWhoseChildrenGrowAheadOfAThrow_When_TheBoundaryCatchesOnAnUpdate_Then_TheTargetKeepsNothing()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(PortalHostRender, key: "host"), CaughtErrors.Unlogged);
            var before = string.Join(",", s_target.Children().Select(child => child.name));
            s_throws = true;

            // Act
            s_setTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(before + "|" + string.Join(",", s_target.Children().Select(child => child.name)) + "|" + Texts(),
                Is.EqualTo("x||fallback"));
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode GhostBoundaryRender(int tick)
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            var key = tick == 0 ? "a" : "b";
            return V.Fragment(new VNode[]
            {
                V.AnimatePresence(key: "presence", children: new VNode[]
                {
                    V.Motion(name: "item-" + key, key: key, variants: s_fade, animate: "visible", exit: "hidden",
                        transition: new StyleTransitionConfig { DurationSec = 0.3f }),
                }),
                V.Component(ThrowerRender, key: "thrower"),
            });
        }

        [Component(Compiler = false)]
        private static VNode GhostHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(name: "host", children: new VNode[] { V.Component(GhostBoundaryRender, tick, key: "boundary") });
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode EnterBoundaryRender()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Fragment(new VNode[]
            {
                V.AnimatePresence(key: "presence", initial: false, children: new VNode[]
                {
                    V.Motion(name: "item-a", key: "a", transition: StyleTransition.Fade,
                        onEnterComplete: () => s_enterCompletions++),
                }),
                V.Component(ThrowerRender, key: "thrower"),
            });
        }

        // The presence's child names no animate of its own, so the enter it inherits from the Motion above is the
        // one the element's creation resolves and, under initial: false, blocks.
        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode InheritedEnterBoundaryRender()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Fragment(new VNode[]
            {
                V.Motion(variants: s_fade, initial: "hidden", animate: "visible", children: new VNode[]
                {
                    V.AnimatePresence(key: "presence", initial: false, children: new VNode[]
                    {
                        V.Motion(key: "a", variants: s_fade, onEnterComplete: () => s_enterCompletions++),
                    }),
                }),
                V.Component(ThrowerRender, key: "thrower"),
            });
        }

        [Component(Compiler = false)]
        private static VNode InheritedEnterHostRender()
            => V.Div(children: new VNode[] { V.Component(InheritedEnterBoundaryRender, key: "boundary") });

        [Component(Compiler = false)]
        private static VNode EnterAheadHostRender()
            => V.Div(children: new VNode[]
            {
                V.AnimatePresence(key: "presence", initial: false, children: new VNode[]
                {
                    V.Motion(name: "item-a", key: "a", transition: StyleTransition.Fade,
                        onEnterComplete: () => s_enterCompletions++),
                }),
                V.Component(BoundaryRender, key: "boundary"),
            });

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode SuspenseFailedBoundaryRender()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Fragment(new VNode[]
            {
                V.Suspense(
                    fallback: V.Label(text: "loading"),
                    children: new VNode[] { V.Component(PendingReaderRender, key: "reader") }),
                V.Component(ThrowerRender, key: "thrower"),
            });
        }

        [Component(Compiler = false)]
        private static VNode PendingReaderRender()
        {
            var value = Hooks.Use<int>(_ => new VelvetTaskCompletionSource<int>().Task, "pending");
            return V.Label(text: "value " + value);
        }

        [Component(Compiler = false)]
        private static VNode SuspenseFailedHostRender()
            => V.Div(children: new VNode[] { V.Component(SuspenseFailedBoundaryRender, key: "boundary") });

        [Component(Compiler = false)]
        private static VNode EnterHostRender() => V.Div(children: new VNode[] { V.Component(EnterBoundaryRender, key: "boundary") });

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode PortalBoundaryRender(int tick)
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            var children = tick == 0
                ? new VNode[] { V.Label(name: "x", text: "x") }
                : new VNode[]
                {
                    V.Label(name: "x", text: "x"),
                    V.Label(name: "y", text: "y"),
                    V.Div(name: "body", children: new VNode[] { V.Component(ThrowerRender, key: "thrower") }),
                };
            return V.Portal(s_target, children);
        }

        [Component(Compiler = false)]
        private static VNode PortalHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Div(name: "host", children: new VNode[] { V.Component(PortalBoundaryRender, tick, key: "boundary") });
        }

        #endregion

        #region Where the original error goes after the fallback's own

        private static string s_outerSaw;
        private static readonly System.Collections.Generic.List<string> s_caughtStacks = new();

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_AFallbackWhoseContentThrowsUnderABoundaryCatchingInTheWalk_When_TheChildThrows_Then_TheOuterBoundaryReceivesTheContentsError()
        {
            // Arrange
            s_throws = true;

            // Act
            using var mounted = V.Mount(_root, V.Component(RecordingHostRender, key: "host"), CaughtErrors.Unlogged);

            // Assert
            Assert.That(Texts() + "|" + s_outerSaw, Is.EqualTo("outer-fallback|Broken fallback content"));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_AnOuterBoundaryThatDeclinesTheFallbacksError_When_TheOriginalErrorGoesOn_Then_ItCatchesThatInTheWalk()
        {
            // Arrange
            s_throws = true;
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: Broken fallback content");

            // Act
            using var mounted = V.Mount(_root, V.Component(DecliningHostRender, key: "host"), CaughtErrors.Unlogged);

            // Assert
            Assert.That(Texts(), Is.EqualTo("declined-fallback,sibling"));
        }

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        [Test]
        public void Given_AnOuterBoundaryThatDeclinesTheFallbacksError_When_ItReportsTheOriginalError_Then_TheComponentStackStartsAtTheComponentThatThrew()
        {
            // Arrange
            s_throws = true;
            LogAssert.Expect(LogType.Exception, "InvalidOperationException: Broken fallback content");

            // Act
            using var mounted = V.Mount(_root, V.Component(DecliningHostRender, key: "host"),
                new MountOptions((_, info) => s_caughtStacks.Add(info.ComponentStack)));

            // Assert
            Assert.That(string.Join("|", s_caughtStacks.Select(stack => stack.Split('\n')[0])),
                Is.EqualTo("    at BoundaryCatchScopeTests.ThrowerRender"));
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode RecordingOuterRender()
        {
            Hooks.UseFallback(ex =>
            {
                s_outerSaw = ex.Message;
                return V.Label(text: "outer-fallback");
            });
            return V.Component(BrokenFallbackBoundaryRender, key: "inner");
        }

        [Component(Compiler = false)]
        private static VNode RecordingHostRender() => V.Div(children: new VNode[] { V.Component(RecordingOuterRender, key: "outer") });

        // Declines the fallback content's error, by giving no fallback for it, and catches the original one.
        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode DecliningOuterRender()
        {
            Hooks.UseFallback(ex => ex.Message == "Broken fallback content" ? null : V.Label(text: "declined-fallback"));
            return V.Component(BrokenFallbackBoundaryRender, key: "inner");
        }

        [Component(Compiler = false)]
        private static VNode DecliningHostRender()
            => V.Div(children: new VNode[]
            {
                V.Component(DecliningOuterRender, key: "outer"),
                V.Label(text: "sibling"),
            });

        #endregion

        #region An element callback's error below a boundary

        private static int s_callbackSetups;
        private static int s_callbackCleanups;

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        // Before that, as characterization: the merge base takes an element callback's error on the aborting path.
        // It disposes what the element built, which a catch taken in the walk for that error would leave set up.
        [Test]
        public void Given_AnElementCallbackErrorBelowABoundary_When_TheBoundaryCatchesOnMount_Then_NoEffectUnderTheElementIsLeftSetUp()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(CallbackHostRender, key: "host"), CaughtErrors.Unlogged);

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(s_callbackSetups - s_callbackCleanups, Is.EqualTo(0));
        }

        [Component(Compiler = false)]
        private static VNode EffectUnderTheElementRender()
        {
            Hooks.UseEffect(() =>
            {
                s_callbackSetups++;
                return () => s_callbackCleanups++;
            }, Array.Empty<object>());
            return V.Label(text: "under");
        }

        [Component(Compiler = false)]
        private static VNode CallbackThrowingRender()
            => V.ScrollView(onCreated: _ => throw new InvalidOperationException("Element callback throw"),
                children: new VNode[] { V.Component(EffectUnderTheElementRender, key: "under") });

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode CallbackBoundaryRender()
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Component(CallbackThrowingRender, key: "callback");
        }

        [Component(Compiler = false)]
        private static VNode CallbackHostRender() => V.Div(children: new VNode[] { V.Component(CallbackBoundaryRender, key: "boundary") });

        // GREEN_ON_BASE(refactor): only how this fixture reads the pool's rented-out counts changed.
        // VNodePoolTestAccess now finds the event-array sets by their type rather than by the old name.
        // Before that, as characterization: the merge base forgets no catch when a primary is discarded. What this
        // pins is that the forgetting this branch adds leaves alone a catch by a boundary above the Suspense's owner.
        [Test]
        public void Given_ABoundaryAboveASuspenseOwnerCatchingAnElementCallbackErrorInAPrimaryThatStaysPending_When_ThatRenderCommits_Then_TheCatchIsReported()
        {
            // Arrange — the primary holds a memoized reader that bails while its read is still pending
            var reports = 0;
            using var mounted = V.Mount(_root, V.Div(children: new VNode[] { V.Component(OuterCatchBoundaryRender, key: "outer") }),
                new MountOptions((_, _) => reports++));

            // Act
            s_setOuterCatchTick.Invoke(1);
            mounted.FlushStateForTest();
            mounted.FlushEffectsForTest();

            // Assert — the factory's runs are read with it, since a callback that never ran is reported by nobody
            Assert.That((s_outerCatchFactoryRuns, reports), Is.EqualTo((1, 1)));
        }

        private static Action<int> s_setOuterCatchTick;
        private static int s_outerCatchFactoryRuns;

        // Memoized with no props, so the update reaches it without rendering it again, its read still pending.
        [Component(Compiler = false, Memoize = true)]
        private static VNode MemoPendingRender()
        {
            var value = Hooks.Use<int>(_ => new VelvetTaskCompletionSource<int>().Task, "memo-pending");
            return V.Label(text: "value " + value);
        }

        // Its own update is the pass, so the boundary above it takes the callback's error on the aborting path.
        [Component(Compiler = false)]
        private static VNode OuterCatchHostRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setOuterCatchTick = setTick;
            return V.Suspense(
                fallback: V.Label(text: "loading"),
                children: new VNode[]
                {
                    V.Component(MemoPendingRender, key: "pending"),
                    tick == 0 ? null : V.Component(CallbackThrowingRender, key: "callback"),
                });
        }

        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode OuterCatchBoundaryRender()
        {
            Hooks.UseFallback(_ =>
            {
                s_outerCatchFactoryRuns++;
                return V.Label(text: "outer-fallback");
            });
            return V.Component(OuterCatchHostRender, key: "host");
        }


        #endregion
    }
}
