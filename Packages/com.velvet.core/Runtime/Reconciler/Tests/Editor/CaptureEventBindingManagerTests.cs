using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// The binding manager's reading of <see cref="FiberDispatchedEventBinding.Capture"/> where no panel
    /// dispatch is involved: what counts as the bindings an element already holds, and the order a
    /// synthetic invocation at the event's target takes. <see cref="CaptureEventBindingTests"/> owns the
    /// live dispatch.
    /// </summary>
    [TestFixture]
    internal sealed class CaptureEventBindingManagerTests
    {
        private static readonly List<string> s_log = new();

        private FiberEventBindingManager _manager;

        [SetUp]
        public void SetUp()
        {
            _manager = new FiberEventBindingManager();
            s_log.Clear();
        }

        [TearDown]
        public void TearDown() => _manager.Clear();

        private static void Handler(PointerDownEvent _) { }

        [Test]
        public void Given_OneDelegateBoundInBothPhases_When_ComparedWithTheSameTwoBindings_Then_HasSameBindings()
        {
            // Arrange
            var element = new VisualElement();
            var events = new FiberEventBinding[]
            {
                new PointerDownBinding { Handler = Handler },
                new PointerDownBinding { Handler = Handler, Capture = true },
            };
            foreach (var binding in events) _manager.Bind(element, binding);

            // Act
            var same = _manager.HasSameBindings(element, events);

            // Assert
            Assert.That(same, Is.True);
        }

        [Test]
        public void Given_ADelegateBoundToBubble_When_ComparedWithItBoundToCapture_Then_NotSameBindings()
        {
            // Arrange
            var element = new VisualElement();
            _manager.Bind(element, new PointerDownBinding { Handler = Handler });

            // Act
            var same = _manager.HasSameBindings(element,
                new FiberEventBinding[] { new PointerDownBinding { Handler = Handler, Capture = true } });

            // Assert
            Assert.That(same, Is.False);
        }

        [Test]
        public void Given_ABubbleBindingBoundAheadOfACaptureBinding_When_InvokedSyntheticallyAsTheTarget_Then_TheCaptureBindingRunsFirst()
        {
            // Arrange — the shape the layer router hands an element it picked: the element is the event's
            // target, where React runs capture handlers ahead of bubble handlers.
            var element = new VisualElement();
            _manager.Bind(element, new PointerDownBinding { Handler = _ => s_log.Add("bubble") });
            _manager.Bind(element, new PointerDownBinding { Handler = _ => s_log.Add("capture"), Capture = true });
            using var evt = PointerDownEvent.GetPooled();

            // Act
            _manager.TryInvokeSynthetic(element, evt);

            // Assert
            Assert.That(string.Join(",", s_log), Is.EqualTo("capture,bubble"));
        }
    }
}
