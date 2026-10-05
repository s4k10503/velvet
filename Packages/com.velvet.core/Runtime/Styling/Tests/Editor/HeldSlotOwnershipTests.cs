using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// The bookkeeping a gap, grid or divide container keeps on a child it writes to: which slots it holds,
    /// and which containers re-apply when the child's own classes change.
    /// </summary>
    [TestFixture]
    internal sealed class HeldSlotOwnershipTests
    {
        private sealed class CountingOwner : Manipulator, IChildClassWatcher
        {
            public int Reapplied { get; private set; }

            public void Reapply() => Reapplied++;

            protected override void RegisterCallbacksOnTarget()
            {
            }

            protected override void UnregisterCallbacksFromTarget()
            {
            }
        }

        [Test]
        public void Given_ALonghandThenAShorthandBehindAHold_When_TheSlotIsHandedBack_Then_TheLaterShorthandIsRestored()
        {
            // Arrange
            var element = new VisualElement();
            StyleArbitraryValueResolver.Apply(element,
                new ArbitraryStyle(ArbitraryProperty.MarginTop, 2f, LengthUnit.Pixel));
            StyleArbitraryValueResolver.Apply(element,
                new ArbitraryStyle(ArbitraryProperty.Margin, 8f, LengthUnit.Pixel));
            StyleArbitraryValueResolver.Hold(element, HeldSlot.MarginTop, new StyleLength(12f));
            var held = element.style.marginTop.value.value;

            // Act
            StyleArbitraryValueResolver.HandBack(element, HeldSlot.MarginTop);

            // Assert
            Assert.That((held, element.style.marginTop.value.value), Is.EqualTo((12f, 8f)));
        }

        [Test]
        public void Given_AnElementHoldingOneSlot_When_AnotherSlotIsHandedBackIfHeld_Then_ThatSlotsInlineValueStays()
        {
            // Arrange — a value nothing here wrote is not the hold layer's to clear.
            var element = new VisualElement();
            StyleArbitraryValueResolver.Hold(element, HeldSlot.MarginLeft, new StyleLength(4f));
            element.style.marginRight = 5f;

            // Act
            StyleArbitraryValueResolver.HandBackIfHeld(element, HeldSlot.MarginRight);

            // Assert
            Assert.That(element.style.marginRight.value.value, Is.EqualTo(5f));
        }

        [Test]
        public void Given_AChildTwoContainersClaimInTurn_When_ItsClassesChange_Then_OnlyTheLaterOwnerReapplies()
        {
            // Arrange
            var owners = new Dictionary<VisualElement, Manipulator>();
            var child = new VisualElement();
            var first = new CountingOwner();
            var second = new CountingOwner();
            StyleChildOwnership.Claim(owners, child, first);
            StyleChildOwnership.Claim(owners, child, second);

            // Act
            StyleArbitraryValueResolver.NotifyClassesChanged(child);

            // Assert
            Assert.That((first.Reapplied, second.Reapplied), Is.EqualTo((0, 1)));
        }

        [Test]
        public void Given_AChildItsOwnerReleased_When_ItsClassesChange_Then_TheFormerOwnerDoesNotReapply()
        {
            // Arrange
            var owners = new Dictionary<VisualElement, Manipulator>();
            var child = new VisualElement();
            var owner = new CountingOwner();
            StyleChildOwnership.Claim(owners, child, owner);
            var released = StyleChildOwnership.TryRelease(owners, child, owner);

            // Act
            StyleArbitraryValueResolver.NotifyClassesChanged(child);

            // Assert — the release rides along, since a claim never dropped would leave the owner watching.
            Assert.That((released, owner.Reapplied), Is.EqualTo((true, 0)));
        }

        [Test]
        public void Given_AClaimedChild_When_AClassLeavesIt_Then_ItsOwnerReapplies()
        {
            // Arrange
            var owners = new Dictionary<VisualElement, Manipulator>();
            var child = new VisualElement();
            child.AddToClassList("mr-2");
            var owner = new CountingOwner();
            StyleChildOwnership.Claim(owners, child, owner);

            // Act
            StyleClassProjection.Remove(child, "mr-2", StyleLayerPriority.Base);

            // Assert
            Assert.That(owner.Reapplied, Is.EqualTo(1));
        }
    }
}
