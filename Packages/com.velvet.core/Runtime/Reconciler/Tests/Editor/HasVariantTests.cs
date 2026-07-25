using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the <c>has-[...]</c> variant — a parent styled by a DESCENDANT condition. Three forms:
    /// <c>has-[:checked]:</c> (any descendant control checked, driven by a bubbling
    /// <c>ChangeEvent&lt;bool&gt;</c>), <c>has-[:focus]:</c> (a descendant holds focus, driven by a bubbling
    /// <c>FocusInEvent</c> / <c>FocusOutEvent</c>), and <c>has-[.class]:</c> (a descendant carries a class,
    /// re-derived by the element's post-children pass). Off-panel: a descendant's event reaches the parent's
    /// own callback registry, so the bubbled signal is fired directly on the parent (the element whose
    /// manipulator owns it) — for focus-within the bubbled FocusIn carries the DESCENDANT as its target
    /// (SimulateBubbledEvent), since <c>:has(:focus)</c> matches a focused descendant, not the element itself.
    /// The payload is asserted via the parent's class list. Also covers the has- reactivity gap: a has-
    /// element's own post-children pass only re-derives the payload when the has- element ITSELF
    /// reconciles, so when a descendant component re-renders INDEPENDENTLY (its own store/state changes a
    /// descendant's class or a controlled field value, never re-rendering the enclosing has- element), only
    /// the settled-flush sweep (<c>FiberNodePatcher.RefreshHasVariants</c>, driven by <c>FiberRenderer</c>,
    /// walking UP the ancestor chain from the flushed region's root) keeps the payload in sync. GWT, one
    /// assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class HasVariantTests
    {
        private VisualElement _root;

        private readonly record struct ToggleState(bool Active);

        private sealed class ToggleStore : Store<ToggleState>
        {
            public ToggleStore() : base(new ToggleState(false)) { }
            public void SetActive(bool v) => SetState(_ => new ToggleState(v));
            protected override void ResetCore() => SetState(_ => new ToggleState(false));
        }

        private static ToggleStore s_store;

        [SetUp]
        public void SetUp()
        {
            _root = new VisualElement();
            s_store = null;
        }

        private static VisualElement Parent(ReconcilerScope scope) => scope.Root.Q<VisualElement>("parent");

        private static VisualElement Parent(VisualElement root) => root.Q<VisualElement>("parent");

        private static VisualElement Leaf(ReconcilerScope scope) => scope.Root.Q<VisualElement>("leaf");

        [Test]
        public void Given_HasCheckedParent_When_ADescendantIsMountedChecked_Then_PayloadApplied()
        {
            // Arrange/Act — a parent with has-[:checked]:bg-mark containing a Toggle mounted already-checked;
            // the attach-time descendant scan must light the payload (ChangeEvent only fires on a change).
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "has-[:checked]:bg-mark", name: "parent", children: new VNode[]
                {
                    V.Toggle(name: "leaf", value: true),
                }),
            });

            // Assert
            Assert.IsTrue(Parent(scope).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_HasCheckedParent_When_NoDescendantIsChecked_Then_PayloadAbsent()
        {
            // Arrange/Act — the sole descendant Toggle is unchecked.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "has-[:checked]:bg-mark", name: "parent", children: new VNode[]
                {
                    V.Toggle(name: "leaf", value: false),
                }),
            });

            // Assert
            Assert.IsFalse(Parent(scope).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_HasCheckedParent_When_ADescendantBecomesChecked_Then_PayloadApplied()
        {
            // Arrange — a parent with an unchecked descendant Toggle (payload off).
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "has-[:checked]:bg-mark", name: "parent", children: new VNode[]
                {
                    V.Toggle(name: "leaf", value: false),
                }),
            });
            var leaf = scope.Root.Q<Toggle>("leaf");
            Assume.That(Parent(scope).ClassListContains("bg-mark"), Is.False, "Precondition: payload off while unchecked");

            // Act — the descendant turns on and its ChangeEvent bubbles to the parent (re-scan finds it on).
            leaf.SetValueWithoutNotify(true);
            using (var evt = ChangeEvent<bool>.GetPooled()) Parent(scope).SimulateEvent(evt);

            // Assert
            Assert.IsTrue(Parent(scope).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_HasCheckedParentApplied_When_TheDescendantBecomesUnchecked_Then_PayloadRemoved()
        {
            // Arrange — a parent whose payload is on (descendant mounted checked).
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "has-[:checked]:bg-mark", name: "parent", children: new VNode[]
                {
                    V.Toggle(name: "leaf", value: true),
                }),
            });
            var leaf = scope.Root.Q<Toggle>("leaf");
            Assume.That(Parent(scope).ClassListContains("bg-mark"), Is.True, "Precondition: payload on while checked");

            // Act — the descendant turns off and its ChangeEvent bubbles to the parent (re-scan finds none on).
            leaf.SetValueWithoutNotify(false);
            using (var evt = ChangeEvent<bool>.GetPooled()) Parent(scope).SimulateEvent(evt);

            // Assert
            Assert.IsFalse(Parent(scope).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_HasFocusParent_When_ADescendantGainsFocus_Then_PayloadApplied()
        {
            // Arrange — a parent with has-[:focus]:bg-mark (payload off before any focus).
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "has-[:focus]:bg-mark", name: "parent", children: new VNode[]
                {
                    V.Div(name: "leaf"),
                }),
            });
            Assume.That(Parent(scope).ClassListContains("bg-mark"), Is.False, "Precondition: payload off before focus");

            // Act — a DESCENDANT gains focus; FocusIn bubbles to the parent carrying the descendant as target.
            using (var evt = FocusInEvent.GetPooled()) Parent(scope).SimulateBubbledEvent(evt, Leaf(scope));

            // Assert
            Assert.IsTrue(Parent(scope).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_HasFocusParent_When_TheElementItselfGainsFocus_Then_PayloadAbsent()
        {
            // Arrange — a parent with has-[:focus]:bg-mark; focus-within is descendant-only (CSS :has(:focus)).
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "has-[:focus]:bg-mark", name: "parent", children: new VNode[]
                {
                    V.Div(name: "leaf"),
                }),
            });

            // Act — the has- element ITSELF gains focus (FocusIn whose target is the element, not a descendant).
            using (var evt = FocusInEvent.GetPooled()) Parent(scope).SimulateBubbledEvent(evt, Parent(scope));

            // Assert — :has(:focus) matches only a focused DESCENDANT, so the element focusing itself must not light it.
            Assert.IsFalse(Parent(scope).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_HasFocusParentApplied_When_FocusLeavesTheSubtree_Then_PayloadRemoved()
        {
            // Arrange — a focus-within parent whose payload is on (a descendant holds focus).
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "has-[:focus]:bg-mark", name: "parent", children: new VNode[]
                {
                    V.Div(name: "leaf"),
                }),
            });
            using (var evt = FocusInEvent.GetPooled()) Parent(scope).SimulateBubbledEvent(evt, Leaf(scope));
            Assume.That(Parent(scope).ClassListContains("bg-mark"), Is.True, "Precondition: payload on while focused");

            // Act — focus leaves the subtree entirely (FocusOut with no related target inside it).
            using (var evt = FocusOutEvent.GetPooled()) Parent(scope).SimulateEvent(evt);

            // Assert
            Assert.IsFalse(Parent(scope).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_HasClassParent_When_ADescendantCarriesTheClass_Then_PayloadApplied()
        {
            // Arrange/Act — a parent with has-[.active]:bg-mark containing a descendant carrying `active`; the
            // post-children pass scans descendants and lights the payload.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "has-[.active]:bg-mark", name: "parent", children: new VNode[]
                {
                    V.Div(className: "active", name: "leaf"),
                }),
            });

            // Assert
            Assert.IsTrue(Parent(scope).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_HasClassParent_When_NoDescendantCarriesTheClass_Then_PayloadAbsent()
        {
            // Arrange/Act — no descendant carries `active`.
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "has-[.active]:bg-mark", name: "parent", children: new VNode[]
                {
                    V.Div(className: "inactive", name: "leaf"),
                }),
            });

            // Assert
            Assert.IsFalse(Parent(scope).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_HasClassParent_When_TheCarryingDescendantIsRemoved_Then_PayloadClears()
        {
            // Arrange — a parent whose payload is on because a descendant carries `active`.
            using var scope = new ReconcilerScope();
            var before = new VNode[]
            {
                V.Div(className: "has-[.active]:bg-mark", name: "parent", children: new VNode[]
                {
                    V.Div(className: "active", key: "a", name: "leaf"),
                }),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), before);
            Assume.That(Parent(scope).ClassListContains("bg-mark"), Is.True, "Precondition: payload on while descendant present");

            // Act — the carrying descendant is removed (the post-children pass re-derives from a fresh scan).
            scope.Reconciler.Reconcile(scope.Root, before, new VNode[]
            {
                V.Div(className: "has-[.active]:bg-mark", name: "parent", children: Array.Empty<VNode>()),
            });

            // Assert — the parent drops the payload (reactivity to descendant removal).
            Assert.IsFalse(Parent(scope).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_HasCheckedParentApplied_When_TheCheckedDescendantIsRemovedByReconcile_Then_PayloadClears()
        {
            // Arrange — a parent whose has-[:checked]: payload is on because it holds a checked Toggle.
            using var scope = new ReconcilerScope();
            var before = new VNode[]
            {
                V.Div(className: "has-[:checked]:bg-mark", name: "parent", children: new VNode[]
                {
                    V.Toggle(key: "t", name: "leaf", value: true),
                }),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), before);
            Assume.That(Parent(scope).ClassListContains("bg-mark"), Is.True, "Precondition: payload on while a checked descendant is present");

            // Act — the checked descendant is removed by reconciliation (no ChangeEvent fires on a structural
            // removal), so only the container's post-children re-scan can re-derive the payload.
            scope.Reconciler.Reconcile(scope.Root, before, new VNode[]
            {
                V.Div(className: "has-[:checked]:bg-mark", name: "parent", children: Array.Empty<VNode>()),
            });

            // Assert — the parent drops the payload (CSS :has(:checked) clears when the checked element leaves).
            Assert.IsFalse(Parent(scope).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_HasCheckedParent_When_AnAlreadyCheckedDescendantIsAddedByReconcile_Then_PayloadApplied()
        {
            // Arrange — a parent with has-[:checked]:bg-mark and no descendants (payload off).
            using var scope = new ReconcilerScope();
            var before = new VNode[]
            {
                V.Div(className: "has-[:checked]:bg-mark", name: "parent", children: Array.Empty<VNode>()),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), before);
            Assume.That(Parent(scope).ClassListContains("bg-mark"), Is.False, "Precondition: payload off with no descendants");

            // Act — an already-checked Toggle is added by reconciliation (mounted with value == true, which
            // fires no ChangeEvent), so only the post-children re-scan can re-derive the payload.
            scope.Reconciler.Reconcile(scope.Root, before, new VNode[]
            {
                V.Div(className: "has-[:checked]:bg-mark", name: "parent", children: new VNode[]
                {
                    V.Toggle(key: "t", name: "leaf", value: true),
                }),
            });

            // Assert
            Assert.IsTrue(Parent(scope).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_HasCheckedParentWithTwoCheckedToggles_When_OneIsRemovedByReconcile_Then_PayloadStays()
        {
            // Arrange — a parent with two checked Toggles; the payload is on.
            using var scope = new ReconcilerScope();
            var before = new VNode[]
            {
                V.Div(className: "has-[:checked]:bg-mark", name: "parent", children: new VNode[]
                {
                    V.Toggle(key: "a", value: true),
                    V.Toggle(key: "b", name: "survivor", value: true),
                }),
            };
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), before);
            Assume.That(Parent(scope).ClassListContains("bg-mark"), Is.True, "Precondition: payload on with two checked descendants");

            // Act — one checked Toggle is removed; the other still checked one remains.
            scope.Reconciler.Reconcile(scope.Root, before, new VNode[]
            {
                V.Div(className: "has-[:checked]:bg-mark", name: "parent", children: new VNode[]
                {
                    V.Toggle(key: "b", name: "survivor", value: true),
                }),
            });

            // Assert — the re-scan finds the survivor still checked, so the payload stays (not a naive clear).
            Assert.IsTrue(Parent(scope).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_HasVariantToken_When_ParentMounted_Then_TokenIsNotInClassList()
        {
            // Arrange/Act — the has- token must never enter the USS class list (it is manipulator/pass-owned).
            using var scope = new ReconcilerScope();
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), new VNode[]
            {
                V.Div(className: "has-[:checked]:bg-mark", name: "parent", children: new VNode[]
                {
                    V.Toggle(name: "leaf", value: false),
                }),
            });

            // Assert
            Assert.IsFalse(Parent(scope).ClassListContains("has-[:checked]:bg-mark"));
        }

        // A child that subscribes ONLY to the store (the parent does not). Its own re-render toggles the
        // `active` class on its element without re-rendering the enclosing has-[.active]: parent.
        [Component]
        private static VNode ActiveClassChild()
        {
            var active = Hooks.UseStore(s_store, s => s.Active);
            return V.Div(className: active ? "active" : "inactive", name: "leaf");
        }

        // The has-[.class]: parent does NOT subscribe to the store, so a store update re-renders only the child.
        [Component]
        private static VNode HasClassParent()
            => V.Div(className: "has-[.active]:bg-mark", name: "parent", children: new VNode[]
            {
                V.Component(ActiveClassChild, key: "child"),
            });

        // A child whose controlled Toggle value is driven by the store; a store update re-renders only the
        // child, applying the new value via SetValueWithoutNotify (which fires NO ChangeEvent).
        [Component]
        private static VNode CheckedChild()
        {
            var on = Hooks.UseStore(s_store, s => s.Active);
            return V.Toggle(value: on, name: "leaf");
        }

        [Component]
        private static VNode HasCheckedParent()
            => V.Div(className: "has-[:checked]:bg-mark", name: "parent", children: new VNode[]
            {
                V.Component(CheckedChild, key: "child"),
            });

        // The has-[.active]: ancestor sits multiple ancestor hops above the independently-re-rendering child's
        // region (the child commits into `middle`, with `intermediate` then `parent` above it) and does NOT
        // subscribe to the store, so the settled-flush sweep must walk the FULL ancestor chain from the child's
        // region up to reach it — a region-only or fixed-one-level walk would leave the has- ancestor stale.
        [Component]
        private static VNode HasClassGrandParent()
            => V.Div(className: "has-[.active]:bg-mark", name: "parent", children: new VNode[]
            {
                V.Div(name: "intermediate", children: new VNode[]
                {
                    V.Div(name: "middle", children: new VNode[]
                    {
                        V.Component(ActiveClassChild, key: "child"),
                    }),
                }),
            });

        [Test]
        public void Given_HasClassParent_When_ADescendantComponentIndependentlyAddsTheClass_Then_PayloadApplied()
        {
            // Arrange — the parent's has-[.active]: payload is off; its child component carries `inactive`.
            using var store = new ToggleStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(HasClassParent, key: "root"));
            Assume.That(Parent(_root).ClassListContains("bg-mark"), Is.False, "Precondition: payload off while child is inactive");

            // Act — only the child re-renders (the parent does not subscribe), switching its class to `active`.
            store.SetActive(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — the settled-flush sweep re-derives the parent from its live subtree even though the has-
            // element itself never re-rendered.
            Assert.IsTrue(Parent(_root).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_HasClassGrandParent_When_ADeeperDescendantIndependentlyAddsTheClass_Then_PayloadApplied()
        {
            // Arrange — the grandparent's has-[.active]: payload is off; the deeply nested child carries `inactive`.
            using var store = new ToggleStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(HasClassGrandParent, key: "root"));
            Assume.That(Parent(_root).ClassListContains("bg-mark"), Is.False, "Precondition: payload off while the deep child is inactive");

            // Act — only the deep child re-renders (its region is two levels below the has- ancestor).
            store.SetActive(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — the dirty-scoped sweep walks up the full ancestor chain from the child's region and
            // re-derives the grandparent even though no element between them reconciled.
            Assert.IsTrue(Parent(_root).ClassListContains("bg-mark"));
        }

        [Test]
        public void Given_HasCheckedParent_When_ADescendantComponentIndependentlyChecksAControlledToggle_Then_PayloadApplied()
        {
            // Arrange — the parent's has-[:checked]: payload is off; its child's controlled Toggle is unchecked.
            using var store = new ToggleStore();
            s_store = store;
            using var mounted = V.Mount(_root, V.Component(HasCheckedParent, key: "root"));
            Assume.That(Parent(_root).ClassListContains("bg-mark"), Is.False, "Precondition: payload off while the controlled Toggle is unchecked");

            // Act — only the child re-renders, applying value=true via SetValueWithoutNotify (no ChangeEvent
            // the has- manipulator could catch).
            store.SetActive(true);
            mounted.GetSchedulerForTest().DrainImmediateForTest();

            // Assert — the settled-flush sweep re-scans the manipulator's subtree, so the payload lights despite
            // the absent event.
            Assert.IsTrue(Parent(_root).ClassListContains("bg-mark"));
        }
    }
}
