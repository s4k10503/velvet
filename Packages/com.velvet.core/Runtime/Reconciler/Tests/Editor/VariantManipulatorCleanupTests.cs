using System;
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
    /// every screen that ever mounted a <c>dark:</c> element. GWT, one assert per case.
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
    }
}
