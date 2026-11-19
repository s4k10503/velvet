using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies who may turn a <c>[&amp;&gt;*]:</c> payload back off on a child.
    /// <see cref="StyleChildVariantManipulator"/> tracks the children it wrote to by reference, and a child
    /// pooled out of one container and re-rented under another is in two containers' lists at once — the old
    /// one's walk would turn it off when it is next re-entered, by which point the payload on that element
    /// belongs to somebody else. A class payload needs the two containers to carry the same token for
    /// anything to be taken away; an arbitrary one does not, an inline layer being keyed by property and
    /// priority rather than by the token that wrote it.
    /// </summary>
    [TestFixture]
    internal sealed class ChildVariantChildOwnershipTests
    {
        private const string Payload = "bg-red-500";

        private static (VisualElement Container, StyleChildVariantManipulator Walk) Container(
            ReconcilerContext ctx, string payload = Payload)
        {
            var container = new VisualElement();
            var walk = new StyleChildVariantManipulator(ctx, new[] { payload });
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
            var tracked = child.ClassListContains(Payload);
            first.Remove(child);
            second.Add(child);
            secondWalk.Apply();

            // Act
            firstWalk.Apply();

            // Assert — the first container's own application rides along: with nothing tracked there, the
            // release under test never runs and the class the second wrote survives for the wrong reason.
            Assert.That((tracked, child.ClassListContains(Payload)), Is.EqualTo((true, true)));
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
            var tracked = child.ClassListContains(Payload);
            first.Remove(child);
            second.Add(child);
            secondWalk.Apply();

            // Act
            first.RemoveManipulator(firstWalk);

            // Assert — as above, the first container's own application rides along.
            Assert.That((tracked, child.ClassListContains(Payload)), Is.EqualTo((true, true)));
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
            var applied = child.ClassListContains(Payload);
            first.Remove(child);
            new VisualElement().Add(child);

            // Act
            firstWalk.Apply();

            // Assert — the precondition rides along, since a payload that was never applied and one the
            // sweep removed leave the same class list.
            Assert.That((applied, child.ClassListContains(Payload)), Is.EqualTo((true, false)));
        }

        [Test]
        public void Given_TwoContainersWithDifferentArbitraryPayloads_When_TheOldWalkRunsAfterwards_Then_TheNewOneSurvives()
        {
            // Arrange — an inline layer is keyed by property and priority, so the turn-off does not need the
            // two tokens to match: the width the second container wrote is what the first would take away.
            var ctx = new ReconcilerContext();
            var (first, firstWalk) = Container(ctx, "w-[8px]");
            var (second, secondWalk) = Container(ctx, "w-[12px]");
            var child = new VisualElement();
            first.Add(child);
            firstWalk.Apply();
            var tracked = child.style.width.value.value;
            first.Remove(child);
            second.Add(child);
            secondWalk.Apply();

            // Act
            firstWalk.Apply();

            // Assert — as above, the first container's own application rides along.
            Assert.That((tracked, child.style.width.value.value), Is.EqualTo((8f, 12f)));
        }
    }
}
