using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that an inline component's recorded slot start keeps naming the rows its container holds
    /// for it across the moves below, so its own re-render rewrites its own rows.
    /// <list type="bullet">
    /// <item>A component growing inside the component ahead of it, or ahead of the component holding it.</item>
    /// <item>A neighbouring Portal on the same target growing — whether that Portal's own children changed, a
    /// component inside it grew, or its patch drained a parked component — or unmounting; and a Portal whose
    /// component grew removes every row it added when it goes.</item>
    /// <item>A pass that meant to move it and aborted, which leaves it where the container still holds it.</item>
    /// <item>A walk that drains a parked component ahead of a boundary it has not reached yet, whose catch
    /// then lands on the boundary's own rows.</item>
    /// <item>A fallback holding more or fewer rows than its boundary did, which moves what comes after it
    /// once even where the boundary's own re-render is measuring the container around the swap; and a
    /// drain inside the re-render of the component holding the drained one, likewise.</item>
    /// <item>Disposing an inline component removes its own rows.</item>
    /// <item>Where two components start at the same row, which of them moves is decided by which holds
    /// rows, which holds the other, and otherwise by their order in the fiber tree.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class InlineSlotStartTests
    {
        private const string ThrowMessage = "Inline slot start test throw";

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_portalTarget = null;
            s_setInnerRows = null;
            s_holdEmpty = false;
            s_setTailTick = null;
            s_setGrowerRows = null;
            s_setLeafTick = null;
            s_setPortalRows = null;
            s_setPortalChildTick = null;
            s_setPortalGrowerRows = null;
            s_setShowGrowingPortal = null;
            s_setMoverTick = null;
            s_setMoveOrder = null;
            s_bombThrows = false;
            s_setSelfGuardTick = null;
            s_setParkingPortalTick = null;
            s_setEnclosingTick = null;
            s_parkedRows = 1;
            s_parkedFiber = null;
            s_setParkOrder = null;
            s_unmountedRow = null;
            s_setOwnRows = null;
            s_setOwnChildTick = null;
            s_setHolderTick = null;
            s_setHeldRows = null;
            s_setLateShown = null;
            s_setLateRows = null;
            s_setSiblingTick = null;
            s_setFirstRows = null;
            s_setSecondRows = null;
            s_setAheadOrder = null;
            s_setAheadPortalChildTick = null;
            s_setShowSecondPortal = null;
            s_setOwnRowsPortalRows = null;
            s_setOwnRowsPortalChildTick = null;
            s_setDeclaringTick = null;
            s_setBoxedGrowerRows = null;
            s_setShowBoxedPortal = null;
            s_setOwnPortalShown = null;
            s_setPortalHolderTick = null;
            s_setShowHeldPortal = null;
            s_setAheadPortalRows = null;
            s_setSeamGrowerRows = null;
            s_setSeamOwnRows = null;
            s_setShowSeamGrower = null;
            s_setEmptyGrowerRows = null;
            s_setBehindEmptyRows = null;
            s_setLateRangeOnTarget = null;
            s_setLateRangeRows = null;
            s_setWrittenAheadShown = null;
            s_setWrittenAheadRows = null;
            s_setWrittenBehindRows = null;
            s_setEmptyAheadChildRows = null;
            s_setOwnRowsFromEmpty = null;
            s_setRetargeted = null;
            s_setRetargetFirstRows = null;
            s_setRetargetRowTick = null;
            s_retargetElsewhere = null;
            s_setMidWalkOrder = null;
            s_midWalkThrows = false;
        }

        private static string Names(VisualElement container)
            => string.Join(",", container.Children().Select(child => child.name));

        private static VNode Rows(string prefix, int count)
        {
            var rows = new VNode[count];
            for (var i = 0; i < count; i++) rows[i] = V.Label(name: prefix + i, key: prefix + i);
            return V.Fragment(children: rows);
        }

        #region Growth below a co-located component

        private static Action<int> s_setInnerRows;
        private static Action<int> s_setTailTick;

        [Component]
        private static VNode InnerRender()
        {
            var (rows, setRows) = Hooks.UseState(1);
            s_setInnerRows = setRows;
            return Rows("inner", rows);
        }

        [Component]
        private static VNode OuterRender() => V.Component(InnerRender, key: "inner");

        [Component]
        private static VNode TailRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTailTick = setTick;
            return V.Fragment(children: new VNode[]
            {
                V.Label(name: "tail0-" + tick, key: "tail0"),
                V.Label(name: "tail1-" + tick, key: "tail1"),
            });
        }

        [Component]
        private static VNode NestedGrowthHostRender()
            => V.Div(name: "box", children: new VNode[]
            {
                V.Component(OuterRender, key: "outer"),
                V.Component(TailRender, key: "tail"),
            });

        [Test]
        public void Given_AComponentGrewInsideTheOneAheadOfIt_When_TheOneBehindRerenders_Then_ItRewritesItsOwnRows()
        {
            // Arrange — the growth is the inner component's own render, one level below the one ahead.
            using var mounted = V.Mount(_root, V.Component(NestedGrowthHostRender, key: "host"));
            s_setInnerRows.Invoke(3);
            mounted.FlushStateForTest();

            // Act
            s_setTailTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(_root.Q(name: "box")), Is.EqualTo("inner0,inner1,inner2,tail0-1,tail1-1"));
        }

        private static Action<int> s_setGrowerRows;
        private static Action<int> s_setLeafTick;

        [Component]
        private static VNode GrowerRender()
        {
            var (rows, setRows) = Hooks.UseState(1);
            s_setGrowerRows = setRows;
            return Rows("grow", rows);
        }

        [Component]
        private static VNode LeafRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setLeafTick = setTick;
            return V.Label(name: "leaf-" + tick, key: "leaf");
        }

        [Component]
        private static VNode LeafHolderRender() => V.Component(LeafRender, key: "leaf");

        [Component]
        private static VNode GrowthAheadOfAHolderHostRender()
            => V.Div(name: "box", children: new VNode[]
            {
                V.Component(GrowerRender, key: "grower"),
                V.Component(LeafHolderRender, key: "holder"),
            });

        [Test]
        public void Given_AComponentGrewAheadOfOneHoldingAnother_When_TheHeldOneRerenders_Then_ItRewritesItsOwnRow()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(GrowthAheadOfAHolderHostRender, key: "host"));
            s_setGrowerRows.Invoke(3);
            mounted.FlushStateForTest();

            // Act
            s_setLeafTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(_root.Q(name: "box")), Is.EqualTo("grow0,grow1,grow2,leaf-1"));
        }

        #endregion

        #region A neighbouring Portal on the same target

        private static VisualElement s_portalTarget;
        private static Action<int> s_setPortalRows;
        private static Action<int> s_setPortalChildTick;
        private static Action<int> s_setPortalGrowerRows;
        private static Action<int> s_setShowGrowingPortal;

        [Component]
        private static VNode GrowingPortalRender()
        {
            var (rows, setRows) = Hooks.UseState(1);
            s_setPortalRows = setRows;
            var children = new VNode[rows];
            for (var i = 0; i < rows; i++) children[i] = V.Label(name: "a" + i, key: "a" + i);
            return V.Portal(s_portalTarget, children: children);
        }

        [Component]
        private static VNode PortalChildRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setPortalChildTick = setTick;
            return V.Label(name: "b-" + tick, key: "b");
        }

        [Component]
        private static VNode PortalWithChildRender()
            => V.Portal(s_portalTarget, children: new VNode[] { V.Component(PortalChildRender, key: "child") });

        [Component]
        private static VNode PortalGrowerRender()
        {
            var (rows, setRows) = Hooks.UseState(1);
            s_setPortalGrowerRows = setRows;
            return Rows("a", rows);
        }

        [Component]
        private static VNode PortalWithGrowerRender()
            => V.Portal(s_portalTarget, children: new VNode[] { V.Component(PortalGrowerRender, key: "grower") });

        // Each Portal is declared by a component of its own, so neither re-renders when the other does.
        [Component]
        private static VNode PortalNeighboursHostRender()
            => V.Fragment(children: new VNode[]
            {
                V.Component(GrowingPortalRender, key: "first"),
                V.Component(PortalWithChildRender, key: "second"),
            });

        [Component]
        private static VNode GrowingChildPortalNeighboursHostRender()
        {
            var (shown, setShown) = Hooks.UseState(1);
            s_setShowGrowingPortal = setShown;
            return V.Fragment(children: new VNode[]
            {
                shown == 1 ? V.Component(PortalWithGrowerRender, key: "first") : null,
                V.Component(PortalWithChildRender, key: "second"),
            });
        }

        [Test]
        public void Given_APortalGrewAheadOfAnotherOnItsTarget_When_TheSecondPortalsChildRerenders_Then_ItRewritesItsOwnRow()
        {
            // Arrange
            s_portalTarget = new VisualElement();
            using var mounted = V.Mount(_root, V.Component(PortalNeighboursHostRender, key: "host"));
            s_setPortalRows.Invoke(2);
            mounted.FlushStateForTest();

            // Act
            s_setPortalChildTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(s_portalTarget), Is.EqualTo("a0,a1,b-1"));
        }

        [Test]
        public void Given_AComponentInAPortalGrewAheadOfAnotherPortal_When_TheSecondPortalsChildRerenders_Then_ItRewritesItsOwnRow()
        {
            // Arrange
            s_portalTarget = new VisualElement();
            using var mounted = V.Mount(_root, V.Component(GrowingChildPortalNeighboursHostRender, key: "host"));
            s_setPortalGrowerRows.Invoke(2);
            mounted.FlushStateForTest();

            // Act
            s_setPortalChildTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(s_portalTarget), Is.EqualTo("a0,a1,b-1"));
        }

        [Test]
        public void Given_AComponentInAPortalGrew_When_ThatPortalUnmounts_Then_EveryRowItAddedLeavesTheTarget()
        {
            // Arrange
            s_portalTarget = new VisualElement();
            using var mounted = V.Mount(_root, V.Component(GrowingChildPortalNeighboursHostRender, key: "host"));
            s_setPortalGrowerRows.Invoke(2);
            mounted.FlushStateForTest();

            // Act
            s_setShowGrowingPortal.Invoke(0);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(s_portalTarget), Is.EqualTo("b-0"));
        }

        private static Action<int> s_setPortalHolderTick;
        private static Action<int> s_setShowHeldPortal;

        [Component]
        private static VNode PortalHolderRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setPortalHolderTick = setTick;
            return V.Fragment(children: new VNode[]
            {
                V.Label(name: "h-" + tick, key: "h"),
                V.Component(PortalGrowerRender, key: "grower"),
            });
        }

        [Component]
        private static VNode HeldGrowerPortalRender()
            => V.Portal(s_portalTarget, children: new VNode[] { V.Component(PortalHolderRender, key: "holder") });

        [Component]
        private static VNode HeldGrowerPortalNeighboursHostRender()
        {
            var (shown, setShown) = Hooks.UseState(1);
            s_setShowHeldPortal = setShown;
            return V.Fragment(children: new VNode[]
            {
                shown == 1 ? V.Component(HeldGrowerPortalRender, key: "first") : null,
                V.Component(PortalWithChildRender, key: "second"),
            });
        }

        [Test]
        public void Given_AComponentBelowAPortalsChildGrewAfterThatChildRerendered_When_ThePortalUnmounts_Then_EveryRowItAddedLeavesTheTarget()
        {
            // Arrange — the holder's own re-render reaches the grower with no Portal named, which erases the
            // grower's own record of the Portal it is in; the growth comes after that.
            s_portalTarget = new VisualElement();
            using var mounted = V.Mount(_root, V.Component(HeldGrowerPortalNeighboursHostRender, key: "host"));
            s_setPortalHolderTick.Invoke(1);
            mounted.FlushStateForTest();
            s_setPortalGrowerRows.Invoke(2);
            mounted.FlushStateForTest();

            // Act
            s_setShowHeldPortal.Invoke(0);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(s_portalTarget), Is.EqualTo("b-0"));
        }

        private static Action<int> s_setOwnPortalShown;

        [Component]
        private static VNode SelfTogglingPortalRender()
        {
            var (shown, setShown) = Hooks.UseState(1);
            s_setOwnPortalShown = setShown;
            return shown == 1
                ? V.Portal(s_portalTarget, children: new VNode[] { V.Label(name: "a0", key: "a0") })
                : V.Fragment(children: Array.Empty<VNode>());
        }

        [Component]
        private static VNode SelfTogglingPortalNeighboursHostRender()
            => V.Fragment(children: new VNode[]
            {
                V.Component(SelfTogglingPortalRender, key: "first"),
                V.Component(PortalWithChildRender, key: "second"),
            });

        [Test]
        public void Given_APortalAheadOfAnotherOnItsTargetUnmounted_When_TheSecondPortalsChildRerenders_Then_ItRewritesItsOwnRow()
        {
            // Arrange — the first Portal's own component drops it, so the second Portal is not patched in that pass.
            s_portalTarget = new VisualElement();
            using var mounted = V.Mount(_root, V.Component(SelfTogglingPortalNeighboursHostRender, key: "host"));
            s_setOwnPortalShown.Invoke(0);
            mounted.FlushStateForTest();

            // Act
            s_setPortalChildTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(s_portalTarget), Is.EqualTo("b-1"));
        }

        private static Action<int> s_setAheadPortalChildTick;
        private static Action<int> s_setShowSecondPortal;
        private static Action<int> s_setOwnRowsPortalRows;
        private static Action<int> s_setOwnRowsPortalChildTick;
        private static Action<int> s_setDeclaringTick;
        private static Action<int> s_setBoxedGrowerRows;
        private static Action<int> s_setShowBoxedPortal;

        [Component]
        private static VNode AheadPortalChildRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setAheadPortalChildTick = setTick;
            return V.Label(name: "z-" + tick, key: "z");
        }

        [Component]
        private static VNode AheadPortalRender()
            => V.Portal(s_portalTarget, children: new VNode[] { V.Component(AheadPortalChildRender, key: "child") });

        [Component]
        private static VNode AheadOfAGrowingPortalHostRender()
            => V.Fragment(children: new VNode[]
            {
                V.Component(AheadPortalRender, key: "ahead"),
                V.Component(GrowingPortalRender, key: "growing"),
            });

        // GREEN_ON_BASE(characterization): the base's Portal patch shifts no component outside its own range.
        // What it pins is that the components a patch does shift are the ones behind its range.
        [Test]
        public void Given_APortalAheadOfOneThatGrows_When_ItsChildRerenders_Then_ItRewritesItsOwnRow()
        {
            // Arrange
            s_portalTarget = new VisualElement();
            using var mounted = V.Mount(_root, V.Component(AheadOfAGrowingPortalHostRender, key: "host"));
            s_setPortalRows.Invoke(2);
            mounted.FlushStateForTest();

            // Act
            s_setAheadPortalChildTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(s_portalTarget), Is.EqualTo("z-1,a0,a1"));
        }

        [Component]
        private static VNode AheadOfALeavingPortalHostRender()
        {
            var (shown, setShown) = Hooks.UseState(1);
            s_setShowSecondPortal = setShown;
            return V.Fragment(children: new VNode[]
            {
                V.Component(AheadPortalRender, key: "ahead"),
                shown == 1 ? V.Component(GrowingPortalRender, key: "leaving") : null,
            });
        }

        // GREEN_ON_BASE(characterization): the base's Portal teardown shifts no component's start.
        // What it pins is that the components a teardown does shift are the ones behind its range.
        [Test]
        public void Given_APortalAheadOfOneThatUnmounts_When_ItsChildRerenders_Then_ItRewritesItsOwnRow()
        {
            // Arrange
            s_portalTarget = new VisualElement();
            using var mounted = V.Mount(_root, V.Component(AheadOfALeavingPortalHostRender, key: "host"));
            s_setShowSecondPortal.Invoke(0);
            mounted.FlushStateForTest();

            // Act
            s_setAheadPortalChildTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(s_portalTarget), Is.EqualTo("z-1"));
        }

        [Component]
        private static VNode OwnRowsPortalChildRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setOwnRowsPortalChildTick = setTick;
            return V.Label(name: "c-" + tick, key: "c");
        }

        [Component]
        private static VNode RowsThenChildPortalRender()
        {
            var (rows, setRows) = Hooks.UseState(1);
            s_setOwnRowsPortalRows = setRows;
            var children = new VNode[rows + 1];
            for (var i = 0; i < rows; i++) children[i] = V.Label(name: "r" + i, key: "r" + i);
            children[rows] = V.Component(OwnRowsPortalChildRender, key: "child");
            return V.Portal(s_portalTarget, children: children);
        }

        // GREEN_ON_BASE(characterization): the base's Portal patch places its own child and shifts no component.
        // What it pins is that a patch leaves alone its own child, which the patch has just placed.
        [Test]
        public void Given_APortalsOwnChildrenGrewAheadOfItsComponent_When_ThatComponentRerenders_Then_ItRewritesItsOwnRow()
        {
            // Arrange
            s_portalTarget = new VisualElement();
            using var mounted = V.Mount(_root, V.Component(RowsThenChildPortalRender, key: "host"));
            s_setOwnRowsPortalRows.Invoke(3);
            mounted.FlushStateForTest();

            // Act
            s_setOwnRowsPortalChildTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(s_portalTarget), Is.EqualTo("r0,r1,r2,c-1"));
        }

        [Component]
        private static VNode TickedChildRender(int tick) => V.Label(name: "b-" + tick, key: "b");

        [Component]
        private static VNode DeclaringPortalRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setDeclaringTick = setTick;
            return V.Portal(s_portalTarget, children: new VNode[] { V.Component(TickedChildRender, tick, key: "child") });
        }

        [Component]
        private static VNode GrowerThenDeclaringPortalHostRender()
            => V.Fragment(children: new VNode[]
            {
                V.Component(PortalWithGrowerRender, key: "first"),
                V.Component(DeclaringPortalRender, key: "second"),
            });

        [Test]
        public void Given_AComponentInAPortalGrewAheadOfAnotherPortal_When_TheSecondPortalIsPatched_Then_ItRewritesItsOwnRange()
        {
            // Arrange
            s_portalTarget = new VisualElement();
            using var mounted = V.Mount(_root, V.Component(GrowerThenDeclaringPortalHostRender, key: "host"));
            s_setPortalGrowerRows.Invoke(2);
            mounted.FlushStateForTest();

            // Act — the second Portal's declaring component re-renders, so its patch reconciles its range.
            s_setDeclaringTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(s_portalTarget), Is.EqualTo("a0,a1,b-1"));
        }

        [Component]
        private static VNode BoxedGrowerRender()
        {
            var (rows, setRows) = Hooks.UseState(1);
            s_setBoxedGrowerRows = setRows;
            return Rows("in", rows);
        }

        [Component]
        private static VNode BoxRender()
            => V.Div(name: "a-box", children: new VNode[] { V.Component(BoxedGrowerRender, key: "grower") });

        [Component]
        private static VNode BoxedPortalRender()
            => V.Portal(s_portalTarget, children: new VNode[] { V.Component(BoxRender, key: "box") });

        [Component]
        private static VNode BoxedPortalThenPortalHostRender()
        {
            var (shown, setShown) = Hooks.UseState(1);
            s_setShowBoxedPortal = setShown;
            return V.Fragment(children: new VNode[]
            {
                shown == 1 ? V.Component(BoxedPortalRender, key: "first") : null,
                V.Component(PortalWithChildRender, key: "second"),
            });
        }

        // GREEN_ON_BASE(characterization): the base moves no Portal range for a component's own growth.
        // What it pins is that growth inside an element of a Portal's child leaves that Portal's range alone.
        [Test]
        public void Given_AComponentGrewInsideAnElementInAPortal_When_ThatPortalUnmounts_Then_OnlyItsOwnRowLeaves()
        {
            // Arrange
            s_portalTarget = new VisualElement();
            using var mounted = V.Mount(_root, V.Component(BoxedPortalThenPortalHostRender, key: "host"));
            s_setBoxedGrowerRows.Invoke(3);
            mounted.FlushStateForTest();

            // Act
            s_setShowBoxedPortal.Invoke(0);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(s_portalTarget), Is.EqualTo("b-0"));
        }

        private static Action<int> s_setParkingPortalTick;

        [Component]
        private static VNode ParkingPortalRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setParkingPortalTick = setTick;
            return V.Portal(s_portalTarget, children: new VNode[]
            {
                V.Label(name: "p-" + tick, key: "p"),
                V.Component(ParkedRowsRender, key: "g"),
            });
        }

        [Component]
        private static VNode ParkedPortalNeighboursHostRender()
            => V.Fragment(children: new VNode[]
            {
                V.Component(ParkingPortalRender, key: "first"),
                V.Component(PortalWithChildRender, key: "second"),
            });

        [Test]
        public void Given_APortalsPatchDrainedAParkedComponentAheadOfAnotherPortal_When_TheSecondPortalsChildRerenders_Then_ItRewritesItsOwnRow()
        {
            // Arrange — g, inside the first Portal, parks growing on the Transition lane; the first Portal's
            // declaring component re-renders, and the patch of its children reaches g and drains it.
            s_portalTarget = new VisualElement();
            using var mounted = V.Mount(_root, V.Component(ParkedPortalNeighboursHostRender, key: "host"));
            s_parkedRows = 20;
            s_parkedFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_parkedFiber.FlushStateWithTinyBudgetForTest();
            var parked = s_parkedFiber.HasPendingReconcileWorkForTest();
            s_setParkingPortalTick.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            s_setPortalChildTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — parked is folded in: a g that never parked leaves no drain to ask about.
            var rows = string.Join(",", Enumerable.Range(0, 20).Select(i => "g" + i));
            Assert.That((parked, Names(s_portalTarget)), Is.EqualTo((true, "p-1," + rows + ",b-1")));
        }

        #endregion

        #region A pass that aborts before placing

        private static Action<int> s_setMoverTick;
        private static Action<int> s_setMoveOrder;
        private static bool s_bombThrows;

        [Component]
        private static VNode MoverRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setMoverTick = setTick;
            return V.Label(name: "mover-" + tick, key: "mover");
        }

        [Component(Compiler = false)]
        private static VNode BombRender()
        {
            if (s_bombThrows) throw new InvalidOperationException(ThrowMessage);
            return V.Label(name: "bomb", key: "bomb");
        }

        [Component(IsErrorBoundary = true)]
        private static VNode GuardRender()
        {
            Hooks.UseFallback(_ => V.Label(name: "guard-fallback", key: "fallback"));
            return V.Component(BombRender, key: "bomb");
        }

        [Component]
        private static VNode AbortedMoveHostRender()
        {
            var (order, setOrder) = Hooks.UseState(0);
            s_setMoveOrder = setOrder;
            return order == 0
                ? V.Div(name: "box", children: new VNode[]
                {
                    V.Component(MoverRender, key: "mover"),
                    V.Label(name: "ahead", key: "ahead"),
                    V.Component(GuardRender, key: "guard"),
                })
                : V.Div(name: "box", children: new VNode[]
                {
                    V.Label(name: "ahead", key: "ahead"),
                    V.Component(MoverRender, key: "mover"),
                    V.Component(GuardRender, key: "guard"),
                });
        }

        [Test]
        public void Given_APassThatMovedAComponentAborted_When_TheComponentRerenders_Then_ItRewritesTheRowItStillHolds()
        {
            // Arrange — the pass puts the mover behind "ahead", and a catch later in the same pass stops it
            // from placing anything.
            using var mounted = V.Mount(_root, V.Component(AbortedMoveHostRender, key: "host"));
            s_bombThrows = true;
            s_setMoveOrder.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            s_setMoverTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(_root.Q(name: "box")), Is.EqualTo("mover-1,ahead,guard-fallback"));
        }

        #endregion

        #region A drain inside a walk, ahead of a boundary the walk has not reached

        private static int s_parkedRows;
        private static ComponentFiber s_parkedFiber;
        private static Action<int> s_setParkOrder;

        [Component(Compiler = false)]
        private static VNode ParkedRowsRender()
        {
            s_parkedFiber = FiberAmbientStack.Current;
            return Rows("g", s_parkedRows);
        }

        [Component(IsErrorBoundary = true)]
        private static VNode ParkGuardRender()
        {
            Hooks.UseFallback(_ => V.Label(name: "s-fallback", key: "fallback"));
            return V.Component(BombRender, key: "bomb");
        }

        [Component]
        private static VNode ParkHostRender()
        {
            var (order, setOrder) = Hooks.UseState(0);
            s_setParkOrder = setOrder;
            return V.Div(name: "box", children: order == 1
                ? new VNode[] { V.Component(ParkGuardRender, key: "s"), V.Component(ParkedRowsRender, key: "g") }
                : new VNode[] { V.Component(ParkedRowsRender, key: "g"), V.Component(ParkGuardRender, key: "s") });
        }

        [Test]
        public void Given_AWalkDrainsAParkedComponentAheadOfABoundary_When_TheBoundaryCatches_Then_ItsFallbackReplacesItsOwnRow()
        {
            // Arrange — created as g then the boundary, reordered to the boundary first, and g parks growing on
            // the Transition lane. The pass below puts g first again, so the walk reaches g, drains it, and
            // only then reaches the boundary.
            using var mounted = V.Mount(_root, V.Component(ParkHostRender, key: "host"));
            s_setParkOrder.Invoke(1);
            mounted.FlushStateForTest();
            s_parkedRows = 20;
            s_parkedFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_parkedFiber.FlushStateWithTinyBudgetForTest();
            var parked = s_parkedFiber.HasPendingReconcileWorkForTest();
            s_bombThrows = true;

            // Act
            s_setParkOrder.Invoke(2);
            mounted.FlushStateForTest();

            // Assert — parked is folded in: a g that never parked leaves no drain inside the walk to ask about.
            var rows = string.Join(",", Enumerable.Range(0, 20).Select(i => "g" + i));
            Assert.That((parked, Names(_root.Q(name: "box"))), Is.EqualTo((true, "s-fallback," + rows)));
        }

        #endregion

        #region A fallback holding a different number of rows

        private static Action<int> s_setSelfGuardTick;

        [Component]
        private static VNode ThreeRowsFailingRefRender()
            => V.Fragment(children: new VNode[]
            {
                V.Label(name: "g0", key: "g0"),
                V.Label(name: "g1", key: "g1"),
                V.Div(name: "g2", key: "g2", refCallback: _ => throw new InvalidOperationException(ThrowMessage)),
            });

        [Component]
        private static VNode OneRowFailingRefRender()
            => V.Div(name: "g0", key: "g0", refCallback: _ => throw new InvalidOperationException(ThrowMessage));

        [Component(IsErrorBoundary = true)]
        private static VNode OneRowFallbackGuardRender()
        {
            Hooks.UseFallback(_ => V.Label(name: "guard-fallback", key: "fallback"));
            return V.Component(ThreeRowsFailingRefRender, key: "rows");
        }

        [Component(IsErrorBoundary = true)]
        private static VNode ThreeRowFallbackGuardRender()
        {
            Hooks.UseFallback(_ => Rows("f", 3));
            return V.Component(OneRowFailingRefRender, key: "row");
        }

        // A ref setup runs where the pass that committed its element ends, so the catch comes after that
        // pass placed every row and no reconcile of the container is in flight around it.
        [Component]
        private static VNode ShrinkingFallbackHostRender()
            => V.Div(name: "box", children: new VNode[]
            {
                V.Component(OneRowFallbackGuardRender, key: "guard"),
                V.Component(TailRender, key: "tail"),
            });

        [Component]
        private static VNode GrowingFallbackHostRender()
            => V.Div(name: "box", children: new VNode[]
            {
                V.Component(ThreeRowFallbackGuardRender, key: "guard"),
                V.Component(TailRender, key: "tail"),
            });

        [Test]
        public void Given_AFallbackHoldingFewerRowsThanItsBoundary_When_TheComponentBehindRerenders_Then_ItRewritesItsOwnRows()
        {
            // Arrange — the mount's ref setup throws, and the boundary swaps its three rows for one.
            using var mounted = V.Mount(_root, V.Component(ShrinkingFallbackHostRender, key: "host"));

            // Act
            s_setTailTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(_root.Q(name: "box")), Is.EqualTo("guard-fallback,tail0-1,tail1-1"));
        }

        [Test]
        public void Given_AFallbackHoldingMoreRowsThanItsBoundary_When_TheComponentBehindRerenders_Then_ItRewritesItsOwnRows()
        {
            // Arrange — the mount's ref setup throws, and the boundary swaps its one row for three.
            using var mounted = V.Mount(_root, V.Component(GrowingFallbackHostRender, key: "host"));

            // Act
            s_setTailTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(_root.Q(name: "box")), Is.EqualTo("f0,f1,f2,tail0-1,tail1-1"));
        }

        [Component(IsErrorBoundary = true)]
        private static VNode SelfRenderingGuardRender()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setSelfGuardTick = setTick;
            Hooks.UseFallback(_ => V.Label(name: "guard-fallback", key: "fallback"));
            return V.Component(ThreeRowBombRender, key: "bomb");
        }

        [Component(Compiler = false)]
        private static VNode ThreeRowBombRender()
        {
            if (s_bombThrows) throw new InvalidOperationException(ThrowMessage);
            return Rows("g", 3);
        }

        [Component]
        private static VNode SelfCatchHostRender()
            => V.Div(name: "box", children: new VNode[]
            {
                V.Component(SelfRenderingGuardRender, key: "guard"),
                V.Component(TailRender, key: "tail"),
            });

        // GREEN_ON_BASE(characterization): the base's boundary re-render measures the swap it contains.
        // There the swap itself moves nothing, and the change the boundary's own re-render measures around
        // it is what moves the component behind. It pins that a swap which moves what comes after it is not
        // counted again by the re-render it happened inside.
        [Test]
        public void Given_ABoundaryCaughtInsideItsOwnRerender_When_TheComponentBehindRerenders_Then_ItRewritesItsOwnRows()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(SelfCatchHostRender, key: "host"));
            s_bombThrows = true;
            s_setSelfGuardTick.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            s_setTailTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(_root.Q(name: "box")), Is.EqualTo("guard-fallback,tail0-1,tail1-1"));
        }

        #endregion

        #region A drain inside a component's own re-render

        private static Action<int> s_setEnclosingTick;

        [Component]
        private static VNode EnclosingRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setEnclosingTick = setTick;
            return V.Fragment(children: new VNode[]
            {
                V.Label(name: "x-" + tick, key: "x"),
                V.Component(ParkedRowsRender, key: "g"),
            });
        }

        [Component]
        private static VNode EnclosingHostRender()
            => V.Div(name: "box", children: new VNode[]
            {
                V.Component(EnclosingRender, key: "enclosing"),
                V.Component(TailRender, key: "tail"),
            });

        // The holder's own re-render measures the container around the drain, which has already moved what
        // comes after it.
        [Test]
        public void Given_AParkedComponentDrainedInsideItsHoldersRerender_When_TheComponentBehindRerenders_Then_ItRewritesItsOwnRows()
        {
            // Arrange — g parks growing on the Transition lane; the holder's own re-render reaches it and
            // drains it inside the holder's reconcile.
            using var mounted = V.Mount(_root, V.Component(EnclosingHostRender, key: "host"));
            s_parkedRows = 20;
            s_parkedFiber.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
            s_parkedFiber.FlushStateWithTinyBudgetForTest();
            var parked = s_parkedFiber.HasPendingReconcileWorkForTest();
            s_setEnclosingTick.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            s_setTailTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — parked is folded in: a g that never parked leaves no drain to ask about.
            var rows = string.Join(",", Enumerable.Range(0, 20).Select(i => "g" + i));
            Assert.That((parked, Names(_root.Q(name: "box"))), Is.EqualTo((true, "x-1," + rows + ",tail0-1,tail1-1")));
        }

        #endregion

        #region Disposal

        private static ComponentFiber s_unmountedRow;

        [Component]
        private static VNode UnmountedRowRender()
        {
            s_unmountedRow = FiberAmbientStack.Current;
            return V.Label(name: "own", key: "own");
        }

        // Disposed directly: ComponentRegistry.DisposeFiberInternal nulls an inline fiber's tree before
        // disposing it, and ComponentRegistry.Dispose runs after Reconciler.Dispose has marked the context
        // disposed, where Reconciler.Reconcile returns at once — so neither lets the unmount's reconcile touch
        // the container.
        [Test]
        public void Given_AnInlineComponentBehindAnotherRow_When_ItIsDisposed_Then_OnlyItsOwnRowLeaves()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Fragment(children: new VNode[]
            {
                V.Label(name: "lead", key: "lead"),
                V.Component(UnmountedRowRender, key: "row"),
            }));

            // Act
            FiberRenderer.Dispose(s_unmountedRow);

            // Assert
            Assert.That(Names(_root), Is.EqualTo("lead"));
        }

        #endregion

        #region Which of two components at one row moves

        private static Action<int> s_setOwnRows;
        private static Action<int> s_setOwnChildTick;

        [Component]
        private static VNode OwnChildRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setOwnChildTick = setTick;
            return V.Label(name: "in-" + tick, key: "in");
        }

        [Component]
        private static VNode RowsThenChildRender()
        {
            var (rows, setRows) = Hooks.UseState(1);
            s_setOwnRows = setRows;
            var children = new VNode[rows + 1];
            for (var i = 0; i < rows; i++) children[i] = V.Label(name: "o" + i, key: "o" + i);
            children[rows] = V.Component(OwnChildRender, key: "child");
            return V.Fragment(children: children);
        }

        [Component]
        private static VNode OwnRowsHostRender()
            => V.Div(name: "box", children: new VNode[] { V.Component(RowsThenChildRender, key: "outer") });

        // GREEN_ON_BASE(characterization): the base's shift walks the grower's siblings and not its children.
        // What it pins is that a shift reading every component on the container leaves alone the rows the
        // grower's own reconcile has just placed.
        [Test]
        public void Given_AComponentsOwnRenderMovedItsChild_When_TheChildRerenders_Then_ItRewritesItsOwnRow()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(OwnRowsHostRender, key: "host"));
            s_setOwnRows.Invoke(3);
            mounted.FlushStateForTest();

            // Act
            s_setOwnChildTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(_root.Q(name: "box")), Is.EqualTo("o0,o1,o2,in-1"));
        }

        private static Action<int> s_setHolderTick;
        private static Action<int> s_setHeldRows;

        [Component]
        private static VNode HeldRender()
        {
            var (rows, setRows) = Hooks.UseState(0);
            s_setHeldRows = setRows;
            return Rows("e", rows);
        }

        [Component]
        private static VNode HolderRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setHolderTick = setTick;
            return V.Fragment(children: new VNode[]
            {
                V.Component(HeldRender, key: "held"),
                V.Label(name: "h-" + tick, key: "h"),
            });
        }

        [Component]
        private static VNode HolderHostRender()
            => V.Div(name: "box", children: new VNode[] { V.Component(HolderRender, key: "holder") });

        // GREEN_ON_BASE(characterization): the base's shift walks the grower's siblings and not its holder.
        // What it pins is that a holder starting at the empty component's row stays where it is.
        [Test]
        public void Given_AnEmptyComponentAtTheFirstRowOfItsHolder_When_ItGrows_Then_TheHolderStillRewritesItsOwnRows()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(HolderHostRender, key: "host"));
            s_setHeldRows.Invoke(2);
            mounted.FlushStateForTest();

            // Act
            s_setHolderTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(_root.Q(name: "box")), Is.EqualTo("e0,e1,h-1"));
        }

        private static Action<int> s_setLateShown;
        private static Action<int> s_setLateRows;
        private static Action<int> s_setSiblingTick;

        [Component]
        private static VNode LateRender()
        {
            var (rows, setRows) = Hooks.UseState(0);
            s_setLateRows = setRows;
            return Rows("late", rows);
        }

        [Component]
        private static VNode SiblingRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setSiblingTick = setTick;
            return V.Label(name: "sib-" + tick, key: "sib");
        }

        [Component]
        private static VNode LateMountHostRender()
        {
            var (shown, setShown) = Hooks.UseState(0);
            s_setLateShown = setShown;
            return V.Div(name: "box", children: new VNode[]
            {
                shown == 1 ? V.Component(LateRender, key: "late") : null,
                V.Component(SiblingRender, key: "sib"),
            });
        }

        [Test]
        public void Given_AnEmptyComponentMountedAheadOfAnother_When_ItGrows_Then_TheOtherRerendersItsOwnRow()
        {
            // Arrange — the empty component mounts after the one it sits ahead of, so the two were created in
            // the opposite order to their rows.
            using var mounted = V.Mount(_root, V.Component(LateMountHostRender, key: "host"));
            s_setLateShown.Invoke(1);
            mounted.FlushStateForTest();
            s_setLateRows.Invoke(2);
            mounted.FlushStateForTest();

            // Act
            s_setSiblingTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(_root.Q(name: "box")), Is.EqualTo("late0,late1,sib-1"));
        }

        private static Action<int> s_setFirstRows;
        private static Action<int> s_setSecondRows;

        [Component]
        private static VNode FirstEmptyRender()
        {
            var (rows, setRows) = Hooks.UseState(0);
            s_setFirstRows = setRows;
            return Rows("first", rows);
        }

        [Component]
        private static VNode SecondEmptyRender()
        {
            var (rows, setRows) = Hooks.UseState(0);
            s_setSecondRows = setRows;
            return Rows("second", rows);
        }

        // The leading row puts both components past the container's first row.
        [Component]
        private static VNode TwoEmptiesHostRender()
            => V.Div(name: "box", children: new VNode[]
            {
                V.Label(name: "lead", key: "lead"),
                V.Component(FirstEmptyRender, key: "first"),
                V.Component(SecondEmptyRender, key: "second"),
            });

        [Component]
        private static VNode EmptyHolderRender() => V.Component(FirstEmptyRender, key: "first");

        [Component]
        private static VNode HeldEmptyThenEmptyHostRender()
            => V.Div(name: "box", children: new VNode[]
            {
                V.Component(EmptyHolderRender, key: "holder"),
                V.Component(SecondEmptyRender, key: "second"),
            });

        [Test]
        public void Given_AnEmptyComponentInsideAnEmptyHolderAheadOfAnother_When_ItGrowsAndThenTheOther_Then_TheOthersRowsLandBehind()
        {
            // Arrange — the two empty components sit at one row, one level apart.
            using var mounted = V.Mount(_root, V.Component(HeldEmptyThenEmptyHostRender, key: "host"));
            s_setFirstRows.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            s_setSecondRows.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(_root.Q(name: "box")), Is.EqualTo("first0,second0"));
        }

        // GREEN_ON_BASE(characterization): the base's shift moves a following sibling starting at the grower's row.
        // What it pins is that the second of two empty components moves behind the first's new rows.
        [Test]
        public void Given_TwoEmptyComponents_When_TheFirstGrowsAndThenTheSecond_Then_TheSecondsRowsLandBehind()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TwoEmptiesHostRender, key: "host"));
            s_setFirstRows.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            s_setSecondRows.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(_root.Q(name: "box")), Is.EqualTo("lead,first0,second0"));
        }

        // GREEN_ON_BASE(characterization): the base's shift never walks back to a sibling ahead of the grower.
        // What it pins is that the first of two empty components stays ahead of the second's new rows.
        [Test]
        public void Given_TwoEmptyComponents_When_TheSecondGrowsAndThenTheFirst_Then_TheFirstsRowsLandAhead()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(TwoEmptiesHostRender, key: "host"));
            s_setSecondRows.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            s_setFirstRows.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(_root.Q(name: "box")), Is.EqualTo("lead,first0,second0"));
        }

        private static Action<int> s_setEmptyOrder;
        private static bool s_holdEmpty;

        [Component]
        private static VNode SecondEmptyHolderRender() => V.Component(SecondEmptyRender, key: "second");

        [Component(Compiler = false)]
        private static VNode ReorderedEmptiesHostRender()
        {
            var (order, setOrder) = Hooks.UseState(0);
            s_setEmptyOrder = setOrder;
            var first = s_holdEmpty ? V.Component(EmptyHolderRender, key: "first") : V.Component(FirstEmptyRender, key: "first");
            var second = s_holdEmpty ? V.Component(SecondEmptyHolderRender, key: "second") : V.Component(SecondEmptyRender, key: "second");
            return V.Div(name: "box", children: order == 0
                ? new VNode[] { first, second }
                : new VNode[] { second, first });
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void Given_TwoEmptyComponentsReordered_When_TheyGrowIndependently_Then_TheirRowsFollowTheCommittedOrder(bool firstGrowsFirst, bool nested)
            => AssertReorderedEmptiesFollowTheCommittedOrder(firstGrowsFirst, nested);

        // GREEN_ON_BASE(characterization): the base already lands this one of the four growth orders.
        // It reverses the other three.
        [Test]
        public void Given_TwoHeldEmptyComponentsReordered_When_TheFirstGrowsFirst_Then_TheirRowsFollowTheCommittedOrder()
            => AssertReorderedEmptiesFollowTheCommittedOrder(firstGrowsFirst: true, nested: true);

        private void AssertReorderedEmptiesFollowTheCommittedOrder(bool firstGrowsFirst, bool nested)
        {
            // Arrange
            s_holdEmpty = nested;
            using var mounted = V.Mount(_root, V.Component(ReorderedEmptiesHostRender, key: "host"));
            s_setEmptyOrder.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            (firstGrowsFirst ? s_setFirstRows : s_setSecondRows).Invoke(1);
            mounted.FlushStateForTest();
            (firstGrowsFirst ? s_setSecondRows : s_setFirstRows).Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(_root.Q(name: "box")), Is.EqualTo("second0,first0"));
        }

        private static Action<int> s_setRetargeted;
        private static Action<int> s_setRetargetFirstRows;
        private static Action<int> s_setRetargetRowTick;
        private static VisualElement s_retargetElsewhere;

        [Component]
        private static VNode RetargetFirstRender()
        {
            var (rows, setRows) = Hooks.UseState(0);
            s_setRetargetFirstRows = setRows;
            return Rows("f", rows);
        }

        [Component]
        private static VNode RetargetRowRender()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setRetargetRowTick = setTick;
            return V.Label(name: "t-" + tick, key: "t");
        }

        [Component(Compiler = false)]
        private static VNode RetargetedPortalRender()
        {
            var (onTarget, setOnTarget) = Hooks.UseState(0);
            s_setRetargeted = setOnTarget;
            return V.Portal(onTarget == 1 ? s_portalTarget : s_retargetElsewhere,
                children: new VNode[] { V.Component(RetargetRowRender, key: "t") });
        }

        [Component]
        private static VNode RetargetFirstPortalRender()
            => V.Portal(s_portalTarget, children: new VNode[] { V.Component(RetargetFirstRender, key: "f") });

        [Component]
        private static VNode RetargetAheadHostRender()
            => V.Fragment(children: new VNode[]
            {
                V.Component(RetargetedPortalRender, key: "retargeted"),
                V.Component(RetargetFirstPortalRender, key: "first"),
            });

        // The retargeted Portal is declared ahead and mounts its range at the target's end, so its component
        // and the empty one share the target's first row while the committed order puts its component first.
        [Test]
        public void Given_APortalRetargetedOntoTheRowOfAnEmptyComponentDeclaredBehindIt_When_TheEmptyOneGrowsAndTheOtherRerenders_Then_ItRewritesItsOwnRow()
        {
            // Arrange
            s_portalTarget = new VisualElement();
            s_retargetElsewhere = new VisualElement();
            using var mounted = V.Mount(_root, V.Component(RetargetAheadHostRender, key: "host"));
            s_setRetargeted.Invoke(1);
            mounted.FlushStateForTest();
            s_setRetargetFirstRows.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            s_setRetargetRowTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(s_portalTarget), Is.EqualTo("f0,t-1"));
        }

        private static Action<int> s_setAheadOrder;

        [Component]
        private static VNode AheadOrderHostRender()
        {
            var (order, setOrder) = Hooks.UseState(0);
            s_setAheadOrder = setOrder;
            return V.Div(name: "box", children: order == 0
                ? new VNode[] { V.Component(GrowerRender, key: "grower"), V.Component(FirstEmptyRender, key: "empty") }
                : new VNode[] { V.Component(FirstEmptyRender, key: "empty"), V.Component(GrowerRender, key: "grower") });
        }

        [Test]
        public void Given_AnEmptyComponentReorderedAheadOfOneHoldingRows_When_TheOtherGrows_Then_TheEmptyOnesRowsLandAhead()
        {
            // Arrange — created with the grower first, then reordered so the empty one sits ahead of it at the
            // same row.
            using var mounted = V.Mount(_root, V.Component(AheadOrderHostRender, key: "host"));
            s_setAheadOrder.Invoke(1);
            mounted.FlushStateForTest();
            s_setGrowerRows.Invoke(3);
            mounted.FlushStateForTest();

            // Act
            s_setFirstRows.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(_root.Q(name: "box")), Is.EqualTo("first0,grow0,grow1,grow2"));
        }

        [Component]
        private static VNode RowsAheadOfAnEmptyHostRender()
            => V.Div(name: "box", children: new VNode[]
            {
                V.Component(SiblingRender, key: "sib"),
                V.Component(FirstEmptyRender, key: "empty"),
            });

        // GREEN_ON_BASE(characterization): the base's shift never walks back to a sibling ahead of the grower.
        // What it pins is that an empty component growing leaves one holding rows ahead of it where it is.
        [Test]
        public void Given_AComponentHoldingRowsAheadOfAnEmptyOne_When_TheEmptyOneGrows_Then_TheFirstRerendersItsOwnRow()
        {
            // Arrange
            using var mounted = V.Mount(_root, V.Component(RowsAheadOfAnEmptyHostRender, key: "host"));
            s_setFirstRows.Invoke(2);
            mounted.FlushStateForTest();

            // Act
            s_setSiblingTick.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(_root.Q(name: "box")), Is.EqualTo("sib-1,first0,first1"));
        }

        #endregion

        #region An empty Portal range at the start of the one that changes

        private static Action<int> s_setAheadPortalRows;
        private static Action<int> s_setSeamGrowerRows;
        private static Action<int> s_setSeamOwnRows;
        private static Action<int> s_setShowSeamGrower;

        [Component]
        private static VNode EmptyAheadPortalRender()
        {
            var (rows, setRows) = Hooks.UseState(0);
            s_setAheadPortalRows = setRows;
            var children = new VNode[rows];
            for (var i = 0; i < rows; i++) children[i] = V.Label(name: "a" + i, key: "a" + i);
            return V.Portal(s_portalTarget, children: children);
        }

        [Component]
        private static VNode SeamGrowerRender()
        {
            var (rows, setRows) = Hooks.UseState(1);
            s_setSeamGrowerRows = setRows;
            return Rows("b", rows);
        }

        [Component]
        private static VNode SeamGrowerPortalRender()
            => V.Portal(s_portalTarget, children: new VNode[] { V.Component(SeamGrowerRender, key: "grower") });

        [Component]
        private static VNode SeamOwnRowsPortalRender()
        {
            var (rows, setRows) = Hooks.UseState(1);
            s_setSeamOwnRows = setRows;
            var children = new VNode[rows];
            for (var i = 0; i < rows; i++) children[i] = V.Label(name: "b" + i, key: "b" + i);
            return V.Portal(s_portalTarget, children: children);
        }

        [Component]
        private static VNode EmptyAheadOfComponentGrowthHostRender()
            => V.Fragment(children: new VNode[]
            {
                V.Component(EmptyAheadPortalRender, key: "ahead"),
                V.Component(SeamGrowerPortalRender, key: "grower"),
            });

        [Component]
        private static VNode EmptyAheadOfOwnRowsGrowthHostRender()
            => V.Fragment(children: new VNode[]
            {
                V.Component(EmptyAheadPortalRender, key: "ahead"),
                V.Component(SeamOwnRowsPortalRender, key: "grower"),
            });

        [Component]
        private static VNode EmptyAheadOfLeavingPortalHostRender()
        {
            var (shown, setShown) = Hooks.UseState(1);
            s_setShowSeamGrower = setShown;
            return V.Fragment(children: new VNode[]
            {
                V.Component(EmptyAheadPortalRender, key: "ahead"),
                shown == 1 ? V.Component(SeamOwnRowsPortalRender, key: "leaving") : null,
                V.Component(PortalWithChildRender, key: "behind"),
            });
        }

        [Test]
        public void Given_AnEmptyPortalAheadOfOneThatUnmounted_When_TheEmptyOneGainsTwoRows_Then_TheyLandAheadOfTheRest()
        {
            // Arrange
            s_portalTarget = new VisualElement();
            using var mounted = V.Mount(_root, V.Component(EmptyAheadOfLeavingPortalHostRender, key: "host"));
            s_setShowSeamGrower.Invoke(0);
            mounted.FlushStateForTest();
            s_setAheadPortalRows.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            s_setAheadPortalRows.Invoke(2);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(s_portalTarget), Is.EqualTo("a0,a1,b-0"));
        }

        // GREEN_ON_BASE(characterization): the base moves no Portal range for a component's own growth.
        // What it pins is that the growth, which moves the ranges behind it now, leaves an empty one ahead.
        [Test]
        public void Given_AnEmptyPortalAheadOfOneWhoseComponentGrew_When_TheEmptyOneGainsARow_Then_ItLandsAhead()
        {
            // Arrange — both ranges start at the target's first row; the empty one was mounted first.
            s_portalTarget = new VisualElement();
            using var mounted = V.Mount(_root, V.Component(EmptyAheadOfComponentGrowthHostRender, key: "host"));
            s_setSeamGrowerRows.Invoke(2);
            mounted.FlushStateForTest();

            // Act
            s_setAheadPortalRows.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(s_portalTarget), Is.EqualTo("a0,b0,b1"));
        }

        [Test]
        public void Given_AnEmptyPortalAheadOfOneWhoseOwnChildrenGrew_When_TheEmptyOneGainsARow_Then_ItLandsAhead()
        {
            // Arrange
            s_portalTarget = new VisualElement();
            using var mounted = V.Mount(_root, V.Component(EmptyAheadOfOwnRowsGrowthHostRender, key: "host"));
            s_setSeamOwnRows.Invoke(2);
            mounted.FlushStateForTest();

            // Act
            s_setAheadPortalRows.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(s_portalTarget), Is.EqualTo("a0,b0,b1"));
        }

        private static Action<int> s_setEmptyGrowerRows;
        private static Action<int> s_setEmptyAheadChildRows;
        private static Action<int> s_setOwnRowsFromEmpty;

        [Component]
        private static VNode EmptyGrowerRender()
        {
            var (rows, setRows) = Hooks.UseState(0);
            s_setEmptyGrowerRows = setRows;
            return Rows("b", rows);
        }

        [Component]
        private static VNode EmptyGrowerPortalRender()
            => V.Portal(s_portalTarget, children: new VNode[] { V.Component(EmptyGrowerRender, key: "grower") });

        [Component]
        private static VNode EmptyAheadChildRender()
        {
            var (rows, setRows) = Hooks.UseState(0);
            s_setEmptyAheadChildRows = setRows;
            return Rows("a", rows);
        }

        [Component]
        private static VNode EmptyAheadChildPortalRender()
            => V.Portal(s_portalTarget, children: new VNode[] { V.Component(EmptyAheadChildRender, key: "child") });

        [Component]
        private static VNode OwnRowsFromEmptyPortalRender()
        {
            var (rows, setRows) = Hooks.UseState(0);
            s_setOwnRowsFromEmpty = setRows;
            var children = new VNode[rows];
            for (var i = 0; i < rows; i++) children[i] = V.Label(name: "b" + i, key: "b" + i);
            return V.Portal(s_portalTarget, children: children);
        }

        [Component]
        private static VNode EmptyAheadOfEmptyGrowerHostRender()
            => V.Fragment(children: new VNode[]
            {
                V.Component(EmptyAheadPortalRender, key: "ahead"),
                V.Component(EmptyGrowerPortalRender, key: "grower"),
            });

        [Component]
        private static VNode EmptyChildAheadOfEmptyOwnRowsHostRender()
            => V.Fragment(children: new VNode[]
            {
                V.Component(EmptyAheadChildPortalRender, key: "ahead"),
                V.Component(OwnRowsFromEmptyPortalRender, key: "grower"),
            });

        private static Action<int> s_setBehindEmptyRows;

        [Component]
        private static VNode BehindEmptyPortalRender()
        {
            var (rows, setRows) = Hooks.UseState(0);
            s_setBehindEmptyRows = setRows;
            var children = new VNode[rows];
            for (var i = 0; i < rows; i++) children[i] = V.Label(name: "b" + i, key: "b" + i);
            return V.Portal(s_portalTarget, children: children);
        }

        [Component]
        private static VNode EmptyGrowerAheadOfEmptyHostRender()
            => V.Fragment(children: new VNode[]
            {
                V.Component(EmptyAheadChildPortalRender, key: "grower"),
                V.Component(BehindEmptyPortalRender, key: "behind"),
            });

        [Test]
        public void Given_TwoEmptyPortalsAndTheFirstsComponentGrew_When_TheSecondGainsARow_Then_ItLandsBehind()
        {
            // Arrange
            s_portalTarget = new VisualElement();
            using var mounted = V.Mount(_root, V.Component(EmptyGrowerAheadOfEmptyHostRender, key: "host"));
            s_setEmptyAheadChildRows.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            s_setBehindEmptyRows.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(s_portalTarget), Is.EqualTo("a0,b0"));
        }

        private static Action<int> s_setLateRangeOnTarget;
        private static Action<int> s_setLateRangeRows;

        // Declared first, and retargeted onto the target after the Portal behind it has mounted there, so its
        // range sits after that Portal's start although its placeholder comes first.
        [Component(Compiler = false)]
        private static VNode LateRangePortalRender()
        {
            var (onTarget, setOnTarget) = Hooks.UseState(0);
            var (rows, setRows) = Hooks.UseState(1);
            s_setLateRangeOnTarget = setOnTarget;
            s_setLateRangeRows = setRows;
            var children = new VNode[rows];
            for (var i = 0; i < rows; i++) children[i] = V.Label(name: "o" + i, key: "o" + i);
            return V.Portal(onTarget == 1 ? s_portalTarget : s_retargetElsewhere, children: children);
        }

        [Component]
        private static VNode LateRangeAheadOfEmptyGrowerHostRender()
            => V.Fragment(children: new VNode[]
            {
                V.Component(LateRangePortalRender, key: "late"),
                V.Component(EmptyGrowerPortalRender, key: "grower"),
            });

        [Component]
        private static VNode LateEmptyRangeAheadOfOwnRowsHostRender()
            => V.Fragment(children: new VNode[]
            {
                V.Component(LateRangePortalRender, key: "late"),
                V.Component(SeamOwnRowsPortalRender, key: "rows"),
            });

        [Test]
        public void Given_ARangeHoldingRowsRetargetedOntoAnEmptyOnesRow_When_TheEmptyOnesComponentGrowsAndTheRangeGrows_Then_ItsRowsStayBehind()
        {
            // Arrange
            s_portalTarget = new VisualElement();
            s_retargetElsewhere = new VisualElement();
            using var mounted = V.Mount(_root, V.Component(LateRangeAheadOfEmptyGrowerHostRender, key: "host"));
            s_setLateRangeOnTarget.Invoke(1);
            mounted.FlushStateForTest();
            s_setEmptyGrowerRows.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            s_setLateRangeRows.Invoke(2);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(s_portalTarget), Is.EqualTo("b0,o0,o1"));
        }

        // GREEN_ON_BASE(characterization): the base moves every range at or after the growing one's start.
        // What it pins is that a range at the grown one's end moves, whichever Portal is declared first.
        [Test]
        public void Given_AnEmptyRangeRetargetedOntoTheEndOfAnotherRange_When_TheOtherGrowsAndTheEmptyOneGainsARow_Then_ItLandsBehind()
        {
            // Arrange
            s_portalTarget = new VisualElement();
            s_retargetElsewhere = new VisualElement();
            using var mounted = V.Mount(_root, V.Component(LateEmptyRangeAheadOfOwnRowsHostRender, key: "host"));
            s_setLateRangeRows.Invoke(0);
            mounted.FlushStateForTest();
            s_setLateRangeOnTarget.Invoke(1);
            mounted.FlushStateForTest();
            s_setSeamOwnRows.Invoke(2);
            mounted.FlushStateForTest();

            // Act
            s_setLateRangeRows.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(s_portalTarget), Is.EqualTo("b0,b1,o0"));
        }

        // GREEN_ON_BASE(characterization): the base moves no Portal range for a component's own growth.
        // What it pins is that the growth leaves an empty range declared ahead where it is.
        [Test]
        public void Given_TwoEmptyPortalsAndTheSecondsComponentGrew_When_TheFirstGainsARow_Then_ItLandsAhead()
        {
            // Arrange — both ranges are empty at the target's first row; the first is declared ahead.
            s_portalTarget = new VisualElement();
            using var mounted = V.Mount(_root, V.Component(EmptyAheadOfEmptyGrowerHostRender, key: "host"));
            s_setEmptyGrowerRows.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            s_setAheadPortalRows.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(s_portalTarget), Is.EqualTo("a0,b0"));
        }

        // GREEN_ON_BASE(characterization): the base's Portal patch moves no component's start.
        // What it pins is that the patch leaves the component of an empty range declared ahead where it is.
        [Test]
        public void Given_TwoEmptyPortalsAndTheSecondsOwnChildrenGrew_When_TheFirstsComponentGrows_Then_ItsRowLandsAhead()
        {
            // Arrange
            s_portalTarget = new VisualElement();
            using var mounted = V.Mount(_root, V.Component(EmptyChildAheadOfEmptyOwnRowsHostRender, key: "host"));
            s_setOwnRowsFromEmpty.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            s_setEmptyAheadChildRows.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(s_portalTarget), Is.EqualTo("a0,b0"));
        }

        private static Action<int> s_setWrittenAheadShown;
        private static Action<int> s_setWrittenAheadRows;
        private static Action<int> s_setWrittenBehindRows;

        [Component]
        private static VNode WrittenAheadChildRender()
        {
            var (rows, setRows) = Hooks.UseState(0);
            s_setWrittenAheadRows = setRows;
            return Rows("a", rows);
        }

        [Component]
        private static VNode WrittenBehindChildRender()
        {
            var (rows, setRows) = Hooks.UseState(0);
            s_setWrittenBehindRows = setRows;
            return Rows("b", rows);
        }

        [Component]
        private static VNode WrittenAheadPortalRender()
            => V.Portal(s_portalTarget, children: new VNode[] { V.Component(WrittenAheadChildRender, key: "child") });

        [Component]
        private static VNode WrittenBehindPortalRender()
            => V.Portal(s_portalTarget, children: new VNode[] { V.Component(WrittenBehindChildRender, key: "child") });

        // The Portal written ahead appears after the one behind it has mounted, and the one behind it is placed
        // by the walk of an element of its own, so the fiber chain puts the one behind first.
        [Component]
        private static VNode WrittenAheadLaterHostRender()
        {
            var (aheadShown, setAheadShown) = Hooks.UseState(0);
            s_setWrittenAheadShown = setAheadShown;
            return V.Fragment(children: new VNode[]
            {
                aheadShown == 1 ? V.Component(WrittenAheadPortalRender, key: "ahead") : null,
                V.Div(key: "box", children: new VNode[] { V.Component(WrittenBehindPortalRender, key: "behind") }),
            });
        }

        // GREEN_ON_BASE(characterization): the base's growth moves only siblings in the chain and no range.
        // What it pins is that the growth order and the Portals' written order agree across two walks.
        [Test]
        public void Given_AnEmptyPortalWrittenAheadAppearingLater_When_TheOneBehindAndThenItGrow_Then_TheRowsFollowTheWrittenOrder()
        {
            // Arrange
            s_portalTarget = new VisualElement();
            using var mounted = V.Mount(_root, V.Component(WrittenAheadLaterHostRender, key: "host"));
            s_setWrittenAheadShown.Invoke(1);
            mounted.FlushStateForTest();
            s_setWrittenBehindRows.Invoke(1);
            mounted.FlushStateForTest();

            // Act
            s_setWrittenAheadRows.Invoke(1);
            mounted.FlushStateForTest();

            // Assert
            Assert.That(Names(s_portalTarget), Is.EqualTo("a0,b0"));
        }

        #endregion

        #region A boundary catching in a walk that created a component ahead of it

        private static Action<int> s_setMidWalkOrder;
        private static bool s_midWalkThrows;

        [Component(Compiler = false)]
        private static VNode MidWalkThrowerRender()
        {
            if (s_midWalkThrows) throw new InvalidOperationException(ThrowMessage);
            return V.Label(name: "thrower", key: "thrower");
        }

        [Component(IsErrorBoundary = true)]
        private static VNode MidWalkGuardRender()
        {
            Hooks.UseFallback(_ => V.Label(name: "g-fallback", key: "fallback"));
            return V.Fragment(children: new VNode[]
            {
                V.Label(name: "ga", key: "ga"),
                V.Div(name: "gb", key: "gb", children: new VNode[] { V.Component(MidWalkThrowerRender, key: "t") }),
            });
        }

        [Component]
        private static VNode MidWalkCRender() => V.Label(name: "c", key: "c");

        [Component]
        private static VNode MidWalkXRender() => V.Label(name: "x", key: "x");

        [Component]
        private static VNode MidWalkHostRender()
        {
            var (order, setOrder) = Hooks.UseState(0);
            s_setMidWalkOrder = setOrder;
            return V.Div(name: "box", children: order == 0
                ? new VNode[] { V.Component(MidWalkGuardRender, key: "guard"), V.Component(MidWalkCRender, key: "c") }
                : new VNode[]
                {
                    V.Component(MidWalkCRender, key: "c"),
                    V.Component(MidWalkXRender, key: "x"),
                    V.Component(MidWalkGuardRender, key: "guard"),
                });
        }

        // GREEN_ON_BASE(characterization): the base's fallback swap passes no slot limit and removes both rows.
        // What it pins is that the limit the swap takes now is not cut short by a component no walk has placed.
        [Test]
        public void Given_AWalkThatCreatedAComponentAheadOfABoundary_When_TheBoundaryCatches_Then_ItsFallbackReplacesBothItsRows()
        {
            // Arrange — the walk emits c, creates x, and then reaches the boundary, whose own child throws.
            using var mounted = V.Mount(_root, V.Component(MidWalkHostRender, key: "host"));
            s_midWalkThrows = true;

            // Act
            s_setMidWalkOrder.Invoke(1);
            mounted.FlushStateForTest();

            // Assert — what the aborted walk places is ErrorBoundaryTests' subject; this pins the boundary's rows.
            Assert.That(
                Names(_root.Q(name: "box")).Split(','),
                Has.Member("g-fallback").And.No.Member("ga").And.No.Member("gb"));
        }

        #endregion
    }
}
