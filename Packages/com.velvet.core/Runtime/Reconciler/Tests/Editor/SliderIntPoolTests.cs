using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    // SliderPoolAdmissionTests and CompositeFieldChildPoolReuseTests' slider case, over the SliderInt pool:
    // the same admission check and the same foreign-child detach, reached through the int slider's own
    // pool and reset.
    internal sealed class SliderIntPoolTests
    {
        // The pool is process-wide, so a fixture that ran earlier can leave it at its cap, where a return
        // is dropped without ever reaching what these read; and a children-bearing slider left in it breaks
        // whatever runs next.
        [SetUp]
        public void SetUp() => VNodePoolTestAccess.ClearSliderIntPoolForTest();

        [TearDown]
        public void TearDown() => VNodePoolTestAccess.ClearSliderIntPoolForTest();

        [Test]
        public void Given_ASliderIntThatBuiltItsInputField_When_ItIsReturnedToThePool_Then_OnlyOneWithoutItComesBack()
        {
            // Arrange
            var carrying = new SliderInt();
            carrying.showInputField = true;
            var built = carrying.Q<TextField>() != null;
            var plain = new SliderInt();

            // Act
            VNodePool.ReturnSliderInt(carrying);
            var afterCarrying = VNodePool.RentSliderInt();
            VNodePool.ReturnSliderInt(plain);
            var afterPlain = VNodePool.RentSliderInt();

            // Assert
            Assert.That(
                (built, ReferenceEquals(afterCarrying, carrying), ReferenceEquals(afterPlain, plain)),
                Is.EqualTo((true, false, true)));
        }

        [Test]
        public void Given_ASliderIntCarryingAForeignChild_When_ItIsRentedBackFromThePool_Then_ItHoldsOnlyItsOwnInput()
        {
            // Arrange — the foreign child sits in front, where CompositeFieldChildPoolReuseTests measured the
            // reconciler placing a V.Custom child.
            var returned = new SliderInt();
            var ownInput = returned.ElementAt(0);
            returned.Insert(0, new Label("stale"));
            var whileCarrying = returned.childCount;
            VNodePool.ReturnSliderInt(returned);

            // Act
            var rented = VNodePool.RentSliderInt();

            // Assert — same instance, and only the input its constructor made.
            Assert.That(
                (whileCarrying, ReferenceEquals(rented, returned), rented.childCount,
                    ReferenceEquals(rented.ElementAt(0), ownInput)),
                Is.EqualTo((2, true, 1, true)));
        }

        [Test]
        public void Given_ARolledBackSliderInt_When_ItsOrphanIsReturned_Then_TheNextRentHandsItBack()
        {
            // Arrange — a stand-in for the orphan a discarded speculative render leaves: detached without
            // passing through RemoveElement, so the rollback path is the only one that can pool it.
            using var scope = new ReconcilerScope();
            var tree = new VNode[] { V.SliderInt() };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var orphan = scope.Root[0];
            orphan.RemoveFromHierarchy();

            // Act
            new FiberElementCleaner(scope.Reconciler.Context).ReturnRolledBackOrphan(orphan);

            // Assert
            Assert.That(ReferenceEquals(VNodePool.RentSliderInt(), orphan), Is.True);
        }
    }
}
