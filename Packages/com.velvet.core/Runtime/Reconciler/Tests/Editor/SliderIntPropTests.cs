using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <c>V.SliderInt</c> on a detached root: its value, range, <c>direction:</c> and
    /// <c>inverted:</c> reach a <see cref="SliderInt"/> on the create and patch paths, and a pooled one comes
    /// back without its last tenancy's direction or record. The rules those props follow are
    /// <c>V.Slider</c>'s, which <c>SliderDirectionPropTests</c> measures in full; these measure that the
    /// int slider reaches them. <c>SliderIntPanelTests</c> measures its keyboard on a real panel.
    /// </summary>
    [TestFixture]
    internal sealed class SliderIntPropTests : ReconcilerTestFixture
    {
        private SliderInt ReconcileAndGet(VNode[] tree)
        {
            Reconciler!.Reconcile(Root, Array.Empty<VNode>(), tree);
            return (SliderInt)Root!.ElementAt(0);
        }

        #region create and patch

        [Test]
        public void Given_AValueAndARange_When_TheSliderIntIsReconciled_Then_TheElementCarriesThem()
        {
            // Arrange
            var tree = new VNode[] { V.SliderInt(value: 7, lowValue: 2, highValue: 20) };

            // Act
            var slider = ReconcileAndGet(tree);

            // Assert
            Assert.That((slider.lowValue, slider.highValue, slider.value), Is.EqualTo((2, 20, 7)));
        }

        [Test]
        public void Given_AValueAndARange_When_ALaterRenderMovesBoth_Then_TheSameElementCarriesTheNewOnes()
        {
            // Arrange
            var oldTree = new VNode[] { V.SliderInt(value: 7, lowValue: 2, highValue: 20) };
            var newTree = new VNode[] { V.SliderInt(value: 25, lowValue: 10, highValue: 30) };
            var slider = ReconcileAndGet(oldTree);

            // Act
            Reconciler!.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That((slider.lowValue, slider.highValue, slider.value), Is.EqualTo((10, 30, 25)));
        }

        [Test]
        public void Given_OnlyAHighValue_When_TheSliderIntIsReconciled_Then_TheElementCarriesIt()
        {
            // Arrange
            var tree = new VNode[] { V.SliderInt(highValue: 4) };

            // Act
            var slider = ReconcileAndGet(tree);

            // Assert
            Assert.That(slider.highValue, Is.EqualTo(4));
        }

        [Test]
        public void Given_ADeclaredRange_When_ALaterRenderRaisesOnlyItsLowValueAboveTheValue_Then_TheValueIsClamped()
        {
            // Arrange
            var oldTree = new VNode[] { V.SliderInt(value: 2, lowValue: 0, highValue: 10) };
            var newTree = new VNode[] { V.SliderInt(value: 2, lowValue: 5, highValue: 10) };
            var slider = ReconcileAndGet(oldTree);

            // Act
            Reconciler!.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That((slider.lowValue, slider.value), Is.EqualTo((5, 5)));
        }

        [Test]
        public void Given_ADeclaredRange_When_ALaterRenderLowersOnlyItsHighValueBelowTheValue_Then_TheValueIsClamped()
        {
            // Arrange
            var oldTree = new VNode[] { V.SliderInt(value: 8, lowValue: 0, highValue: 10) };
            var newTree = new VNode[] { V.SliderInt(value: 8, lowValue: 0, highValue: 5) };
            var slider = ReconcileAndGet(oldTree);

            // Act
            Reconciler!.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That((slider.highValue, slider.value), Is.EqualTo((5, 5)));
        }

        [Test]
        public void Given_DataAttributes_When_VSliderIntIsCalled_Then_TheNodeCarriesThem()
        {
            // Arrange
            var data = new Dictionary<string, string> { ["state"] = "open" };

            // Act
            var node = V.SliderInt(data: data);

            // Assert
            Assert.That(node.Props?.Data, Is.SameAs(data));
        }

        [Test]
        public void Given_ADeclaredVerticalDirection_When_TheSliderIntIsReconciled_Then_TheElementIsVertical()
        {
            // Arrange
            var tree = new VNode[] { V.SliderInt(direction: SliderDirection.Vertical) };

            // Act
            var slider = ReconcileAndGet(tree);

            // Assert
            Assert.That(slider.direction, Is.EqualTo(SliderDirection.Vertical));
        }

        [Test]
        public void Given_ADeclaredInvertedFlag_When_ALaterRenderDropsIt_Then_TheSliderIntIsNotInvertedAgain()
        {
            // Arrange
            var oldTree = new VNode[] { V.SliderInt(inverted: true) };
            var newTree = new VNode[] { V.SliderInt() };
            var slider = ReconcileAndGet(oldTree);
            var whileDeclared = slider.inverted;

            // Act
            Reconciler!.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That((whileDeclared, slider.inverted), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_ADeclaredRange_When_ALaterRenderDropsIt_Then_TheRangeIsZeroToTen()
        {
            // Arrange
            var oldTree = new VNode[] { V.SliderInt(lowValue: 2, highValue: 4) };
            var newTree = new VNode[] { V.SliderInt() };
            var slider = ReconcileAndGet(oldTree);
            var whileDeclared = (slider.lowValue, slider.highValue);

            // Act
            Reconciler!.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(
                (whileDeclared.lowValue, whileDeclared.highValue, slider.lowValue, slider.highValue),
                Is.EqualTo((2, 4, 0, 10)));
        }

        #endregion

        #region the step

        [TestCase(0)]
        [TestCase(-1)]
        public void Given_AStepNotAboveZero_When_VSliderIntIsCalled_Then_ItThrowsNamingTheStep(int step)
        {
            // Act
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => V.SliderInt(step: step));

            // Assert
            Assert.That(ex.ParamName, Is.EqualTo("step"));
        }

        [Test]
        public void Given_ADeclaredStep_When_VSliderIntIsCalled_Then_TheNodeCarriesIt()
        {
            // Act
            var node = V.SliderInt(step: 5);

            // Assert
            Assert.That(node.Props?.SliderInt?.Step, Is.EqualTo(5));
        }

        #endregion

        #region pooled reuse

        [Test]
        public void Given_APooledSliderIntWhoseLastTenantWasVertical_When_APlainSliderIntRentsIt_Then_ItIsHorizontal()
        {
            // Arrange — the pool is process-wide, and one at its cap drops the return this reads.
            VNodePoolTestAccess.ClearSliderIntPoolForTest();
            var declaring = new VNode[] { V.SliderInt(direction: SliderDirection.Vertical) };
            var pooled = ReconcileAndGet(declaring);
            var whileDeclared = pooled.direction;
            Reconciler!.Reconcile(Root, declaring, Array.Empty<VNode>());
            var plain = new VNode[] { V.SliderInt() };

            // Act
            var rented = ReconcileAndGet(plain);

            // Assert — the identity term is what makes this a reading of the recycled element; a fresh one
            // is horizontal on its own.
            Assert.That(
                (ReferenceEquals(rented, pooled), whileDeclared, rented.direction),
                Is.EqualTo((true, SliderDirection.Vertical, SliderDirection.Horizontal)));
        }

        [Test]
        public void Given_APooledSliderIntWhoseLastTenantDeclaredADirection_When_ItsNextTenantWritesOneFromARefCallback_Then_TheDirectionSurvives()
        {
            // Arrange — the first tenancy records a direction default and then unmounts, which is what
            // hands the element to the shared pool; the second declares only the flag.
            VNodePoolTestAccess.ClearSliderIntPoolForTest();
            var declaring = new VNode[] { V.SliderInt(direction: SliderDirection.Horizontal) };
            var pooled = ReconcileAndGet(declaring);
            Reconciler!.Reconcile(Root, declaring, Array.Empty<VNode>());
            Func<VisualElement, Action> setDirection = el =>
            {
                ((SliderInt)el).direction = SliderDirection.Vertical;
                return () => { };
            };
            var oldTree = new VNode[] { V.SliderInt(inverted: true, refCallback: setDirection) };
            var newTree = new VNode[] { V.SliderInt(inverted: false, refCallback: setDirection) };
            Reconciler!.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler!.Reconcile(Root, oldTree, newTree);

            // Assert — the identity term is what makes this a reading of the recycled element, whose
            // previous tenancy took the record.
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), pooled), ((SliderInt)Root!.ElementAt(0)).direction),
                Is.EqualTo((true, SliderDirection.Vertical)));
        }

        #endregion
    }
}
