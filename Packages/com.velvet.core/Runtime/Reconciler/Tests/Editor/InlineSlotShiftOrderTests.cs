using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Holds the slot shift to the components the growth can actually move: one whose rows start after the
    /// growing component's moves, one whose rows start before it stays, one on another container stays, and
    /// the growing component keeps its own start. The row counts follow the growth for the component holding
    /// the grower's rows, and not for the one rendering their container.
    /// <para>
    /// The components are mounted, so the shift finds them where it looks, and their recorded starts are then
    /// set by hand to the arrangement each case asks about. A keyed reorder leaves the first of those: the
    /// order the fibers were created in is not the order their rows sit in.
    /// </para>
    /// </summary>
    [TestFixture]
    internal sealed class InlineSlotShiftOrderTests
    {
        private static ComponentFiber s_growing;
        private static ComponentFiber s_other;
        private static ComponentFiber s_holder;
        private static ComponentFiber s_outer;

        [SetUp]
        public void SetUp()
        {
            s_growing = null;
            s_other = null;
            s_holder = null;
            s_outer = null;
        }

        [Component]
        private static VNode GrowingRender()
        {
            s_growing = FiberAmbientStack.Current;
            return V.Label(name: "growing", key: "growing");
        }

        [Component]
        private static VNode OtherRender()
        {
            s_other = FiberAmbientStack.Current;
            return V.Label(name: "other", key: "other");
        }

        [Component]
        private static VNode EmptyRender()
        {
            s_growing = FiberAmbientStack.Current;
            return V.Fragment(children: System.Array.Empty<VNode>());
        }

        [Component]
        private static VNode HolderRender()
        {
            s_holder = FiberAmbientStack.Current;
            return V.Component(GrowingRender, key: "growing");
        }

        [Component]
        private static VNode OuterRender()
        {
            s_outer = FiberAmbientStack.Current;
            return V.Div(children: new VNode[] { V.Component(HolderRender, key: "holder") });
        }

        [Component]
        private static VNode EmptyThenOtherRender()
            => V.Div(children: new VNode[]
            {
                V.Component(EmptyRender, key: "empty"),
                V.Component(OtherRender, key: "other"),
            });

        [Component]
        private static VNode SharedContainerRender()
            => V.Div(children: new VNode[]
            {
                V.Component(GrowingRender, key: "growing"),
                V.Component(OtherRender, key: "other"),
            });

        [Component]
        private static VNode SeparateContainersRender()
            => V.Fragment(children: new VNode[]
            {
                V.Div(children: new VNode[] { V.Component(GrowingRender, key: "growing") }),
                V.Div(children: new VNode[] { V.Component(OtherRender, key: "other") }),
            });

        // GREEN_ON_BASE(characterization): the base's sibling chain skips a start before the growth too.
        // What it pins is that reading every component on the container keeps that bound.
        [Test]
        public void Given_AComponentWhoseRowsStartBeforeTheGrowth_When_TheShiftRuns_Then_ItIsLeftAlone()
        {
            // Arrange — created growing first, rows sitting other first.
            using var mounted = V.Mount(new VisualElement(), V.Component(SharedContainerRender, key: "host"));
            s_growing.MountSlotStart = 5;
            s_other.MountSlotStart = 0;

            // Act
            FiberCommitWork.PropagateInlineSlotShift(s_growing, 2);

            // Assert
            Assert.That(s_other.MountSlotStart, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base's sibling chain moves a start after the growth too.
        // It is the control for the case above: a shift that moved nothing would satisfy that one while
        // losing what the propagation is for.
        [Test]
        public void Given_AComponentWhoseRowsStartAfterTheGrowth_When_TheShiftRuns_Then_ItMoves()
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Component(SharedContainerRender, key: "host"));
            s_growing.MountSlotStart = 5;
            s_other.MountSlotStart = 9;

            // Act
            FiberCommitWork.PropagateInlineSlotShift(s_growing, 2);

            // Assert
            Assert.That(s_other.MountSlotStart, Is.EqualTo(11));
        }

        // GREEN_ON_BASE(characterization): the base's sibling chain skips another target's sibling too.
        // A delta measured on one container says nothing about a coordinate into another.
        [Test]
        public void Given_AComponentOnAnotherContainer_When_TheShiftRuns_Then_ItIsLeftAlone()
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Component(SeparateContainersRender, key: "host"));
            s_growing.MountSlotStart = 5;
            s_other.MountSlotStart = 9;

            // Act
            FiberCommitWork.PropagateInlineSlotShift(s_growing, 2);

            // Assert
            Assert.That(s_other.MountSlotStart, Is.EqualTo(9));
        }

        // GREEN_ON_BASE(characterization): the base's sibling chain never reaches the grower itself.
        // What it pins is that a shift reading every component on the container passes over the one that grew.
        [Test]
        public void Given_AnEmptyComponentThatGrows_When_TheShiftRuns_Then_ItsOwnStartStays()
        {
            // Arrange — empty, so the start it shares with the component behind it is a tie the shift decides.
            using var mounted = V.Mount(new VisualElement(), V.Component(EmptyThenOtherRender, key: "host"));

            // Act
            FiberCommitWork.PropagateInlineSlotShift(s_growing, 2);

            // Assert
            Assert.That(s_growing.MountSlotStart, Is.EqualTo(0));
        }

        [Test]
        public void Given_AComponentInsideAHolder_When_TheShiftRuns_Then_TheHolderCountsItsRowsAndTheOneRenderingTheContainerDoesNot()
        {
            // Arrange — the outer component renders the container the other two share, as one row of its own.
            using var mounted = V.Mount(new VisualElement(), V.Component(OuterRender, key: "outer"));

            // Act
            FiberCommitWork.PropagateInlineSlotShift(s_growing, 2);

            // Assert
            Assert.That(
                (s_growing.MountSlotCount, s_holder.MountSlotCount, s_outer.MountSlotCount),
                Is.EqualTo((3, 3, 1)));
        }
    }
}
