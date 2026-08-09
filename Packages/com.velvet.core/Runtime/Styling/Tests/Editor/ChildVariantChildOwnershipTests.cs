using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies who may turn a <c>[&amp;&gt;*]:</c> payload back off on a child.
    /// <see cref="StyleChildVariantManipulator"/> tracks the children it wrote to by reference, and a child
    /// pooled out of one container and re-rented under another is in two containers' lists at once — the old
    /// one's walk runs on the next geometry event, by which point the payload on that element belongs to
    /// somebody else. The two containers must carry the same payload for the turn-off to take anything away.
    /// </summary>
    [TestFixture]
    internal sealed class ChildVariantChildOwnershipTests
    {
        private const string Payload = "bg-red-500";

        private static (VisualElement Container, StyleChildVariantManipulator Walk) Container(ReconcilerContext ctx)
        {
            var container = new VisualElement();
            var walk = new StyleChildVariantManipulator(ctx, new[] { Payload });
            container.AddManipulator(walk);
            return (container, walk);
        }

        [Test]
        public void Given_AChildReRentedByAnotherContainer_When_TheOldWalkRunsAfterwards_Then_ThePayloadSurvives()
        {
            // Arrange — the child is applied to under the first container, then moves to the second, which
            // applies the same payload. Both containers hold a reference to it now.
            var ctx = new ReconcilerContext();
            var (first, firstWalk) = Container(ctx);
            var (second, secondWalk) = Container(ctx);
            var child = new VisualElement();
            first.Add(child);
            firstWalk.Apply();
            Assume.That(child.ClassListContains(Payload), Is.True, "Precondition: the first container applied it");
            first.Remove(child);
            second.Add(child);
            secondWalk.Apply();

            // Act — the geometry event the first container gets for having lost a child.
            firstWalk.Apply();

            // Assert
            Assert.That(child.ClassListContains(Payload), Is.True);
        }

        [Test]
        public void Given_AChildReRentedByAnotherContainer_When_TheOldContainerIsTornDown_Then_ThePayloadSurvives()
        {
            // Arrange — the same ownership question reached through detach rather than through the walk.
            var ctx = new ReconcilerContext();
            var (first, firstWalk) = Container(ctx);
            var (second, secondWalk) = Container(ctx);
            var child = new VisualElement();
            first.Add(child);
            firstWalk.Apply();
            first.Remove(child);
            second.Add(child);
            secondWalk.Apply();
            Assume.That(child.ClassListContains(Payload), Is.True, "Precondition: the second container applied it");

            // Act
            first.RemoveManipulator(firstWalk);

            // Assert
            Assert.That(child.ClassListContains(Payload), Is.True);
        }

        [Test]
        public void Given_AChildThatLeftAndWasNotReRented_When_TheWalkRunsAfterwards_Then_ThePayloadIsRemoved()
        {
            // Arrange — the case the stale-reference sweep exists for, which the ownership check must not
            // suppress: a child reparented out of the container and claimed by nobody keeps no residue.
            var ctx = new ReconcilerContext();
            var (first, firstWalk) = Container(ctx);
            var child = new VisualElement();
            first.Add(child);
            firstWalk.Apply();
            first.Remove(child);
            new VisualElement().Add(child);

            // Act
            firstWalk.Apply();

            // Assert
            Assert.That(child.ClassListContains(Payload), Is.False);
        }
    }
}
