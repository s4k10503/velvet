using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Regression coverage for variant-manipulator lifecycle leaks. Every state/conditional/relational variant
    /// (<c>hover:</c> / <c>dark:</c> / <c>group-hover:</c> …) attaches a <see cref="UnityEngine.UIElements.Manipulator"/>
    /// that registers callbacks — on the element, on the panel root, on a group/peer source, and (for <c>dark:</c>)
    /// on the process-wide static <see cref="VelvetTheme.DarkModeChanged"/> event. When the element is removed by a
    /// reconcile, <c>FiberElementCleaner</c> must <c>RemoveManipulator</c> it (running
    /// <c>UnregisterCallbacksFromTarget</c>) and drop it from the <see cref="ReconcilerContext"/> tracking dictionary,
    /// otherwise the manipulator — and the detached element it captures — leak. The <c>dark:</c> case is the most
    /// dangerous: a static event holds the manipulator alive for the whole process, so a missed unsubscribe leaks
    /// every screen that ever mounted a <c>dark:</c> element. Also covers the STACKED-variant form
    /// (<c>dark:hover:</c>, <c>hover:dark:</c>): a stacked leaf spawns a
    /// <c>StyleStackedVariantManipulator</c> the first time its outer gate opens, and that manipulator must
    /// be detached from <see cref="ReconcilerContext.StackedVariantManipulators"/> both on element removal
    /// and — for a LEVEL-based inner such as a stacked <c>dark:</c> — the instant the outer gate closes,
    /// releasing its process-wide subscription immediately rather than leaving it lingering until unmount.
    /// GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class VariantManipulatorCleanupTests : VariantCleanupTestsBase
    {
        // hover: → VariantManipulators

        [Test]
        public void Given_AHoverVariantLeafWasMounted_When_ItIsRemoved_Then_ItIsNoLongerTrackedAsAVariantManipulator()
        {
            // Arrange — a leaf carrying a hover: payload, mounted (so a StyleVariantManipulator tracks it).
            using var mounted = MountHost(_ => V.Label(name: "leaf", className: "hover:bg-red-500", text: "x"),
                out var scheduler, out var ctx);
            var leaf = _root.Q<Label>("leaf");
            Assume.That(ctx.VariantManipulators.ContainsKey(leaf), Is.True,
                "Precondition: the hover: leaf is tracked while mounted");

            // Act — the leaf is removed by a reconcile.
            s_store.Set(1);
            scheduler.DrainImmediateForTest();

            // Assert — it is dropped from the variant-manipulator tracking dictionary (no leak).
            Assert.IsFalse(ctx.VariantManipulators.ContainsKey(leaf));
        }

        // dark: → ConditionalVariantManipulators + static event unsubscribe

        [Test]
        public void Given_ADarkVariantLeafWasMounted_When_ItIsRemoved_Then_ItIsNoLongerTrackedAsAConditionalVariantManipulator()
        {
            // Arrange — a leaf carrying a dark: payload, mounted (so a StyleConditionalVariantManipulator tracks it).
            using var mounted = MountHost(_ => V.Label(name: "leaf", className: "dark:bg-black", text: "x"),
                out var scheduler, out var ctx);
            var leaf = _root.Q<Label>("leaf");
            Assume.That(ctx.ConditionalVariantManipulators.ContainsKey(leaf), Is.True,
                "Precondition: the dark: leaf is tracked while mounted");

            // Act — the leaf is removed by a reconcile.
            s_store.Set(1);
            scheduler.DrainImmediateForTest();

            // Assert — it is dropped from the conditional-variant tracking dictionary.
            Assert.IsFalse(ctx.ConditionalVariantManipulators.ContainsKey(leaf));
        }

        [Test]
        public void Given_ADarkVariantLeafWasMounted_When_ItIsRemoved_Then_ItUnsubscribesFromTheStaticDarkModeEvent()
        {
            // Arrange — the process-wide DarkModeChanged subscriber count, then a dark: leaf mounted on top of it.
            var baseline = DarkModeChangedSubscriberCount();
            using var mounted = MountHost(_ => V.Label(name: "leaf", className: "dark:bg-black", text: "x"),
                out var scheduler, out _);
            Assume.That(DarkModeChangedSubscriberCount(), Is.EqualTo(baseline + 1),
                "Precondition: mounting a dark: leaf adds exactly one DarkModeChanged subscriber");

            // Act — the leaf is removed by a reconcile.
            s_store.Set(1);
            scheduler.DrainImmediateForTest();

            // Assert — the static subscription is released (the manipulator + detached element do not leak forever).
            Assert.AreEqual(baseline, DarkModeChangedSubscriberCount());
        }

        // group-hover: → RelationalVariantManipulators

        [Test]
        public void Given_AGroupHoverVariantLeafWasMounted_When_ItIsRemoved_Then_ItIsNoLongerTrackedAsARelationalVariantManipulator()
        {
            // Arrange — a group-hover: leaf nested under a `group` parent, mounted (so a StyleRelationalVariantManipulator tracks it).
            using var mounted = MountHost(
                _ => V.Div(className: "group", children: new VNode[]
                {
                    V.Label(name: "leaf", className: "group-hover:bg-blue-500", text: "x"),
                }),
                out var scheduler, out var ctx);
            var leaf = _root.Q<Label>("leaf");
            Assume.That(ctx.RelationalVariantManipulators.ContainsKey(leaf), Is.True,
                "Precondition: the group-hover: leaf is tracked while mounted");

            // Act — the leaf is removed by a reconcile.
            s_store.Set(1);
            scheduler.DrainImmediateForTest();

            // Assert — it is dropped from the relational-variant tracking dictionary.
            Assert.IsFalse(ctx.RelationalVariantManipulators.ContainsKey(leaf));
        }

        // dark:hover: / hover:dark: → StackedVariantManipulators

        [Test]
        public void Given_AStackedDarkHoverLeafWasMounted_When_ItIsRemoved_Then_NoStackedManipulatorRemains()
        {
            // Arrange — a dark:hover: leaf; flipping dark on opens the outer gate so the stacked (hover) manipulator
            // is created and tracked.
            using var mounted = MountHost(_ => V.Label(name: "leaf", className: "dark:hover:bg-red-500", text: "x"),
                out var scheduler, out var ctx);
            var leaf = _root.Q<Label>("leaf");
            VelvetTheme.IsDark = true;
            Assume.That(ctx.StackedVariantManipulators.Keys.Any(k => k.target == leaf), Is.True,
                "Precondition: a stacked manipulator is tracked while mounted and dark");

            // Act — the leaf is removed by a reconcile.
            s_store.Set(1);
            scheduler.DrainImmediateForTest();

            // Assert — no stacked manipulator for the leaf remains (no leak).
            Assert.IsFalse(ctx.StackedVariantManipulators.Keys.Any(k => k.target == leaf));
        }

        [Test]
        public void Given_AStackedDarkInnerLeaf_When_Removed_Then_TheStaticDarkSubscriptionIsReleased()
        {
            // Arrange — hover:dark: makes the INNER variant dark, so opening the outer (hover) gate creates a
            // stacked manipulator that subscribes the process-wide DarkModeChanged.
            var baseline = DarkModeChangedSubscriberCount();
            using var mounted = MountHost(_ => V.Label(name: "leaf", className: "hover:dark:bg-red-500", text: "x"),
                out var scheduler, out _);
            var leaf = _root.Q<Label>("leaf");
            using (var evt = PointerOverEvent.GetPooled())
            {
                leaf.SimulateEvent(evt);
            }
            Assume.That(DarkModeChangedSubscriberCount(), Is.EqualTo(baseline + 1),
                "Precondition: a stacked dark inner adds exactly one DarkModeChanged subscriber");

            // Act — the leaf is removed by a reconcile.
            s_store.Set(1);
            scheduler.DrainImmediateForTest();

            // Assert — the static subscription is released (the stacked manipulator does not leak forever).
            Assert.AreEqual(baseline, DarkModeChangedSubscriberCount());
        }

        [Test]
        public void Given_AStackedDarkInner_When_TheOuterGateCloses_Then_ItIsDetachedNotJustGatedOff()
        {
            // Arrange — hover:dark: with hover held creates + tracks the stacked (dark) manipulator,
            // whose inner holds the process-wide DarkModeChanged subscription.
            using var mounted = MountHost(_ => V.Label(name: "leaf", className: "hover:dark:bg-red-500", text: "x"),
                out _, out var ctx);
            var leaf = _root.Q<Label>("leaf");
            using (var evt = PointerOverEvent.GetPooled()) leaf.SimulateEvent(evt);
            Assume.That(ctx.StackedVariantManipulators.Keys.Any(k => k.target == leaf), Is.True,
                "Precondition: created while the outer (hover) gate is open");

            // Act — the outer gate closes (the element stays mounted).
            using (var evt = PointerOutEvent.GetPooled()) leaf.SimulateEvent(evt);

            // Assert — a LEVEL-based inner (dark) is detached + dropped so its process-wide
            // subscription releases immediately, not left lingering until unmount. Edge-based
            // inners (hover/focus/active) instead stay attached with the gate closed — they cannot
            // re-seed a continuously-held state on re-attach (see StackedVariantBehaviorTests).
            Assert.IsFalse(ctx.StackedVariantManipulators.Keys.Any(k => k.target == leaf));
        }
    }

    /// <summary>
    /// Pins the class-content fast-path in <c>SyncClassDrivenStyling</c>: a re-render that supplies a
    /// content-identical but FRESHLY ALLOCATED ClassNames array — the shape any component that rebuilds
    /// its VNode tree each render produces (and the Motion variant path produces unconditionally, since
    /// resolving an unchanged active label still merges a new array) — must NOT re-derive the variant
    /// manipulators. Re-derivation is observed through the <c>StyleVariantManipulator</c>'s private
    /// hover-payload array: a re-derivation always installs a freshly extracted array via
    /// <c>UpdatePayloads</c>, so the array instance surviving the patch proves the whole
    /// <c>ApplyVariantManipulators</c> cascade was skipped. The payload store is a private field, hence
    /// reflection inside the test. GWT, one assert.
    /// </summary>
    [TestFixture]
    internal sealed class ClassContentEqualityVariantSkipTests
    {
        // Builds the leaf with a FRESH ClassNames array per call. Deliberately bypasses V.Div: its
        // ParseClassNames cache returns a reference-stable array for a constant className string, which
        // would hit the ReferenceEquals fast-path and mask the content-equality case under test.
        private static VNode[] Tree() => new VNode[]
        {
            new ElementNode
            {
                Name = "leaf",
                ClassNames = new[] { "p-4", "hover:bg-red-500" },
            },
        };

        // Reads the manipulator's derived hover-payload array. A private field is the only observation
        // point: the manipulator instance itself is reused by design, so instance identity cannot
        // distinguish "derivation skipped" from "derivation re-ran and updated the same instance".
        private static string[] HoverPayloadsOf(StyleVariantManipulator manipulator)
        {
            var field = typeof(StyleVariantManipulator).GetField("_hover", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException(
                    "Could not find StyleVariantManipulator._hover. The payload field may have been renamed.");
            return (string[])field.GetValue(manipulator);
        }

        [Test]
        public void Given_AMountedHoverVariantLeaf_When_RepatchedWithAContentIdenticalFreshClassArray_Then_TheVariantManipulatorIsNotRederived()
        {
            // Arrange — mount the leaf, then capture the manipulator's derived hover-payload array.
            using var scope = new ReconcilerScope();
            var oldTree = Tree();
            var newTree = Tree();
            Assume.That(
                ReferenceEquals(((ElementNode)oldTree[0]).ClassNames, ((ElementNode)newTree[0]).ClassNames),
                Is.False,
                "Precondition: the two renders carry distinct (content-identical) class array instances");
            scope.Reconciler.Reconcile(scope.Root, Array.Empty<VNode>(), oldTree);
            var leaf = scope.Root[0];
            Assume.That(scope.Reconciler.Context.VariantManipulators.ContainsKey(leaf), Is.True,
                "Precondition: the hover: leaf is tracked while mounted");
            var manipulator = scope.Reconciler.Context.VariantManipulators[leaf];
            var payloadsBefore = HoverPayloadsOf(manipulator);

            // Act — patch with the content-identical, freshly allocated class array.
            scope.Reconciler.Reconcile(scope.Root, oldTree, newTree);
            Assume.That(scope.Root[0], Is.SameAs(leaf),
                "Premise guard: the leaf was patched in place — a replacement would trivially keep the " +
                "captured manipulator's payloads and mask a broken fast-path");

            // Assert — the derivation was skipped: a re-derivation would have installed a freshly
            // extracted payload array, so the surviving instance proves the content fast-path held.
            Assert.That(ReferenceEquals(payloadsBefore, HoverPayloadsOf(manipulator)), Is.True,
                "A content-identical class array must not re-derive the variant manipulator payloads");
        }
    }
}
