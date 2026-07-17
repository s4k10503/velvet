using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the recycle contract for factory-rented <see cref="FiberElementProps"/> bags across
    /// re-renders, in both directions.
    /// <list type="bullet">
    /// <item>Depth: a bag rented by a V.* factory for one committed tree must flow back to
    /// <see cref="VNodePool"/> when that tree is retired, wherever the element sits — at the top
    /// level of the fiber's tree, nested under an element, or under a Portal's children. A bag that
    /// never returns is pinned forever by the pool's ownership identity set (one stranded bag per
    /// render of that subtree) and starves the pool for sibling factories.</item>
    /// <item>Sharing: memoization legitimately carries the SAME node instances across consecutive
    /// trees (a memo hit, a deps-stable <c>Hooks.UseMemo</c> subtree toggled out of the output and
    /// back), so the recursive recycle must spare anything the committed state still reaches —
    /// returning it would let an unrelated mount rent and overwrite a live baseline.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class RenderedTreePropsRecycleTests
    {
        private const int RerenderCount = 20;

        // The pool's ownership identity set and pooled stack are deliberately private (production
        // code exposes no test hooks); the probes read them via reflection from the test side.
        private static int OwnedPropsCount()
        {
            var field = typeof(VNodePool).GetField("s_ownedProps", BindingFlags.NonPublic | BindingFlags.Static);
            return ((HashSet<FiberElementProps>)field.GetValue(null)).Count;
        }

        private static bool PropsPoolContains(FiberElementProps props)
        {
            var field = typeof(VNodePool).GetField("s_propsPool", BindingFlags.NonPublic | BindingFlags.Static);
            return ((Stack<FiberElementProps>)field.GetValue(null)).Contains(props);
        }

        private sealed class CounterStore : Store<int>
        {
            public CounterStore() : base(0) { }
            public void Increment() => SetState(x => x + 1);
            protected override void ResetCore() => SetState(_ => 0);
        }

        private static CounterStore s_store;
        private static VNode s_capturedMemoNode;

        private VisualElement _root;
        private VisualElement _portalTarget;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            _portalTarget = new VisualElement();
            FiberPortalRegistry.Clear();
            FiberPortalRegistry.Register("props-recycle-probe", _portalTarget);
            s_store = null;
            s_capturedMemoNode = null;
        }

        [TearDown]
        public void TearDown()
        {
            FiberPortalRegistry.Clear();
        }

        #region depth: retired bags return from every nesting position

        // Each body renders exactly one props-renting element (Button with text) so the ownership
        // set's growth isolates that single bag's return path. The counter is woven into the text
        // so every store write produces a genuinely different tree (no memo bail).

        [Component]
        private static VNode TopLevelRenter()
        {
            var count = Hooks.UseStore(s_store, x => x);
            return V.Button(text: "top-" + count);
        }

        [Component]
        private static VNode NestedRenter()
        {
            var count = Hooks.UseStore(s_store, x => x);
            return V.Div(name: "wrap", children: new VNode[] { V.Button(text: "nested-" + count) });
        }

        [Component]
        private static VNode PortalRenter()
        {
            var count = Hooks.UseStore(s_store, x => x);
            return V.Portal("props-recycle-probe", children: new VNode[] { V.Button(text: "portal-" + count) });
        }

        private int MeasureOwnedGrowth(System.Func<VNode> body)
        {
            using var store = new CounterStore();
            s_store = store;
            using var mounted = V.Mount(_root, body());
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            var baseline = OwnedPropsCount();
            for (var i = 0; i < RerenderCount; i++)
            {
                store.Increment();
                scheduler.DrainImmediateForTest();
            }
            return OwnedPropsCount() - baseline;
        }

        [Test]
        public void Given_ATopLevelPropsRentingElement_When_TheTreeRerendersRepeatedly_Then_TheOwnershipSetDoesNotGrowPerRender()
        {
            // Arrange / Act — twenty re-renders, each renting a fresh bag for the new tree.
            var growth = MeasureOwnedGrowth(() => V.Component(TopLevelRenter, key: "top"));

            // Assert — retired bags round-trip through the pool instead of accumulating one per render.
            Assert.That(growth, Is.LessThan(RerenderCount / 2),
                "A top-level element's rented props bag must return to the pool when its tree is retired");
        }

        [Test]
        public void Given_APropsRentingElementNestedUnderAnElement_When_TheTreeRerendersRepeatedly_Then_TheOwnershipSetDoesNotGrowPerRender()
        {
            // Arrange / Act
            var growth = MeasureOwnedGrowth(() => V.Component(NestedRenter, key: "nested"));

            // Assert
            Assert.That(growth, Is.LessThan(RerenderCount / 2),
                "A nested element's rented props bag must return to the pool when its tree is retired");
        }

        [Test]
        public void Given_APropsRentingElementUnderPortalChildren_When_TheTreeRerendersRepeatedly_Then_TheOwnershipSetDoesNotGrowPerRender()
        {
            // Arrange / Act
            var growth = MeasureOwnedGrowth(() => V.Component(PortalRenter, key: "portal"));

            // Assert
            Assert.That(growth, Is.LessThan(RerenderCount / 2),
                "A Portal-child element's rented props bag must return to the pool when its tree is retired");
        }

        #endregion

        #region sharing: committed state is spared by the sweep

        // A child whose only hook input never changes: the compiler's auto-memo hits on every
        // parent-driven re-render and returns the SAME committed tree instance, so the child's old
        // and new tree are one object graph and the retire sweep must spare all of it. The capture
        // runs only on the miss render (a hit returns the cached tree before the body executes).
        [Component]
        private static VNode AutoMemoHitChild()
        {
            var (staticValue, _) = Hooks.UseState(0);
            s_capturedMemoNode = V.Button(text: "auto-memo-" + staticValue);
            return s_capturedMemoNode;
        }

        [Component]
        private static VNode AutoMemoHitParent()
        {
            var count = Hooks.UseStore(s_store, x => x);
            return V.Div(name: "host", children: new VNode[]
            {
                V.Label(text: "count-" + count),
                V.Component(AutoMemoHitChild, key: "child"),
            });
        }

        [Test]
        public void Given_AChildWhoseAutoMemoHitsOnParentRerenders_When_TheChildOldTreeRetires_Then_TheSharedBagStaysOutOfThePool()
        {
            // Arrange — a mounted parent whose child returns the same cached tree on every re-render.
            using var store = new CounterStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(AutoMemoHitParent, key: "memo-host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            var sharedBag = ((ElementNode)s_capturedMemoNode).Props;
            Assume.That(sharedBag, Is.Not.Null, "Precondition: the memoized button rented a props bag");

            // Act — two re-renders, each retiring a child old tree that IS the committed tree.
            store.Increment();
            scheduler.DrainImmediateForTest();
            store.Increment();
            scheduler.DrainImmediateForTest();

            // Assert — the shared bag was never recycled out from under the committed tree.
            Assert.That(PropsPoolContains(sharedBag), Is.False,
                "A memo-shared node's props bag must not be returned while the committed tree still holds it");
        }

        // A deps-stable Hooks.UseMemo subtree that leaves the output on odd counts: the render that
        // hides it retires an old tree still embedding the memoized node, and the node re-enters the
        // output two renders later — so the slot-held instance must survive the sweep untouched.
        [Component]
        private static VNode ToggledMemoHost()
        {
            var count = Hooks.UseStore(s_store, x => x);
            var kept = Hooks.UseMemo(() => s_capturedMemoNode = V.Button(text: "kept"), 1);
            return V.Div(name: "toggle-host", children: new VNode[]
            {
                V.Label(text: "count-" + count),
                count % 2 == 0 ? kept : null,
            });
        }

        [Test]
        public void Given_AUseMemoHeldSubtreeToggledOutOfTheOutput_When_TheHidingRenderRetiresTheOldTree_Then_TheSlotHeldBagStaysOutOfThePool()
        {
            // Arrange — mounted with the memoized subtree visible (count 0).
            using var store = new CounterStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(ToggledMemoHost, key: "toggle-host"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            var slotHeldBag = ((ElementNode)s_capturedMemoNode).Props;
            Assume.That(slotHeldBag, Is.Not.Null, "Precondition: the memoized button rented a props bag");

            // Act — count 1 hides the subtree; the retiring old tree still embeds the slot-held node.
            store.Increment();
            scheduler.DrainImmediateForTest();

            // Assert — the UseMemo slot still owns the node for its comeback render, so its bag must
            // not have been recycled by the hiding render's sweep.
            Assert.That(PropsPoolContains(slotHeldBag), Is.False,
                "A UseMemo-held node's props bag must not be returned while the slot can re-emit it");
        }

        #endregion
    }
}
