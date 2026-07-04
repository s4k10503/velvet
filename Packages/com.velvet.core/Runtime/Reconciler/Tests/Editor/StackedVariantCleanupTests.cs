using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Lifecycle-leak coverage for STACKED variants (<c>dark:hover:</c>, <c>hover:dark:</c>). A stacked leaf
    /// spawns a <c>StyleStackedVariantManipulator</c> the first time its outer gate opens; that manipulator
    /// subscribes to the inner variant's signal — including, for a stacked <c>dark:</c> inner, the process-wide
    /// static <see cref="VelvetTheme.DarkModeChanged"/>. When the element is removed, <c>FiberElementCleaner</c>
    /// must detach every stacked manipulator keyed to it and drop it from
    /// <see cref="ReconcilerContext.StackedVariantManipulators"/>, or the manipulator (and the detached element it
    /// captures) leaks. GWT, one assert per case.
    /// </summary>
    [TestFixture]
    internal sealed class StackedVariantCleanupTests : VariantCleanupTestsBase
    {
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
        public void Given_AStackedManipulator_When_TheOuterGateCloses_Then_ItIsDetachedNotJustGatedOff()
        {
            // Arrange — dark:hover: with dark on creates + tracks the stacked (hover) manipulator.
            using var mounted = MountHost(_ => V.Label(name: "leaf", className: "dark:hover:bg-red-500", text: "x"),
                out _, out var ctx);
            var leaf = _root.Q<Label>("leaf");
            VelvetTheme.IsDark = true;
            Assume.That(ctx.StackedVariantManipulators.Keys.Any(k => k.target == leaf), Is.True,
                "Precondition: created while the outer (dark) gate is open");

            // Act — the outer gate closes (the element stays mounted).
            VelvetTheme.IsDark = false;

            // Assert — the stacked manipulator is detached + dropped, not left lingering until unmount.
            Assert.IsFalse(ctx.StackedVariantManipulators.Keys.Any(k => k.target == leaf));
        }
    }
}
