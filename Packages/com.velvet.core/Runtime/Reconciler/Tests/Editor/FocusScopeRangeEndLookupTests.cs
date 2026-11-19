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
        // GREEN_ON_BASE(characterization): the base finds no end for a start whose end is not among its siblings.
        // Widen `i < parent.hierarchy.childCount` to `i <= parent.hierarchy.childCount` in EndOf and the scan reads
        // one past the last child.
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

        // GREEN_ON_BASE(characterization): the base finds the outer end past a nested start and end.
        // Drop `depth--;` from the nested-end arm of EndOf and the nested end leaves the depth at one, so the outer
        // end is passed over.
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
