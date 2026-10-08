using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <c>V.Slider</c>'s <c>direction:</c> and <c>inverted:</c> on a detached root, and what
    /// declaring them does to the range beside them.
    /// <list type="bullet">
    /// <item>Each reaches the element through a reconcile of the factory's node, on the create path and on
    /// the patch path.</item>
    /// <item>Each, once dropped by a later render, restores what the element carried before any render
    /// declared it. The drops are also measured on a subclass built vertical and inverted, so an
    /// implementation coalescing to UI Toolkit's constructor values fails them.</item>
    /// <item>A member no render has declared is left where a <c>refCallback:</c> put it — the range
    /// included, which a render changing only these two does not rewrite.</item>
    /// <item>A pooled slider comes back horizontal and not inverted, and the record its last tenancy took
    /// is not its next tenancy's.</item>
    /// </list>
    /// <c>SliderDirectionPanelTests</c> measures the same props through keyboard input on a real panel.
    /// </summary>
    [TestFixture]
    internal sealed class SliderDirectionPropTests : ReconcilerTestFixture
    {
        // Built away from Slider's own direction and flag, so the value a drop must restore differs from
        // the constant an implementation would otherwise coalesce to.
        internal sealed class BuiltVerticalInvertedSlider : Slider
        {
            public BuiltVerticalInvertedSlider()
            {
                direction = SliderDirection.Vertical;
                inverted = true;
            }
        }

        private Slider ReconcileAndGet(VNode[] tree)
        {
            Reconciler!.Reconcile(Root, Array.Empty<VNode>(), tree);
            return (Slider)Root!.ElementAt(0);
        }

        #region create and patch

        [Test]
        public void Given_ADeclaredVerticalDirection_When_TheSliderIsReconciled_Then_TheElementIsVertical()
        {
            // Arrange
            var tree = new VNode[] { V.Slider(direction: SliderDirection.Vertical) };

            // Act
            var slider = ReconcileAndGet(tree);

            // Assert
            Assert.That(slider.direction, Is.EqualTo(SliderDirection.Vertical));
        }

        [Test]
        public void Given_ADeclaredInvertedFlag_When_TheSliderIsReconciled_Then_TheElementIsInverted()
        {
            // Arrange
            var tree = new VNode[] { V.Slider(inverted: true) };

            // Act
            var slider = ReconcileAndGet(tree);

            // Assert
            Assert.That(slider.inverted, Is.True);
        }

        [Test]
        public void Given_ADeclaredVerticalDirection_When_ALaterRenderDeclaresHorizontal_Then_TheElementIsHorizontal()
        {
            // Arrange
            var oldTree = new VNode[] { V.Slider(direction: SliderDirection.Vertical) };
            var newTree = new VNode[] { V.Slider(direction: SliderDirection.Horizontal) };
            var slider = ReconcileAndGet(oldTree);
            var whileVertical = slider.direction;

            // Act
            Reconciler!.Reconcile(Root, oldTree, newTree);

            // Assert — the identity term separates a patch from a remount, which would satisfy the reading
            // while the tree holds a different element.
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), slider), whileVertical, slider.direction),
                Is.EqualTo((true, SliderDirection.Vertical, SliderDirection.Horizontal)));
        }

        [Test]
        public void Given_ADeclaredInvertedFlag_When_ALaterRenderDeclaresItFalse_Then_TheElementIsNotInverted()
        {
            // Arrange
            var oldTree = new VNode[] { V.Slider(inverted: true) };
            var newTree = new VNode[] { V.Slider(inverted: false) };
            var slider = ReconcileAndGet(oldTree);
            var whileInverted = slider.inverted;

            // Act
            Reconciler!.Reconcile(Root, oldTree, newTree);

            // Assert — same identity term, and for the same reason.
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), slider), whileInverted, slider.inverted),
                Is.EqualTo((true, true, false)));
        }

        #endregion

        #region removal

        [Test]
        public void Given_ADeclaredVerticalDirection_When_ALaterRenderDropsIt_Then_TheSliderIsHorizontalAgain()
        {
            // Arrange
            var oldTree = new VNode[] { V.Slider(direction: SliderDirection.Vertical) };
            var newTree = new VNode[] { V.Slider() };
            var slider = ReconcileAndGet(oldTree);
            var whileDeclared = slider.direction;

            // Act
            Reconciler!.Reconcile(Root, oldTree, newTree);

            // Assert — same identity term, and for the same reason.
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), slider), whileDeclared, slider.direction),
                Is.EqualTo((true, SliderDirection.Vertical, SliderDirection.Horizontal)));
        }

        [Test]
        public void Given_ADeclaredInvertedFlag_When_ALaterRenderDropsIt_Then_TheSliderIsNotInvertedAgain()
        {
            // Arrange
            var oldTree = new VNode[] { V.Slider(inverted: true) };
            var newTree = new VNode[] { V.Slider() };
            var slider = ReconcileAndGet(oldTree);
            var whileDeclared = slider.inverted;

            // Act
            Reconciler!.Reconcile(Root, oldTree, newTree);

            // Assert — same identity term, and for the same reason.
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), slider), whileDeclared, slider.inverted),
                Is.EqualTo((true, true, false)));
        }

        [Test]
        public void Given_ASliderBuiltVertical_When_ALaterRenderDropsADeclaredHorizontal_Then_ItIsVerticalAgain()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Custom<BuiltVerticalInvertedSlider>(props: new FiberElementProps
                {
                    Slider = new SliderSettings(Direction: SliderDirection.Horizontal),
                }),
            };
            var newTree = new VNode[] { V.Custom<BuiltVerticalInvertedSlider>() };
            var slider = ReconcileAndGet(oldTree);
            var whileDeclared = slider.direction;

            // Act
            Reconciler!.Reconcile(Root, oldTree, newTree);

            // Assert — same identity term, and for the same reason.
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), slider), whileDeclared, slider.direction),
                Is.EqualTo((true, SliderDirection.Horizontal, SliderDirection.Vertical)));
        }

        [Test]
        public void Given_ASliderBuiltInverted_When_ALaterRenderDropsADeclaredFalse_Then_ItIsInvertedAgain()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Custom<BuiltVerticalInvertedSlider>(props: new FiberElementProps
                {
                    Slider = new SliderSettings(Inverted: false),
                }),
            };
            var newTree = new VNode[] { V.Custom<BuiltVerticalInvertedSlider>() };
            var slider = ReconcileAndGet(oldTree);
            var whileDeclared = slider.inverted;

            // Act
            Reconciler!.Reconcile(Root, oldTree, newTree);

            // Assert — same identity term, and for the same reason.
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), slider), whileDeclared, slider.inverted),
                Is.EqualTo((true, false, true)));
        }

        [Test]
        public void Given_ASliderBuiltVerticalAndInverted_When_OnlyItsRangeIsDeclared_Then_NeitherIsWritten()
        {
            // Arrange
            var tree = new VNode[]
            {
                V.Custom<BuiltVerticalInvertedSlider>(props: new FiberElementProps
                {
                    Slider = new SliderSettings(LowValue: 1f),
                }),
            };

            // Act
            var slider = ReconcileAndGet(tree);

            // Assert
            Assert.That((slider.direction, slider.inverted), Is.EqualTo((SliderDirection.Vertical, true)));
        }

        #endregion

        #region what a refCallback wrote

        // The three below follow TextFieldInputPropTests' refCallback sequence, for the reasons it gives: a
        // render declares one member, the refCallback assigns another, and a second render changes only the
        // first.
        [Test]
        public void Given_ADirectionWrittenFromARefCallback_When_ALaterRenderChangesTheFlag_Then_TheDirectionSurvives()
        {
            // Arrange
            Func<VisualElement, Action> setDirection = el =>
            {
                ((Slider)el).direction = SliderDirection.Vertical;
                return () => { };
            };
            var oldTree = new VNode[] { V.Slider(inverted: true, refCallback: setDirection) };
            var newTree = new VNode[] { V.Slider(inverted: false, refCallback: setDirection) };
            var slider = ReconcileAndGet(oldTree);

            // Act
            Reconciler!.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), slider), slider.direction),
                Is.EqualTo((true, SliderDirection.Vertical)));
        }

        [Test]
        public void Given_AFlagWrittenFromARefCallback_When_ALaterRenderChangesTheDirection_Then_TheFlagSurvives()
        {
            // Arrange
            Func<VisualElement, Action> setFlag = el =>
            {
                ((Slider)el).inverted = true;
                return () => { };
            };
            var oldTree = new VNode[] { V.Slider(direction: SliderDirection.Vertical, refCallback: setFlag) };
            var newTree = new VNode[] { V.Slider(direction: SliderDirection.Horizontal, refCallback: setFlag) };
            var slider = ReconcileAndGet(oldTree);

            // Act
            Reconciler!.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), slider), slider.inverted),
                Is.EqualTo((true, true)));
        }

        [Test]
        public void Given_ARangeWrittenFromARefCallback_When_ALaterRenderChangesOnlyTheDirection_Then_TheRangeSurvives()
        {
            // Arrange
            Func<VisualElement, Action> setRange = el =>
            {
                var target = (Slider)el;
                target.lowValue = -5f;
                target.highValue = 5f;
                return () => { };
            };
            var oldTree = new VNode[] { V.Slider(direction: SliderDirection.Vertical, refCallback: setRange) };
            var newTree = new VNode[] { V.Slider(direction: SliderDirection.Horizontal, refCallback: setRange) };
            var slider = ReconcileAndGet(oldTree);

            // Act
            Reconciler!.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), slider), slider.lowValue, slider.highValue),
                Is.EqualTo((true, -5f, 5f)));
        }

        #endregion

        #region the range beside them

        // GREEN_ON_BASE(characterization): the base applies a lone highValue too; this branch rewrote the
        // condition that admits it, and dropping `|| highValue.HasValue` from that condition reddens this.
        [Test]
        public void Given_OnlyAHighValue_When_TheSliderIsReconciled_Then_TheElementCarriesIt()
        {
            // Arrange
            var tree = new VNode[] { V.Slider(highValue: 4f) };

            // Act
            var slider = ReconcileAndGet(tree);

            // Assert
            Assert.That(slider.highValue, Is.EqualTo(4f));
        }

        [Test]
        public void Given_ADeclaredRangeAndDirection_When_TheSliderIsReconciled_Then_TheElementCarriesTheRange()
        {
            // Arrange
            var tree = new VNode[] { V.Slider(lowValue: 2f, highValue: 4f, direction: SliderDirection.Vertical) };

            // Act
            var slider = ReconcileAndGet(tree);

            // Assert
            Assert.That((slider.lowValue, slider.highValue), Is.EqualTo((2f, 4f)));
        }

        // GREEN_ON_BASE(characterization): the base clamps on this range change as well; this branch
        // rewrote the range condition, and dropping its `LowValue` comparison reddens this.
        [Test]
        public void Given_ADeclaredRange_When_ALaterRenderRaisesOnlyItsLowValueAboveTheValue_Then_TheValueIsClamped()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Slider(value: 2f, lowValue: 0f, highValue: 10f),
            };
            var newTree = new VNode[]
            {
                V.Slider(value: 2f, lowValue: 5f, highValue: 10f),
            };
            var slider = ReconcileAndGet(oldTree);

            // Act
            Reconciler!.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That((slider.lowValue, slider.value), Is.EqualTo((5f, 5f)));
        }

        // GREEN_ON_BASE(characterization): the base clamps on this range change as well; this branch
        // rewrote the range condition, and dropping its `HighValue` comparison reddens this.
        [Test]
        public void Given_ADeclaredRange_When_ALaterRenderLowersOnlyItsHighValueBelowTheValue_Then_TheValueIsClamped()
        {
            // Arrange
            var oldTree = new VNode[]
            {
                V.Slider(value: 8f, lowValue: 0f, highValue: 10f),
            };
            var newTree = new VNode[]
            {
                V.Slider(value: 8f, lowValue: 0f, highValue: 5f),
            };
            var slider = ReconcileAndGet(oldTree);

            // Act
            Reconciler!.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That((slider.highValue, slider.value), Is.EqualTo((5f, 5f)));
        }

        [Test]
        public void Given_ADeclaredRange_When_ALaterRenderDropsItButKeepsTheDirection_Then_TheRangeIsZeroToTen()
        {
            // Arrange
            var oldTree = new VNode[] { V.Slider(lowValue: 2f, highValue: 4f, direction: SliderDirection.Vertical) };
            var newTree = new VNode[] { V.Slider(direction: SliderDirection.Vertical) };
            var slider = ReconcileAndGet(oldTree);
            var whileDeclared = (slider.lowValue, slider.highValue);

            // Act
            Reconciler!.Reconcile(Root, oldTree, newTree);

            // Assert
            Assert.That(
                (whileDeclared.lowValue, whileDeclared.highValue, slider.lowValue, slider.highValue),
                Is.EqualTo((2f, 4f, 0f, 10f)));
        }

        #endregion

        #region pooled reuse

        [Test]
        public void Given_APooledSliderWhoseLastTenantWasVertical_When_APlainSliderRentsIt_Then_ItIsHorizontal()
        {
            // Arrange — the pool is process-wide, and one at its cap drops the return this reads.
            VNodePoolTestAccess.ClearSliderPoolForTest();
            var declaring = new VNode[] { V.Slider(direction: SliderDirection.Vertical) };
            var pooled = ReconcileAndGet(declaring);
            Reconciler!.Reconcile(Root, declaring, Array.Empty<VNode>());
            var plain = new VNode[] { V.Slider() };

            // Act
            var rented = ReconcileAndGet(plain);

            // Assert — the identity term is what makes this a reading of the recycled element; a fresh one
            // is horizontal on its own.
            Assert.That(
                (ReferenceEquals(rented, pooled), rented.direction),
                Is.EqualTo((true, SliderDirection.Horizontal)));
        }

        [Test]
        public void Given_APooledSliderWhoseLastTenantWasInverted_When_APlainSliderRentsIt_Then_ItIsNotInverted()
        {
            // Arrange — same pool reason as above.
            VNodePoolTestAccess.ClearSliderPoolForTest();
            var declaring = new VNode[] { V.Slider(inverted: true) };
            var pooled = ReconcileAndGet(declaring);
            Reconciler!.Reconcile(Root, declaring, Array.Empty<VNode>());
            var plain = new VNode[] { V.Slider() };

            // Act
            var rented = ReconcileAndGet(plain);

            // Assert — same identity term, and for the same reason.
            Assert.That(
                (ReferenceEquals(rented, pooled), rented.inverted),
                Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_APooledSliderWhoseLastTenantDeclaredADirection_When_ItsNextTenantWritesOneFromARefCallback_Then_TheDirectionSurvives()
        {
            // Arrange — the first tenancy records a direction default and then unmounts, which is what
            // hands the element to the shared pool; the second declares only the flag.
            VNodePoolTestAccess.ClearSliderPoolForTest();
            var declaring = new VNode[] { V.Slider(direction: SliderDirection.Horizontal) };
            var pooled = ReconcileAndGet(declaring);
            Reconciler!.Reconcile(Root, declaring, Array.Empty<VNode>());
            Func<VisualElement, Action> setDirection = el =>
            {
                ((Slider)el).direction = SliderDirection.Vertical;
                return () => { };
            };
            var oldTree = new VNode[] { V.Slider(inverted: true, refCallback: setDirection) };
            var newTree = new VNode[] { V.Slider(inverted: false, refCallback: setDirection) };
            Reconciler!.Reconcile(Root, Array.Empty<VNode>(), oldTree);

            // Act
            Reconciler!.Reconcile(Root, oldTree, newTree);

            // Assert — same identity term, and for the same reason.
            Assert.That(
                (ReferenceEquals(Root!.ElementAt(0), pooled), ((Slider)Root!.ElementAt(0)).direction),
                Is.EqualTo((true, SliderDirection.Vertical)));
        }

        #endregion
    }
}
