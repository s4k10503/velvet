using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that <c>V.SliderInt</c>'s keyboard is <c>V.Slider</c>'s on a real panel, reported through an
    /// <c>Action&lt;int&gt;</c>: Home and End on a vertical or inverted slider, the paging keys, the
    /// <c>step:</c> an arrow moves by, the grid a written value lands on, and a pooled slider forgetting its
    /// last tenancy's step. <c>SliderDirectionPanelTests</c> pins the rest of that keyboard on the float
    /// slider, which shares its implementation.
    /// </summary>
    /// <remarks>
    /// A write to <c>SliderInt.value</c> stands for a drag, which writes the same property. The panel, and
    /// the count each assertion takes before the act, are <c>DelayedFlagCommitReportTests</c>' and for its
    /// reasons.
    /// </remarks>
    [TestFixture]
    internal sealed class SliderIntPanelTests : PanelTestBase
    {
        private const int Low = 0;
        private const int High = 100;
        private const int Start = 50;

        private Reconciler _reconciler;
        private VisualElement _root;

        [SetUp]
        public override void SetUp()
        {
            base.SetUp();
            _reconciler = new Reconciler();
            _root = new VisualElement();
            _window.rootVisualElement.Add(_root);
        }

        [TearDown]
        public override void TearDown()
        {
            _reconciler?.Dispose();
            _reconciler = null;
            _root = null;
            base.TearDown();
        }

        private SliderInt Mount(VNode node)
        {
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), new[] { node });
            return (SliderInt)_root.ElementAt(0);
        }

        private SliderInt MountWide(List<int> reported, SliderDirection? direction = null, bool? inverted = null,
            int? step = null) =>
            Mount(V.SliderInt(value: Start, lowValue: Low, highValue: High, onValueChanged: reported.Add,
                direction: direction, inverted: inverted, step: step));

        private static void PressKey(VisualElement element, KeyCode key)
        {
            using (var evt = KeyDownEvent.GetPooled('\0', key, EventModifiers.None))
            {
                evt.target = element;
                element.SendEvent(evt);
            }
        }

        private static void PressArrow(VisualElement element, NavigationMoveEvent.Direction arrow)
        {
            using (var evt = NavigationMoveEvent.GetPooled(arrow, EventModifiers.None))
            {
                evt.target = element;
                element.SendEvent(evt);
            }
        }

        private static string Reported(int whileMounted, List<int> reported) =>
            whileMounted + ":" + string.Join("|", reported);

        [Test]
        public void Given_AVerticalSliderInt_When_HomeIsPressed_Then_OnValueChangedReceivesTheLowValue()
        {
            // Arrange
            var reported = new List<int>();
            var slider = MountWide(reported, SliderDirection.Vertical);
            var whileMounted = reported.Count;

            // Act
            PressKey(slider, KeyCode.Home);

            // Assert — the direction term is what makes the arrangement part of the reading: a horizontal
            // slider's own handler already sends Home to the low value.
            Assert.That(
                (slider.direction, Reported(whileMounted, reported)),
                Is.EqualTo((SliderDirection.Vertical, "0:" + Low)));
        }

        [Test]
        public void Given_AnInvertedSliderInt_When_EndIsPressed_Then_OnValueChangedReceivesTheHighValue()
        {
            // Arrange
            var reported = new List<int>();
            var slider = MountWide(reported, inverted: true);
            var whileMounted = reported.Count;

            // Act
            PressKey(slider, KeyCode.End);

            // Assert — the flag term, for the reason the vertical case gives.
            Assert.That((slider.inverted, Reported(whileMounted, reported)), Is.EqualTo((true, "0:" + High)));
        }

        [Test]
        public void Given_ASliderInt_When_PageUpIsPressed_Then_OnValueChangedReceivesTenSteps()
        {
            // Arrange
            var reported = new List<int>();
            var slider = MountWide(reported);
            var whileMounted = reported.Count;

            // Act
            PressKey(slider, KeyCode.PageUp);

            // Assert
            Assert.That(Reported(whileMounted, reported), Is.EqualTo("0:60"));
        }

        [Test]
        public void Given_ADeclaredStep_When_RightIsPressed_Then_OnValueChangedReceivesOneStep()
        {
            // Arrange
            var reported = new List<int>();
            var slider = MountWide(reported, step: 5);
            var whileMounted = reported.Count;

            // Act
            PressArrow(slider, NavigationMoveEvent.Direction.Right);

            // Assert
            Assert.That(Reported(whileMounted, reported), Is.EqualTo("0:55"));
        }

        [Test]
        public void Given_ADeclaredStep_When_TheValueIsWrittenOffTheGrid_Then_OnValueChangedReceivesTheNearestGridLine()
        {
            // Arrange
            var reported = new List<int>();
            var slider = MountWide(reported, step: 5);
            var whileMounted = reported.Count;

            // Act
            slider.value = 53;

            // Assert
            Assert.That((Reported(whileMounted, reported), slider.value), Is.EqualTo(("0:55", 55)));
        }

        [Test]
        public void Given_APooledSliderIntWhoseLastTenantDeclaredAStep_When_ASliderIntDeclaringNoSettingsRentsIt_Then_RightMovesByOne()
        {
            // Arrange — the pool is process-wide, and one at its cap drops the return this reads. The second
            // tenancy declares no range, direction, flag or step, so nothing it renders writes the step and
            // only the pool return can have taken the first tenancy's away.
            VNodePoolTestAccess.ClearSliderIntPoolForTest();
            var declaring = new VNode[] { V.SliderInt(step: 5) };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), declaring);
            var pooled = _root.ElementAt(0);
            _reconciler.Reconcile(_root, declaring, Array.Empty<VNode>());
            var reported = new List<int>();
            var rented = Mount(V.SliderInt(value: 5, onValueChanged: reported.Add));
            var whileRented = reported.Count;

            // Act
            PressArrow(rented, NavigationMoveEvent.Direction.Right);

            // Assert — the identity term is what makes this a reading of the recycled element; a fresh one
            // moves by one on its own.
            Assert.That(
                (ReferenceEquals(rented, pooled), Reported(whileRented, reported)),
                Is.EqualTo((true, "0:6")));
        }
    }
}
