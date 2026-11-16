using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins where in the commit a deletion's take-downs run: at the turn of the fiber that kept the parent of what was
    /// deleted, as react-reconciler 19.1's <c>commitMutationEffectsOnFiber</c> runs a fiber's deletions ahead of its
    /// children, and not where the container that holds both is done reconciling. The order the cases expect is that
    /// function's for the same trees, read from its source rather than run.
    /// </summary>
    [TestFixtureSource(typeof(EventLogRenderer), nameof(EventLogRenderer.All))]
    internal sealed class DeletionAtOwnerTurnTests
    {
        private readonly EventLogRenderer _renderer;

        public DeletionAtOwnerTurnTests(EventLogRenderer renderer) => _renderer = renderer;

        private sealed record Named(string Name);

        [Component]
        private static VNode Probed(Named props)
        {
            EventLog.UseInsertionAndLayoutEffects(props.Name, Array.Empty<object>());
            return V.Div();
        }

        // Deletes a component of its own output, whose rows follow a label of its own.
        [Component]
        private static VNode DropsItsLeaf(bool shown)
            => V.Fragment(V.Label(text: "b"), shown ? V.Component(Probed, new Named("E"), key: "e") : null);

        // Holds a component that an element of its own deletes.
        [Component]
        private static VNode HoldsAnElementDroppingOne(bool shown)
            => V.Div(children: new VNode[] { shown ? V.Component(Probed, new Named("D"), key: "d") : null });

        [Component]
        private static VNode OwnerThenElement()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                V.Component(DropsItsLeaf, shown, key: "b"),
                V.Div(key: "x", children: new VNode[] { shown ? V.Component(Probed, new Named("D"), key: "d") : null }));
        }

        [Component]
        private static VNode ElementThenOwner()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                V.Component(HoldsAnElementDroppingOne, shown, key: "u"),
                V.Component(DropsItsLeaf, shown, key: "b"));
        }

        // Deletes a component of its own output and one of the container's, which sits behind the owner's rows.
        [Component]
        private static VNode OwnerThenAComponentOfTheContainer()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                V.Component(DropsItsLeaf, shown, key: "b"),
                shown ? V.Component(Probed, new Named("F"), key: "f") : null);
        }

        [Test]
        public void Given_AnOwnerDeletingItsLeafAheadOfAComponentTheContainerDeletes_When_BothAreDeleted_Then_TheContainersDeletionRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(OwnerThenAComponentOfTheContainer)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:F lay-x:F ins-x:E lay-x:E"));
        }

        [Test]
        public void Given_AnOwnerDeletingItsLeafAheadOfAnElementThatDeletesAComponent_When_BothAreDeleted_Then_TheOwnersDeletionRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(OwnerThenElement)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:E lay-x:E ins-x:D lay-x:D"));
        }

        // GREEN_ON_BASE(characterization): the base queues the deletion of the nested container ahead of the outer one, which is
        // React's order for a nested container that comes first. A deletion hoisted to the start of its container instead of
        // to its owner's turn fails this case.
        [Test]
        public void Given_AnElementDeletingAComponentAheadOfAnOwnerDeletingItsLeaf_When_BothAreDeleted_Then_TheElementsDeletionRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(ElementThenOwner)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:D lay-x:D ins-x:E lay-x:E"));
        }

        [TestCase("_owner")]
        [TestCase("_oldFibers")]
        [TestCase("NextGroup")]
        public void Given_AContainerDeletingAtTwoOwnersTurns_When_BothHaveEnded_Then_NoPooledWalkKeepsWhatItNamed(string field)
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(OwnerThenAComponentOfTheContainer)));
            mounted.DrainForTest();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert — a walk with no such field reads as a count the expected one is not
            var info = typeof(DeletionCommit).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(info == null ? -1 : PooledWalksHolding(info), Is.EqualTo(0));
        }

        private static int PooledWalksHolding(FieldInfo field)
        {
            var pool = (Stack<DeletionCommit>)typeof(DeletionCommit)
                .GetField("s_pool", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);
            var holding = 0;
            foreach (var walk in pool)
            {
                if (field.GetValue(walk) != null) holding++;
            }
            return holding;
        }
    }
}
