using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that <c>V.Slider</c>'s keyboard and pointer input are Radix's <c>Slider</c> on a real panel
    /// whatever <c>direction:</c> and <c>inverted:</c> declare: Home and End, the paging keys, the arrows
    /// and Shift+arrow, the <c>step:</c> they move by and the grid a dragged value lands on. It also pins
    /// that a render changing them alongside a controlled value reports nothing through
    /// <c>onValueChanged:</c>.
    /// </summary>
    /// <remarks>
    /// A write to <c>Slider.value</c> stands for a drag, which writes the same property. The panel, and
    /// the count each assertion takes before the act, are <c>DelayedFlagCommitReportTests</c>' and for
    /// its reasons.
    /// </remarks>
    [TestFixture]
    internal sealed class SliderDirectionPanelTests : PanelTestBase
    {
        private const float Low = 0f;
        private const float High = 10f;
        private const float Start = 5f;
        private const float WideHigh = 100f;
        private const float WideStart = 50f;

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

        private static void PressArrow(VisualElement element, NavigationMoveEvent.Direction arrow,
            EventModifiers modifiers = EventModifiers.None)
        {
            using (var evt = NavigationMoveEvent.GetPooled(arrow, modifiers))
            {
                evt.target = element;
                element.SendEvent(evt);
            }
        }

        private readonly Dictionary<List<float>, Action<float>> _recorders = new();

        // One delegate per list, so a render and the one after it hand the element the same handler.
        private Action<float> Recorder(List<float> reported)
        {
            if (!_recorders.TryGetValue(reported, out var recorder))
            {
                recorder = reported.Add;
                _recorders[reported] = recorder;
            }

            return recorder;
        }

        private VNode WideNode(List<float> reported, float value, SliderDirection? direction = null,
            bool? inverted = null, float? step = null, Action<VisualElement> onCreated = null) =>
            V.Slider(value: value, lowValue: Low, highValue: WideHigh, onValueChanged: Recorder(reported),
                direction: direction, inverted: inverted, step: step, onCreated: onCreated);

        private Slider MountWide(List<float> reported, SliderDirection? direction = null, bool? inverted = null,
            float? step = null, float start = WideStart, Action<VisualElement> onCreated = null) =>
            Mount(WideNode(reported, start, direction, inverted, step, onCreated));

        private static string Reported(int whileMounted, List<float> reported) =>
            whileMounted + ":" + string.Join("|", reported);

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

        // Radix's BACK_KEYS, one row per arrow and slide direction.
        [TestCase(false, false, NavigationMoveEvent.Direction.Right, 51f)]
        [TestCase(false, false, NavigationMoveEvent.Direction.Up, 51f)]
        [TestCase(false, false, NavigationMoveEvent.Direction.Left, 49f)]
        [TestCase(false, false, NavigationMoveEvent.Direction.Down, 49f)]
        [TestCase(false, true, NavigationMoveEvent.Direction.Left, 51f)]
        [TestCase(false, true, NavigationMoveEvent.Direction.Up, 51f)]
        [TestCase(false, true, NavigationMoveEvent.Direction.Right, 49f)]
        [TestCase(false, true, NavigationMoveEvent.Direction.Down, 49f)]
        [TestCase(true, false, NavigationMoveEvent.Direction.Right, 51f)]
        [TestCase(true, false, NavigationMoveEvent.Direction.Up, 51f)]
        [TestCase(true, false, NavigationMoveEvent.Direction.Left, 49f)]
        [TestCase(true, false, NavigationMoveEvent.Direction.Down, 49f)]
        [TestCase(true, true, NavigationMoveEvent.Direction.Right, 51f)]
        [TestCase(true, true, NavigationMoveEvent.Direction.Down, 51f)]
        [TestCase(true, true, NavigationMoveEvent.Direction.Left, 49f)]
        [TestCase(true, true, NavigationMoveEvent.Direction.Up, 49f)]
        public void Given_ASliderOfADirection_When_AnArrowIsPressed_Then_OnValueChangedReceivesTheStep(
            bool vertical, bool inverted, NavigationMoveEvent.Direction arrow, float expected)
        {
            // Arrange
            var reported = new List<float>();
            var slider = MountWide(reported, vertical ? SliderDirection.Vertical : SliderDirection.Horizontal, inverted);
            var whileMounted = reported.Count;

            // Act
            PressArrow(slider, arrow);

            // Assert
            Assert.That(Reported(whileMounted, reported), Is.EqualTo("0:" + expected));
        }

        [Test]
        public void Given_AHorizontalSlider_When_ShiftRightIsPressed_Then_OnValueChangedReceivesTenSteps()
        {
            // Arrange
            var reported = new List<float>();
            var slider = MountWide(reported);
            var whileMounted = reported.Count;

            // Act
            PressArrow(slider, NavigationMoveEvent.Direction.Right, EventModifiers.Shift);

            // Assert
            Assert.That(Reported(whileMounted, reported), Is.EqualTo("0:60"));
        }

        [Test]
        public void Given_AHorizontalSlider_When_TheNextMoveIsPressed_Then_OnValueChangedIsNotCalled()
        {
            // Arrange
            var reported = new List<float>();
            var slider = MountWide(reported);
            var whileMounted = reported.Count;

            // Act
            PressArrow(slider, NavigationMoveEvent.Direction.Next);

            // Assert
            Assert.That(Reported(whileMounted, reported), Is.EqualTo("0:"));
        }

        [Test]
        public void Given_AHorizontalSliderAtItsHighValue_When_RightIsPressed_Then_OnValueChangedIsNotCalled()
        {
            // Arrange
            var reported = new List<float>();
            var slider = MountWide(reported, start: WideHigh);
            var whileMounted = reported.Count;

            // Act
            PressArrow(slider, NavigationMoveEvent.Direction.Right);

            // Assert
            Assert.That(Reported(whileMounted, reported), Is.EqualTo("0:"));
        }

        // The paging keys are back and forward whatever the slide direction.
        [TestCase(false, false, KeyCode.PageUp, 60f)]
        [TestCase(false, false, KeyCode.PageDown, 40f)]
        [TestCase(false, true, KeyCode.PageUp, 60f)]
        [TestCase(true, false, KeyCode.PageUp, 60f)]
        [TestCase(true, false, KeyCode.PageDown, 40f)]
        [TestCase(true, true, KeyCode.PageUp, 60f)]
        [TestCase(true, true, KeyCode.PageDown, 40f)]
        public void Given_ASliderOfADirection_When_APagingKeyIsPressed_Then_OnValueChangedReceivesTenSteps(
            bool vertical, bool inverted, KeyCode key, float expected)
        {
            // Arrange
            var reported = new List<float>();
            var slider = MountWide(reported, vertical ? SliderDirection.Vertical : SliderDirection.Horizontal, inverted);
            var whileMounted = reported.Count;

            // Act
            PressKey(slider, key);

            // Assert
            Assert.That(Reported(whileMounted, reported), Is.EqualTo("0:" + expected));
        }

        [Test]
        public void Given_ADeclaredStep_When_RightIsPressed_Then_OnValueChangedReceivesOneStep()
        {
            // Arrange
            var reported = new List<float>();
            var slider = MountWide(reported, step: 5f);
            var whileMounted = reported.Count;

            // Act
            PressArrow(slider, NavigationMoveEvent.Direction.Right);

            // Assert
            Assert.That(Reported(whileMounted, reported), Is.EqualTo("0:55"));
        }

        [Test]
        public void Given_ADeclaredStep_When_PageUpIsPressed_Then_OnValueChangedReceivesTenSteps()
        {
            // Arrange
            var reported = new List<float>();
            var slider = MountWide(reported, step: 5f);
            var whileMounted = reported.Count;

            // Act
            PressKey(slider, KeyCode.PageUp);

            // Assert
            Assert.That(Reported(whileMounted, reported), Is.EqualTo("0:100"));
        }

        [Test]
        public void Given_AValueOffTheStepGrid_When_RightIsPressed_Then_OnValueChangedReceivesTheNextGridLine()
        {
            // Arrange
            var reported = new List<float>();
            var slider = MountWide(reported, step: 5f, start: 52f);
            var whileMounted = reported.Count;

            // Act
            PressArrow(slider, NavigationMoveEvent.Direction.Right);

            // Assert
            Assert.That(Reported(whileMounted, reported), Is.EqualTo("0:55"));
        }

        [Test]
        public void Given_AValueOffTheStepGrid_When_LeftIsPressed_Then_OnValueChangedReceivesThePreviousGridLine()
        {
            // Arrange
            var reported = new List<float>();
            var slider = MountWide(reported, step: 5f, start: 52f);
            var whileMounted = reported.Count;

            // Act
            PressArrow(slider, NavigationMoveEvent.Direction.Left);

            // Assert
            Assert.That(Reported(whileMounted, reported), Is.EqualTo("0:50"));
        }

        [Test]
        public void Given_AStepThatDoesNotDivideTheRange_When_EndIsPressed_Then_OnValueChangedReceivesTheNearestGridLine()
        {
            // Arrange
            var reported = new List<float>();
            var slider = MountWide(reported, step: 30f);
            var whileMounted = reported.Count;

            // Act
            PressKey(slider, KeyCode.End);

            // Assert
            Assert.That(Reported(whileMounted, reported), Is.EqualTo("0:90"));
        }

        [Test]
        public void Given_ADeclaredStep_When_ALaterRenderChangesIt_Then_RightMovesByTheNewStep()
        {
            // Arrange
            var reported = new List<float>();
            var slider = MountWide(reported, step: 5f);
            var oldTree = new[] { WideNode(reported, WideStart, step: 5f) };
            var newTree = new[] { WideNode(reported, WideStart, step: 10f) };
            _reconciler.Reconcile(_root, oldTree, newTree);
            var whileRendered = reported.Count;

            // Act
            PressArrow(slider, NavigationMoveEvent.Direction.Right);

            // Assert
            Assert.That(Reported(whileRendered, reported), Is.EqualTo("0:60"));
        }

        [Test]
        public void Given_ADeclaredStep_When_ALaterRenderDropsIt_Then_RightMovesByOne()
        {
            // Arrange
            var reported = new List<float>();
            var slider = MountWide(reported, step: 5f);
            var oldTree = new[] { WideNode(reported, WideStart, step: 5f) };
            var newTree = new[] { WideNode(reported, WideStart) };
            _reconciler.Reconcile(_root, oldTree, newTree);
            var whileRendered = reported.Count;

            // Act
            PressArrow(slider, NavigationMoveEvent.Direction.Right);

            // Assert
            Assert.That(Reported(whileRendered, reported), Is.EqualTo("0:51"));
        }

        [Test]
        public void Given_APooledSliderWhoseLastTenantDeclaredAStep_When_APlainSliderRentsIt_Then_RightMovesByOne()
        {
            // Arrange — the pool is process-wide, and one at its cap drops the return this reads.
            VNodePoolTestAccess.ClearSliderPoolForTest();
            var declaring = new[] { WideNode(new List<float>(), Low, step: 5f) };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), declaring);
            var pooled = _root.ElementAt(0);
            _reconciler.Reconcile(_root, declaring, Array.Empty<VNode>());
            var reported = new List<float>();
            var rented = MountWide(reported);
            var whileRented = reported.Count;

            // Act
            PressArrow(rented, NavigationMoveEvent.Direction.Right);

            // Assert — the identity term is what makes this a reading of the recycled element; a fresh one
            // moves by one on its own.
            Assert.That(
                (ReferenceEquals(rented, pooled), Reported(whileRented, reported)),
                Is.EqualTo((true, "0:51")));
        }

        [Test]
        public void Given_ADeclaredStep_When_TheValueIsWrittenOffTheGrid_Then_OnValueChangedReceivesTheNearestGridLine()
        {
            // Arrange
            var reported = new List<float>();
            var slider = MountWide(reported, step: 5f);
            var whileMounted = reported.Count;

            // Act
            slider.value = 53f;

            // Assert
            Assert.That(Reported(whileMounted, reported), Is.EqualTo("0:55"));
        }

        [Test]
        public void Given_ADeclaredStep_When_TheValueIsWrittenOffTheGrid_Then_TheSliderHoldsTheGridLine()
        {
            // Arrange
            var reported = new List<float>();
            var slider = MountWide(reported, step: 5f);

            // Act
            slider.value = 53f;

            // Assert
            Assert.That(slider.value, Is.EqualTo(55f));
        }

        [Test]
        public void Given_ADeclaredStep_When_TheValueIsWrittenBackToTheGridLineItHeld_Then_OnValueChangedIsNotCalled()
        {
            // Arrange
            var reported = new List<float>();
            var slider = MountWide(reported, step: 5f);
            var whileMounted = reported.Count;

            // Act
            slider.value = 51f;

            // Assert
            Assert.That((Reported(whileMounted, reported), slider.value), Is.EqualTo(("0:", 50f)));
        }

        [Test]
        public void Given_NoStep_When_TheValueIsWrittenBetweenWholeNumbers_Then_OnValueChangedReceivesTheNearestOne()
        {
            // Arrange
            var reported = new List<float>();
            var slider = MountWide(reported);
            var whileMounted = reported.Count;

            // Act
            slider.value = 52.4f;

            // Assert
            Assert.That(Reported(whileMounted, reported), Is.EqualTo("0:52"));
        }

        [Test]
        public void Given_ASliderShowingItsInputField_When_AnArrowIsAimedAtTheField_Then_OnValueChangedIsNotCalled()
        {
            // Arrange
            var reported = new List<float>();
            var slider = MountWide(reported, onCreated: element => ((Slider)element).showInputField = true);
            var field = slider.Q(className: Slider.textFieldClassName);
            var whileMounted = reported.Count;

            // Act
            PressArrow(field, NavigationMoveEvent.Direction.Right);

            // Assert
            Assert.That(Reported(whileMounted, reported), Is.EqualTo("0:"));
        }

        [Test]
        public void Given_AVerticalSliderShowingItsInputField_When_HomeIsAimedAtTheField_Then_OnValueChangedIsNotCalled()
        {
            // Arrange
            var reported = new List<float>();
            var slider = Mount(V.Slider(value: Start, lowValue: Low, highValue: High, onValueChanged: reported.Add,
                direction: SliderDirection.Vertical, onCreated: element => ((Slider)element).showInputField = true));
            var field = slider.Q(className: Slider.textFieldClassName);
            var whileMounted = reported.Count;

            // Act
            PressKey(field, KeyCode.Home);

            // Assert
            Assert.That(Reported(whileMounted, reported), Is.EqualTo("0:"));
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

        // GREEN_ON_BASE(refactor): the base already places the value between widening and narrowing the range.
        // This change moves that write into FiberPropApplier.WriteRange, and deleting its `declaredValue is T`
        // block reddens this: narrowing the high bound then clamps 50 to 10 and reports it. It needs a panel
        // because the slider's clamp notifies only on one.
        [Test]
        public void Given_AValueOutsideTheNewRange_When_ALaterRenderMovesTheRangeAndTheValueTogether_Then_OnValueChangedIsNotCalled()
        {
            // Arrange
            var reported = new List<float>();
            var oldTree = new VNode[]
            {
                V.Slider(value: 50f, lowValue: 0f, highValue: 100f, onValueChanged: Recorder(reported)),
            };
            var newTree = new VNode[]
            {
                V.Slider(value: 7f, lowValue: 5f, highValue: 10f, onValueChanged: Recorder(reported)),
            };
            _reconciler.Reconcile(_root, Array.Empty<VNode>(), oldTree);
            var slider = (Slider)_root.ElementAt(0);
            var whileMounted = reported.Count;

            // Act
            _reconciler.Reconcile(_root, oldTree, newTree);

            // Assert
            Assert.That(
                (Reported(whileMounted, reported), slider.lowValue, slider.highValue, slider.value),
                Is.EqualTo(("0:", 5f, 10f, 7f)));
        }
    }
}
