using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that <c>V.Slider</c>'s <c>direction:</c> and <c>inverted:</c> change what keyboard input does
    /// to the slider on a real panel, not only the properties <c>SliderDirectionPropTests</c> reads, and
    /// that a render changing them alongside a controlled value reports nothing through
    /// <c>onValueChanged:</c>.
    /// </summary>
    /// <remarks>
    /// Home is the key read, because the end it sends a slider to is the reading that separates the cases:
    /// <c>lowValue</c> for a slider that is horizontal and not inverted, <c>highValue</c> for one carrying
    /// either declaration alone. The panel, and the count each assertion takes before the act, are
    /// <c>DelayedFlagCommitReportTests</c>' and for its reasons.
    /// </remarks>
    [TestFixture]
    internal sealed class SliderDirectionPanelTests : PanelTestBase
    {
        private const float Low = 0f;
        private const float High = 10f;
        private const float Start = 5f;

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

        private Slider Mount(VNode node)
        {
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), new[] { node });
            return (Slider)_root.ElementAt(0);
        }

        private static void PressHome(Slider slider)
        {
            using (var evt = KeyDownEvent.GetPooled('\0', KeyCode.Home, EventModifiers.None))
            {
                evt.target = slider;
                slider.SendEvent(evt);
            }
        }

        [Test]
        public void Given_AVerticalSlider_When_HomeIsPressed_Then_OnValueChangedReceivesTheHighValue()
        {
            // Arrange
            var reported = new List<float>();
            var slider = Mount(V.Slider(value: Start, lowValue: Low, highValue: High,
                onValueChanged: reported.Add, direction: SliderDirection.Vertical));
            var whileMounted = reported.Count;

            // Act
            PressHome(slider);

            // Assert
            Assert.That((whileMounted, string.Join("|", reported)), Is.EqualTo((0, High.ToString())));
        }

        [Test]
        public void Given_AnInvertedHorizontalSlider_When_HomeIsPressed_Then_OnValueChangedReceivesTheHighValue()
        {
            // Arrange
            var reported = new List<float>();
            var slider = Mount(V.Slider(value: Start, lowValue: Low, highValue: High,
                onValueChanged: reported.Add, inverted: true));
            var whileMounted = reported.Count;

            // Act
            PressHome(slider);

            // Assert
            Assert.That((whileMounted, string.Join("|", reported)), Is.EqualTo((0, High.ToString())));
        }

        [Test]
        public void Given_AVerticalSliderALaterRenderStopsDeclaringVertical_When_HomeIsPressed_Then_OnValueChangedReceivesTheLowValue()
        {
            // Arrange
            var reported = new List<float>();
            var oldTree = new VNode[]
            {
                V.Slider(value: Start, lowValue: Low, highValue: High, onValueChanged: reported.Add,
                    direction: SliderDirection.Vertical),
            };
            var newTree = new VNode[]
            {
                V.Slider(value: Start, lowValue: Low, highValue: High, onValueChanged: reported.Add),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldTree);
            _reconciler.Reconcile(_root, oldTree, newTree);
            var slider = (Slider)_root.ElementAt(0);
            var whileRendered = reported.Count;

            // Act
            PressHome(slider);

            // Assert
            Assert.That((whileRendered, string.Join("|", reported)), Is.EqualTo((0, Low.ToString())));
        }

        [Test]
        public void Given_AVerticalSlider_When_ALaterRenderChangesItsValueDirectionAndFlag_Then_OnValueChangedIsNotCalled()
        {
            // Arrange
            var reported = new List<float>();
            var oldTree = new VNode[]
            {
                V.Slider(value: Start, lowValue: Low, highValue: High, onValueChanged: reported.Add,
                    direction: SliderDirection.Vertical),
            };
            var newTree = new VNode[]
            {
                V.Slider(value: 7f, lowValue: Low, highValue: High, onValueChanged: reported.Add,
                    direction: SliderDirection.Horizontal, inverted: true),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldTree);
            var slider = (Slider)_root.ElementAt(0);
            var whileMounted = reported.Count;

            // Act
            _reconciler.Reconcile(_root, oldTree, newTree);

            // Assert — the value term is what makes this a reading of a render that reached the slider; one
            // that never did would report nothing as well.
            Assert.That((whileMounted, reported.Count, slider.value), Is.EqualTo((0, 0, 7f)));
        }
    }
}
