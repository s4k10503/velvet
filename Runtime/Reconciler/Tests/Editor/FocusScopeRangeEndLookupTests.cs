using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds the lookup of the end that closes a focus scope's start among their parent's children, on the element
    /// lists alone.
    /// </summary>
    [TestFixture]
    internal sealed class FocusScopeRangeEndLookupTests
    {
        [Test]
        public void Given_AStartWhoseEndIsNotAmongItsSiblings_When_ItsEndIsLookedUp_Then_NoEndIsFound()
        {
            // Arrange
            var parent = new VisualElement();
            var start = new FocusScopeStart();
            parent.Add(start);
            parent.Add(new VisualElement());

            // Act
            var end = FocusScopeRange.EndOf(start);

            // Assert
            Assert.IsNull(end);
        }

        [Test]
        public void Given_ANestedStartAndEndBetweenAStartAndItsEnd_When_TheOuterEndIsLookedUp_Then_TheOuterEndIsFound()
        {
            // Arrange
            var parent = new VisualElement();
            var outerStart = new FocusScopeStart();
            var outerEnd = new FocusScopeEnd();
            parent.Add(outerStart);
            parent.Add(new FocusScopeStart());
            parent.Add(new FocusScopeEnd());
            parent.Add(outerEnd);

            // Act
            var end = FocusScopeRange.EndOf(outerStart);

            // Assert
            Assert.That(end, Is.SameAs(outerEnd));
        }
    }
}
