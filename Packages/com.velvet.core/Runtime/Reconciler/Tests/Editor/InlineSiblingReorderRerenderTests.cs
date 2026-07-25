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
    /// Pins slot-bound resolution for an inline fiber that a keyed reorder displaced. The fiber
    /// sibling chain is creation order and a reorder does not resync it, so the chain-next sibling
    /// can sit visually BEFORE the fiber; bounding the fiber's own reconcile by that sibling's slot
    /// start produced slotLimit &lt; slotStart, every slot in the fiber's range looked missing, and an
    /// independent re-render (its own setState — not a parent-driven render) inserted a brand-new
    /// element while the stale one stayed: a permanent duplicate no future reconcile removes. The
    /// bound must come from the nearest co-located slot start beyond the fiber's own, regardless of
    /// chain position.
    /// </summary>
    [TestFixture]
    internal sealed class InlineSiblingReorderRerenderTests
    {
        private readonly record struct OrderState(string Order);

        private sealed class OrderStore : Store<OrderState>
        {
            public OrderStore() : base(new OrderState("abc")) { }
            public void Set(string order) => SetState(_ => new OrderState(order));
            protected override void ResetCore() => SetState(_ => new OrderState("abc"));
        }

        private static OrderStore s_store;

        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
        }

        [Component]
        private static VNode RowA()
        {
            var (count, setCount) = Hooks.UseState(0);
            return V.Button(name: "btn-a", text: "a" + count, onClick: () => setCount.Invoke(c => c + 1));
        }

        [Component]
        private static VNode RowB()
        {
            var (count, setCount) = Hooks.UseState(0);
            return V.Button(name: "btn-b", text: "b" + count, onClick: () => setCount.Invoke(c => c + 1));
        }

        [Component]
        private static VNode RowC()
        {
            var (count, setCount) = Hooks.UseState(0);
            return V.Button(name: "btn-c", text: "c" + count, onClick: () => setCount.Invoke(c => c + 1));
        }

        [Component]
        private static VNode ReorderList()
        {
            var order = Hooks.UseStore(s_store, s => s.Order);
            var rows = new List<VNode>();
            foreach (var id in order)
            {
                rows.Add(id switch
                {
                    'a' => V.Component(RowA, key: "a"),
                    'b' => V.Component(RowB, key: "b"),
                    _ => V.Component(RowC, key: "c"),
                });
            }
            return V.Div(name: "container", children: rows.ToArray());
        }

        [Test]
        public void Given_KeyedInlineComponentsWereReordered_When_TheDisplacedFiberRerendersOnItsOwn_Then_NoDuplicateElementAppears()
        {
            // Arrange — mount [a,b,c], then reorder to [b,c,a] so fiber a's creation-order successor
            // (b) now sits visually before it.
            using var store = new OrderStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(ReorderList, key: "list"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            store.Set("bca");
            scheduler.DrainImmediateForTest();
            var container = _root.Q<VisualElement>("container");
            Assume.That(container.childCount, Is.EqualTo(3), "Precondition: the reorder itself commits cleanly");

            // Act — fiber a re-renders on its own via its click handler (a discrete-event flush,
            // bypassing the parent).
            _root.Q<Button>("btn-a").SimulateClick();

            // Assert — the displaced fiber patched its own slot; no ghost duplicate was inserted.
            Assert.AreEqual(3, container.childCount);
        }

        [Test]
        public void Given_KeyedInlineComponentsWereReordered_When_TheDisplacedFiberRerendersOnItsOwn_Then_ItsOwnRowIsUpdatedInPlace()
        {
            // Arrange
            using var store = new OrderStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(ReorderList, key: "list"));
            var scheduler = mounted.Root.Reconciler.Context.BatchScheduler;
            store.Set("bca");
            scheduler.DrainImmediateForTest();

            // Act
            _root.Q<Button>("btn-a").SimulateClick();

            // Assert — exactly one btn-a exists and it carries the bumped state.
            var buttons = _root.Query<Button>(name: "btn-a").ToList();
            Assert.That((buttons.Count, buttons[0].text), Is.EqualTo((1, "a1")));
        }
    }

    /// <summary>
    /// Records the positional (unkeyed) reconciliation semantics for STATEFUL siblings: when same-type
    /// children carry no key, their hook state is bound to array POSITION, not identity. Inserting a sibling at
    /// the head shifts every subsequent position by one, so each fiber's state stays at its old index and
    /// therefore lands on a different conceptual child — the classic "state bleeds by one" bug that motivates
    /// using keys. <see cref="ReconcilerIndexedTests"/> only pins host-element type-match patch vs. type-mismatch
    /// replace per index; it never exercises stateful-component hook state moving with position. This fixture
    /// fills that gap.
    ///
    /// These are parity tests written against the canonical declarative-UI behaviour, so they are expected GREEN: Velvet's
    /// ChildReconciler indexed path should bind hook state to the slot index. This case is
    /// also an unverified divergence candidate — if Velvet shifts state differently (e.g. discards or
    /// mis-assigns the trailing slot), the assertion turns RED and exposes the behavioural difference.
    /// GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class PositionalStateBleedParityTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_insertHead = default;
        }

        private static StateUpdater<bool> s_insertHead;

        // A stateful child with NO key, used purely positionally. Its own count is rendered as the button's
        // text and a click increments it. Several of these sit side by side so an insertion at the head can
        // shift their array positions.
        [Component]
        private static VNode CountingChild()
        {
            var (count, setCount) = Hooks.UseState(0);
            return V.Button(text: count.ToString(), onClick: () => setCount.Invoke(c => c + 1));
        }

        // Parent renders the counting children UNKEYED. Before insertion it renders [X, Y]; a click on
        // "insert-head" flips state so it renders [New, X, Y] — all same type, all keyless — which is the
        // positional-insertion scenario.
        [Component]
        private static VNode HeadInsertingParent()
        {
            var (inserted, setInserted) = Hooks.UseState(false);
            s_insertHead = setInserted;

            var children = new List<VNode>
            {
                V.Button(name: "insert-head", onClick: () => setInserted.Invoke(_ => true)),
            };
            if (inserted)
            {
                children.Add(V.Component(CountingChild));
            }
            children.Add(V.Component(CountingChild));
            children.Add(V.Component(CountingChild));

            return V.Div(name: "parent", children: children.ToArray());
        }

        // Returns the counting children's button texts in document (tree) order, excluding the control button.
        private static string[] CountingTexts(VisualElement root) =>
            root.Query<Button>()
                .ToList()
                .Where(b => b.name != "insert-head")
                .Select(b => b.text)
                .ToArray();

        [Test]
        public void Given_UnkeyedStatefulSiblings_When_ElementInsertedAtHead_Then_StateShiftsByPosition()
        {
            // Arrange — two unkeyed stateful siblings advanced to counts [1, 2] by position.
            using var mounted = V.Mount(_root, V.Component(HeadInsertingParent, key: "parent"));
            var counters = _root.Query<Button>().ToList().Where(b => b.name != "insert-head").ToList();
            counters[0].SimulateClick();                 // X -> 1
            counters[1].SimulateClick();                 // Y -> 1
            counters[1].SimulateClick();                 // Y -> 2
            Assume.That(CountingTexts(_root), Is.EqualTo(new[] { "1", "2" }),
                "Precondition: the two unkeyed siblings hold counts 1 and 2 by position");

            // Act — a new sibling is inserted at the head, shifting every following position by one.
            _root.Q<Button>("insert-head").SimulateClick();

            // Assert — hook state stayed bound to position: slot 0 keeps 1, slot 1 keeps 2, and the new trailing
            // slot is fresh at 0. The old siblings' state has bled forward by one conceptual child.
            Assert.That(CountingTexts(_root), Is.EqualTo(new[] { "1", "2", "0" }));
        }
    }

    /// <summary>
    /// Two DIFFERENT function components at the same tree position must remount instead of
    /// patching. Velvet matches ComponentNodes by C# type in <see cref="ReconcileKeying.CanPatch"/>, but
    /// every <c>[Component]</c> function compiles to the same CLR type (<c>ComponentNode</c>), so the
    /// patch-compatibility decision must additionally compare component IDENTITY
    /// (<c>ComponentNode.ResolvedIdentity</c>, i.e. the <c>Body.Method</c>). Without that, A's element could
    /// be patched as B at the same slot rather than A being unmounted and B mounted fresh.
    /// </summary>
    /// <remarks>
    /// The production expansion path matches components by identity in <c>ComponentRegistry.GetOrCreateInline</c>,
    /// so today this <c>CanPatch</c> branch is latent / defense-in-depth. The first two tests guard the
    /// predicate directly; the behavioral test asserts the expected outcome (fresh state on swap).
    /// </remarks>
    [TestFixture]
    internal sealed class ComponentIdentityPatchParityTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            ResetBeta();
        }

        [Test]
        public void Given_TwoDifferentComponents_When_CanPatch_Then_NotPatchCompatible()
        {
            // Arrange — two different [Component] functions both produce a ComponentNode of the same CLR type.
            var alpha = V.Component(AlphaRender, key: "slot");
            var beta = V.Component(BetaRender, key: "slot");
            Assume.That(alpha.GetType(), Is.EqualTo(beta.GetType()),
                "Precondition: distinct components share the same CLR type, so type alone cannot tell them apart");
            Assume.That(alpha.ResolvedIdentity, Is.Not.EqualTo(beta.ResolvedIdentity),
                "Precondition: distinct components have distinct identities");

            // Act / Assert — different identity must not patch (it remounts).
            Assert.That(ReconcileKeying.CanPatch(alpha, beta), Is.False);
        }

        [Test]
        public void Given_SameComponent_When_CanPatch_Then_PatchCompatible()
        {
            // Arrange — same [Component] function across two renders.
            var first = V.Component(AlphaRender, key: "slot");
            var second = V.Component(AlphaRender, key: "slot");

            // Act / Assert — same identity still patches (no regression for the common path).
            Assert.That(ReconcileKeying.CanPatch(first, second), Is.True);
        }

        [Test]
        public void Given_MountedComponent_When_DifferentComponentReconciledAtSameKey_Then_BetaMountsFresh()
        {
            // The production expansion path matches components by identity in ComponentRegistry, so a real
            // reconcile resolves a component swap there rather than through CanPatch (which is why the CanPatch
            // branch is latent). This test pins the OUTCOME that path already delivers: a different
            // component at the same keyed position renders fresh with its own state, never Alpha's render body.
            var reconciler = new Reconciler();
            var alphaTree = new VNode[] { V.Component(AlphaRender, key: "slot") };
            reconciler.Reconcile(_root, Array.Empty<VNode>(), alphaTree);
            Assume.That(_root.childCount, Is.EqualTo(1), "Precondition: Alpha mounted one element");
            Assume.That(((Label)_root.ElementAt(0)).text, Is.EqualTo("alpha-initial"),
                "Precondition: Alpha rendered its own seed state");

            // Act — swap to a DIFFERENT component at the SAME keyed position.
            var betaTree = new VNode[] { V.Component(BetaRender, key: "slot") };
            reconciler.Reconcile(_root, alphaTree, betaTree);

            // Assert — Beta runs its own render with its own fresh state; Alpha's body is not reused.
            Assert.That(s_betaRenderCount, Is.GreaterThanOrEqualTo(1), "Beta must have rendered");
            Assert.That(((Label)_root.ElementAt(0)).text, Is.EqualTo("beta-initial"),
                "The mounted element must reflect Beta's own fresh state, not Alpha's");

            reconciler.Dispose();
        }

        #region Alpha component

        [Component]
        private static VNode AlphaRender()
        {
            var (text, _) = Hooks.UseState("alpha-initial");
            return V.Label(text: text);
        }

        #endregion

        #region Beta component

        private static int s_betaRenderCount;

        private static void ResetBeta() => s_betaRenderCount = 0;

        [Component]
        private static VNode BetaRender()
        {
            s_betaRenderCount++;
            var (text, _) = Hooks.UseState("beta-initial");
            return V.Label(text: text);
        }

        #endregion
    }

    /// <summary>
    /// Pins the duplicate sibling-key guard for inline component nodes. Two same-identity siblings
    /// sharing one explicit key resolve to the SAME registry fiber — the leaf-level keyed diff warns
    /// on duplicates, but the component path silently expanded the one fiber once per sibling: its
    /// DOM output was emitted twice while the fiber's slot bookkeeping tracked only the last
    /// position (and both copies shared one hook state), so a later re-render patched one copy and
    /// stranded the other. The repeat must warn and be skipped; two independent instances require
    /// unique keys, exactly as the reachable V.List keySelector documentation implies.
    /// </summary>
    [TestFixture]
    internal sealed class DuplicateInlineComponentKeyTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
        }

        [Component]
        private static VNode FooRow()
        {
            return V.Label(text: "foo");
        }

        [Component]
        private static VNode DupHost()
        {
            return V.Div(name: "dup-host", children: new VNode[]
            {
                V.Component(FooRow, key: "x"),
                V.Component(FooRow, key: "x"),
            });
        }

        [Test]
        public void Given_TwoSiblingComponentsWithTheSameKey_When_Mounted_Then_OnlyOneInstanceCommits()
        {
            // Arrange — the duplicate is reported (LogAssert also fails if no warning fires).
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Duplicate component key"));

            // Act
            using var mounted = V.Mount(_root, V.Component(DupHost, key: "host"));

            // Assert — one fiber, one committed copy; the repeat did not double-emit its DOM.
            Assert.AreEqual(1, _root.Q<VisualElement>("dup-host").childCount);
        }
    }

    /// <summary>
    /// Parity coverage for the <c>cond ? node : null</c> conditional-render idiom at the reconciler boundary,
    /// driven through a REAL discrete click (<see cref="Button.SimulateClick"/>) so the toggle commits inside the
    /// event boundary the way production takes. A <c>null</c> child renders as nothing: it produces no host node and
    /// leaves no placeholder, and flipping the condition mounts/unmounts only that child while its siblings keep
    /// their instances. These pin: a false <c>null</c> child creates no host element and is mounted from nothing
    /// when the condition turns true; and a KEYED child nulled out among KEYED siblings is the only one removed,
    /// with the neighbours reused (same instance). Identity preservation across a middle removal is the keyed
    /// guarantee — unkeyed siblings reconcile by position and would re-purpose the trailing node instead, which is
    /// the very lesson keys exist to fix. GWT, one assert per case. If a case goes RED, Velvet diverges from the expected
    /// semantics (e.g. a false <c>null</c> child leaving an empty placeholder element, or a keyed sibling being re-created
    /// instead of preserved).
    /// </summary>
    [TestFixture]
    internal sealed class ConditionalNullChildParityTests
    {
        private VisualElement _root;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_setShow = default;
        }

        private static StateUpdater<bool> s_setShow;

        // A parent that always renders one Label and conditionally renders a second via `show ? node : null`.

        [Component]
        private static VNode OptionalTailParent()
        {
            var (show, setShow) = Hooks.UseState(false);
            s_setShow = setShow;
            return V.Div(name: "parent", children: new VNode[]
            {
                V.Label(name: "always", text: "A"),
                V.Button(name: "toggle", onClick: () => setShow.Invoke(s => !s)),
                show ? V.Label(name: "optional", text: "B") : null,
            });
        }

        [Test]
        public void Given_ConditionalNullChild_When_ConditionFlipsToTrue_Then_ChildMountsFromNothing()
        {
            // Arrange — a parent whose conditional child is currently `null` (show=false), so no host node exists for it.
            using var mounted = V.Mount(_root, V.Component(OptionalTailParent, key: "parent"));
            Assume.That(_root.Q<Label>("optional"), Is.Null, "Precondition: the null child created no host node");

            // Act — a click flips the condition to true.
            _root.Q<Button>("toggle").SimulateClick();

            // Assert — the previously-null child is mounted from nothing.
            Assert.IsNotNull(_root.Q<Label>("optional"));
        }

        // A parent whose three KEYED label siblings include a conditional middle child. Keys give the siblings a
        // stable identity, so removing the middle one preserves the neighbours' instances — the guarantee unkeyed
        // positional reconciliation does not provide (there the trailing node is re-purposed instead).

        [Component]
        private static VNode SandwichParent()
        {
            var (show, setShow) = Hooks.UseState(true);
            s_setShow = setShow;
            return V.Div(name: "parent", children: new VNode[]
            {
                V.Button(name: "toggle", onClick: () => setShow.Invoke(s => !s)),
                V.Fragment(new VNode[]
                {
                    V.Label(key: "a", name: "a", text: "A"),
                    show ? V.Label(key: "b", name: "b", text: "B") : null,
                    V.Label(key: "c", name: "c", text: "C"),
                }),
            });
        }

        [Test]
        public void Given_ConditionalChildAmongSiblings_When_ConditionFlipsToFalse_Then_OnlyThatChildUnmounts()
        {
            // Arrange — a parent rendering siblings A, B, C with B as the conditional child (show=true), recording A's and C's instances.
            using var mounted = V.Mount(_root, V.Component(SandwichParent, key: "parent"));
            var aBefore = _root.Q<Label>("a");
            var cBefore = _root.Q<Label>("c");
            Assume.That(_root.Q<Label>("b"), Is.Not.Null, "Precondition: the conditional child B is mounted");

            // Act — a click nulls out B (show=false).
            _root.Q<Button>("toggle").SimulateClick();

            // Assert — B is gone and only B is gone: the surviving Label siblings are exactly the original A and C instances.
            Assert.That(
                _root.Query<Label>().ToList(),
                Is.EqualTo(new[] { aBefore, cBefore }),
                "B removed; A and C preserved as the same instances");
        }
    }
}
