using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    // SliderPoolAdmissionTests and CompositeFieldChildPoolReuseTests' slider case, over the SliderInt pool:
    // the same admission check and the same foreign-child detach, reached through the int slider's own
    // pool and reset; and the paths that hand an int slider to the pool or drop it from there.
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
        public void Given_ASliderIntHoldingATextFieldClassOutsideItsInput_When_ItIsReturnedToThePool_Then_ItComesBack()
        {
            // Arrange — the class the admission check refuses on, carried by a foreign child's child rather
            // than by the slider's own input field, which is the only place the check is about.
            var returned = new SliderInt();
            var foreign = new VisualElement();
            var classed = new VisualElement();
            classed.AddToClassList(BaseSlider<int>.textFieldClassName);
            foreign.Add(classed);
            returned.Insert(0, foreign);
            var held = returned.Q(className: BaseSlider<int>.textFieldClassName) != null;

            // Act
            VNodePool.ReturnSliderInt(returned);
            var rented = VNodePool.RentSliderInt();

            // Assert
            Assert.That((held, ReferenceEquals(rented, returned)), Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_APooledSliderInt_When_TheDomainReloadResetRuns_Then_TheNextRentBuildsANewOne()
        {
            // Arrange — the reset is private and runs from a RuntimeInitializeOnLoadMethod, so the case reaches
            // it by name.
            var reset = typeof(VNodePool).GetMethod(
                "ResetStaticFields", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var returned = new SliderInt();
            VNodePool.ReturnSliderInt(returned);
            var pooled = VNodePoolTestAccess.SliderIntPoolCountForTest;

            // Act
            reset!.Invoke(null, null);

            // Assert
            Assert.That((pooled, ReferenceEquals(VNodePool.RentSliderInt(), returned)), Is.EqualTo((1, false)));
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
