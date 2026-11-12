using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that StyleCompositeFlowManipulator takes back what it forwarded onto a composite widget's inner box
    /// once it leaves the widget.
    /// </summary>
    internal sealed class CompositeFlowTakeBackTests
    {
        // GREEN_ON_BASE(characterization): the base nulls the forwarded wrap on the inner box when the manipulator leaves.
        [Test]
        public void Given_AWrapForwardedOntoAScrollViewsContent_When_TheManipulatorIsRemoved_Then_TheWrapIsTakenBack()
        {
            // Arrange
            var widget = new ScrollView();
            widget.AddToClassList("flex-wrap");
            var manipulator = new StyleCompositeFlowManipulator(new ReconcilerContext());
            widget.AddManipulator(manipulator);
            var forwarded = widget.contentContainer.style.flexWrap.keyword == StyleKeyword.Undefined;

            // Act
            widget.RemoveManipulator(manipulator);

            // Assert — null where nothing was forwarded, which would leave nothing to take back.
            Assert.That(forwarded ? widget.contentContainer.style.flexWrap.keyword : (StyleKeyword?)null,
                Is.EqualTo(StyleKeyword.Null));
        }
    }
}
