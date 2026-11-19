using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// The binding manager's reading of <see cref="FiberDispatchedEventBinding.Capture"/> where no panel
    /// dispatch is involved: what counts as the bindings an element already holds.
    /// <see cref="CaptureEventBindingTests"/> owns the live dispatch.
    /// </summary>
    [TestFixture]
    internal sealed class CaptureEventBindingManagerTests
    {
        private FiberEventBindingManager _manager;

        [SetUp]
        public void SetUp()
        {
            _manager = new FiberEventBindingManager();
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
    }
}
