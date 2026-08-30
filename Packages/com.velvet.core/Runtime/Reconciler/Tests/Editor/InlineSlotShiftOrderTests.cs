using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds the slot shift to siblings the growth can actually move.
    /// <para>
    /// The shift walks the fiber tree's sibling chain, which is creation order —
    /// <c>NextInlineSiblingSlotStart</c> states that a keyed reorder does not resync it. So a
    /// following sibling can hold rows that sit before the growth, and shifting one moves its next
    /// write off the rows it owns. Growth at an index cannot move what starts before it.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class InlineSlotShiftOrderTests
    {
        private static ComponentFiber Mounted(VisualElement target, int slotStart)
        {
            var fiber = new ComponentFiber { MountPoint = target, MountSlotStart = slotStart };
            return fiber;
        }

        [Test]
        public void Given_AFollowingSiblingThatStartsBeforeTheGrowth_When_TheShiftRuns_Then_ItIsLeftAlone()
        {
            // Arrange — the chain is A then B, and B's rows sit first. A keyed reorder leaves exactly
            // this: the order the fibers were created in is not the order their rows sit in.
            var target = new VisualElement();
            var parent = new ComponentFiber();
            var growing = Mounted(target, 5);
            var earlier = Mounted(target, 0);
            parent.AppendChild(growing);
            parent.AppendChild(earlier);

            // Act
            FiberCommitWork.PropagateInlineSlotShift(growing, 2);

            // Assert
            Assert.That(earlier.MountSlotStart, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the control, and a control is green on both sides by
        // construction -- one that reddened would mean the narrowing had stopped the propagation
        // rather than bounding it.
        [Test]
        public void Given_AFollowingSiblingThatStartsAfterTheGrowth_When_TheShiftRuns_Then_ItMoves()
        {
            // Arrange — the control: a shift that moved nothing would satisfy the case above while
            // losing what the propagation is for.
            var target = new VisualElement();
            var parent = new ComponentFiber();
            var growing = Mounted(target, 5);
            var later = Mounted(target, 9);
            parent.AppendChild(growing);
            parent.AppendChild(later);

            // Act
            FiberCommitWork.PropagateInlineSlotShift(growing, 2);

            // Assert
            Assert.That(later.MountSlotStart, Is.EqualTo(11));
        }

        // GREEN_ON_BASE(characterization): the reading this narrows rather than replaces, and the
        // base already takes it. It sits here so a later edit cannot lose one bound while adding
        // the other.
        [Test]
        public void Given_AFollowingSiblingOnAnotherTarget_When_TheShiftRuns_Then_ItIsLeftAlone()
        {
            // Arrange — the other control, and the reading this narrows rather than replaces: a
            // delta measured on one target says nothing about a coordinate into another.
            var parent = new ComponentFiber();
            var growing = Mounted(new VisualElement(), 5);
            var elsewhere = Mounted(new VisualElement(), 9);
            parent.AppendChild(growing);
            parent.AppendChild(elsewhere);

            // Act
            FiberCommitWork.PropagateInlineSlotShift(growing, 2);

            // Assert
            Assert.That(elsewhere.MountSlotStart, Is.EqualTo(9));
        }
    }
}
