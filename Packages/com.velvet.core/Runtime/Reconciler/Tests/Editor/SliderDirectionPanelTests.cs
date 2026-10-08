using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that <c>V.Slider</c> sends Home to its low value and End to its high value on a real panel
    /// whatever <c>direction:</c> and <c>inverted:</c> declare, and that a render changing them alongside a
    /// controlled value reports nothing through <c>onValueChanged:</c>.
    /// </summary>
    /// <remarks>
    /// The panel, and the count each assertion takes before the act, are <c>DelayedFlagCommitReportTests</c>'
    /// and for its reasons.
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

        private static void PressKey(VisualElement element, KeyCode key)
        {
            using (var evt = KeyDownEvent.GetPooled('\0', key, EventModifiers.None))
            {
                evt.target = element;
                element.SendEvent(evt);
            }
        }

        [Test]
        public void Given_AVerticalSlider_When_HomeIsPressed_Then_OnValueChangedReceivesTheLowValue()
        {
            // Arrange
            var reported = new List<float>();
            var slider = Mount(V.Slider(value: Start, lowValue: Low, highValue: High,
                onValueChanged: reported.Add, direction: SliderDirection.Vertical));
            var whileMounted = reported.Count;

            // Act
            PressKey(slider, KeyCode.Home);

            // Assert
            Assert.That((whileMounted, string.Join("|", reported)), Is.EqualTo((0, Low.ToString())));
        }

        [Test]
        public void Given_AVerticalSlider_When_EndIsPressed_Then_OnValueChangedReceivesTheHighValue()
        {
            // Arrange
            var reported = new List<float>();
            var slider = Mount(V.Slider(value: Start, lowValue: Low, highValue: High,
                onValueChanged: reported.Add, direction: SliderDirection.Vertical));
            var whileMounted = reported.Count;

            // Act
            PressKey(slider, KeyCode.End);

            // Assert
            Assert.That((whileMounted, string.Join("|", reported)), Is.EqualTo((0, High.ToString())));
        }

        [Test]
        public void Given_AnInvertedHorizontalSlider_When_HomeIsPressed_Then_OnValueChangedReceivesTheLowValue()
        {
            // Arrange
            var reported = new List<float>();
            var slider = Mount(V.Slider(value: Start, lowValue: Low, highValue: High,
                onValueChanged: reported.Add, inverted: true));
            var whileMounted = reported.Count;

            // Act
            PressKey(slider, KeyCode.Home);

            // Assert
            Assert.That((whileMounted, string.Join("|", reported)), Is.EqualTo((0, Low.ToString())));
        }

        [Test]
        public void Given_AnInvertedHorizontalSlider_When_EndIsPressed_Then_OnValueChangedReceivesTheHighValue()
        {
            // Arrange
            var reported = new List<float>();
            var slider = Mount(V.Slider(value: Start, lowValue: Low, highValue: High,
                onValueChanged: reported.Add, inverted: true));
            var whileMounted = reported.Count;

            // Act
            PressKey(slider, KeyCode.End);

            // Assert
            Assert.That((whileMounted, string.Join("|", reported)), Is.EqualTo((0, High.ToString())));
        }

        [Test]
        public void Given_ASliderShowingItsInputField_When_HomeIsPressedInTheField_Then_TheFieldReceivesTheKey()
        {
            // Arrange
            var slider = Mount(V.Slider(value: Start, lowValue: Low, highValue: High,
                onCreated: element => ((Slider)element).showInputField = true));
            var field = slider.Q(className: Slider.textFieldClassName);
            var received = new List<KeyCode>();
            field.RegisterCallback<KeyDownEvent>(evt => received.Add(evt.keyCode));

            // Act
            PressKey(field, KeyCode.Home);

            // Assert
            Assert.That(string.Join("|", received), Is.EqualTo(KeyCode.Home.ToString()));
        }

        [Test]
        public void Given_ASliderShowingItsInputField_When_HomeIsPressedInTheFieldsTextElement_Then_TheTextElementReceivesTheKey()
        {
            // Arrange
            var slider = Mount(V.Slider(value: Start, lowValue: Low, highValue: High,
                onCreated: element => ((Slider)element).showInputField = true));
            var text = slider.Q(className: Slider.textFieldClassName).Q<TextElement>();
            var received = new List<KeyCode>();
            text.RegisterCallback<KeyDownEvent>(evt => received.Add(evt.keyCode), TrickleDown.TrickleDown);

            // Act
            PressKey(text, KeyCode.Home);

            // Assert
            Assert.That(string.Join("|", received), Is.EqualTo(KeyCode.Home.ToString()));
        }

        [Test]
        public void Given_AHorizontalSlider_When_ALaterRenderChangesItsValueDirectionAndFlag_Then_OnValueChangedIsNotCalled()
        {
            // Arrange
            var reported = new List<float>();
            var oldTree = new VNode[]
            {
                V.Slider(value: Start, lowValue: Low, highValue: High, onValueChanged: reported.Add,
                    direction: SliderDirection.Horizontal),
            };
            var newTree = new VNode[]
            {
                V.Slider(value: 7f, lowValue: Low, highValue: High, onValueChanged: reported.Add,
                    direction: SliderDirection.Vertical, inverted: true),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldTree);
            var slider = (Slider)_root.ElementAt(0);
            var whileMounted = reported.Count;

            // Act
            _reconciler.Reconcile(_root, oldTree, newTree);

            // Assert — the value, direction and flag terms are what make this a reading of a render that
            // reached the slider with all three; one that never did would report nothing as well.
            Assert.That(
                (whileMounted, reported.Count, slider.value, slider.direction, slider.inverted),
                Is.EqualTo((0, 0, 7f, SliderDirection.Vertical, true)));
        }
    }
}
