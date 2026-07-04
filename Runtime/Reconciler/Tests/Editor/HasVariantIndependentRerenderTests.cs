using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Regression coverage for the has- reactivity gap: a has- element's per-element post-children pass only
    /// re-derives the payload when the has- element ITSELF reconciles. When a descendant component re-renders
    /// INDEPENDENTLY (its own store/state changes a descendant's class or a controlled field value, never
    /// re-rendering the enclosing has- element), the post-children pass never runs and no descendant event
    /// fires — so before the fix the has- payload went stale. After each settled flush the reconciler re-derives
    /// the has- elements that flush could have affected (FiberNodePatcher.RefreshHasVariants, driven by
    /// FiberRenderer): it walks UP the ancestor chain from the flushed region's root (plus each active Portal
    /// target) and re-derives the registered has- elements found there. Here the has- parent deliberately does
    /// NOT subscribe to the store, so the payload can only stay in sync via that sweep. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class HasVariantIndependentRerenderTests
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

        private static FiberBatchScheduler Scheduler(MountedTree mounted)
            => mounted.Root.Reconciler.Context.BatchScheduler;

        private static VisualElement Parent(VisualElement root) => root.Q<VisualElement>("parent");

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
            Scheduler(mounted).DrainImmediateForTest();

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
            Scheduler(mounted).DrainImmediateForTest();

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
            Scheduler(mounted).DrainImmediateForTest();

            // Assert — the settled-flush sweep re-scans the manipulator's subtree, so the payload lights despite
            // the absent event.
            Assert.IsTrue(Parent(_root).ClassListContains("bg-mark"));
        }
    }
}
