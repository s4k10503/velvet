using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that <see cref="ReconcilerContext"/>'s by-target index of stacked-variant manipulators follows
    /// the registry through four ways a registration leaves it — an outer gate closing over a level-based
    /// inner, an owner being dropped, an element being detached, the owning reconciler being disposed — and
    /// that it keeps the registrations that stay. Gates are opened and closed by hand, so no reconcile pass
    /// decides what the index is handed; the dispose cases take their context from a reconciler, the rest
    /// build one bare.
    /// </summary>
    [TestFixture]
    internal sealed class StackedVariantTargetIndexTests
    {
        private static void Open(ReconcilerContext context, VisualElement target, object owner, string payload)
            => context.GateStackedVariant(target, owner, payload, true, StyleLayerPriority.Data, 0);

        private static void Close(ReconcilerContext context, VisualElement target, object owner, string payload)
            => context.GateStackedVariant(target, owner, payload, false, StyleLayerPriority.Data, 0);

        [Test]
        public void Given_AStackedDarkInner_When_ItsOuterGateCloses_Then_TheIndexLetsTheElementGo()
        {
            // Arrange — a dark: inner does not survive an outer close, so closing drops its registration.
            var context = new ReconcilerContext();
            var target = new VisualElement();
            var owner = new object();
            Open(context, target, owner, "dark:w-[10px]");
            var indexedWhileOpen = context.HasStackedVariantsOn(target);

            // Act
            Close(context, target, owner, "dark:w-[10px]");

            // Assert
            Assert.That((indexedWhileOpen, context.HasStackedVariantsOn(target)), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_AStackedHoverInner_When_ItsOwnerIsDropped_Then_TheIndexLetsTheElementGo()
        {
            // Arrange
            var context = new ReconcilerContext();
            var target = new VisualElement();
            var owner = new object();
            Open(context, target, owner, "hover:w-[10px]");
            var indexedWhileOpen = context.HasStackedVariantsOn(target);

            // Act
            context.DropStackedVariants(owner);

            // Assert
            Assert.That((indexedWhileOpen, context.HasStackedVariantsOn(target)), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_TwoStackedInnersOnOneElement_When_TheElementIsDetached_Then_TheIndexLetsTheElementGo()
        {
            // Arrange
            var context = new ReconcilerContext();
            var target = new VisualElement();
            var owner = new object();
            Open(context, target, owner, "hover:w-[10px]");
            Open(context, target, owner, "focus:w-[20px]");
            var indexedWhileOpen = context.HasStackedVariantsOn(target);

            // Act
            context.DetachStackedVariants(target);

            // Assert
            Assert.That((indexedWhileOpen, context.HasStackedVariantsOn(target)), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_TwoStackedInnersOnOneElement_When_TheElementIsDetached_Then_NeitherManipulatorStaysOnIt()
        {
            // Arrange
            var context = new ReconcilerContext();
            var target = new VisualElement();
            var owner = new object();
            Open(context, target, owner, "hover:w-[10px]");
            Open(context, target, owner, "focus:w-[20px]");
            var built = context.StackedVariantManipulators.Values.ToList();

            // Act
            context.DetachStackedVariants(target);

            // Assert — the count rides along, so a pair that was never built cannot read as detached.
            Assert.That((built.Count, built.Count(manipulator => manipulator.target != null)), Is.EqualTo((2, 0)));
        }

        // GREEN_ON_BASE(characterization): the base already detaches what a dropped owner gated.
        [Test]
        public void Given_AStackedHoverInner_When_ItsOwnerIsDropped_Then_ItIsDetached()
        {
            // Arrange
            var context = new ReconcilerContext();
            var target = new VisualElement();
            var owner = new object();
            Open(context, target, owner, "hover:w-[10px]");
            var hover = context.StackedVariantManipulators.Values.Single();
            var attachedWhileOpen = hover.target == target;

            // Act
            context.DropStackedVariants(owner);

            // Assert
            Assert.That((attachedWhileOpen, hover.target), Is.EqualTo((true, (VisualElement)null)));
        }

        // GREEN_ON_BASE(characterization): the base already empties the registry when its reconciler is disposed.
        [Test]
        public void Given_AStackedHoverInner_When_TheOwningReconcilerIsDisposed_Then_TheRegistryIsEmpty()
        {
            // Arrange — a hover leaf that is not itself a variant, so detaching it removes nothing from the
            // registry on its own.
            var scope = new ReconcilerScope();
            var context = scope.Reconciler.Context;
            Open(context, new VisualElement(), new object(), "hover:w-[10px]");
            var registeredBeforeDispose = context.StackedVariantManipulators.Count;

            // Act
            scope.Dispose();

            // Assert
            Assert.That((registeredBeforeDispose, context.StackedVariantManipulators.Count), Is.EqualTo((1, 0)));
        }

        [Test]
        public void Given_AStackedHoverInner_When_TheOwningReconcilerIsDisposed_Then_TheIndexLetsTheElementGo()
        {
            // Arrange
            var scope = new ReconcilerScope();
            var context = scope.Reconciler.Context;
            var target = new VisualElement();
            Open(context, target, new object(), "hover:w-[10px]");
            var indexedBeforeDispose = context.HasStackedVariantsOn(target);

            // Act
            scope.Dispose();

            // Assert
            Assert.That((indexedBeforeDispose, context.HasStackedVariantsOn(target)), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_TwoStackedInnersOnOneElement_When_TheLaterOnesOuterGateCloses_Then_TheIndexStillHoldsTheEarlierOne()
        {
            // Arrange — registered hover first, then dark; only the dark one leaves on an outer close.
            var context = new ReconcilerContext();
            var target = new VisualElement();
            var owner = new object();
            Open(context, target, owner, "hover:w-[10px]");
            var hover = context.StackedVariantManipulators.Single(kv => kv.Key.inner == StyleVariantKind.Hover).Value;
            Open(context, target, owner, "dark:w-[20px]");
            var indexedWhileBothOpen = new List<StyleStackedVariantManipulator>();
            context.CopyStackedVariantsOn(target, indexedWhileBothOpen);
            var indexed = new List<StyleStackedVariantManipulator>();

            // Act
            Close(context, target, owner, "dark:w-[20px]");
            context.CopyStackedVariantsOn(target, indexed);

            // Assert — the count before the close rides along, so a dark registration that never reached the
            // index cannot read as one the close removed.
            Assert.That((indexedWhileBothOpen.Count, indexed.SequenceEqual(new[] { hover })), Is.EqualTo((2, true)));
        }
    }
}
