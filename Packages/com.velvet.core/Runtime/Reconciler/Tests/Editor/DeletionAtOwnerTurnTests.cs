using System;
using NUnit.Framework;
using UnityEngine.UIElements;
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

        private static readonly ComponentContext<int> s_context = ComponentContext<int>.Create(0);

        // Deletes a component of its own output, ahead of whatever follows it in the container.
        [Component]
        private static VNode DropsX(bool shown)
            => V.Fragment(shown ? V.Component(Probed, new Named("X"), key: "x") : null);

        [Component]
        private static VNode XThenAProvider()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                V.Component(DropsX, shown, key: "l"),
                V.Provider(s_context, 1, new VNode[] { shown ? V.Component(Probed, new Named("E"), key: "e") : null }));
        }

        [Component]
        private static VNode XThenASuspense()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                V.Component(DropsX, shown, key: "l"),
                V.Suspense(fallback: V.Div(name: "loading"), children: new VNode[]
                {
                    shown ? V.Component(Probed, new Named("E"), key: "e") : null,
                }));
        }

        // GREEN_ON_BASE(characterization): the base takes one plan over the container in tree order, which is React's: the
        // Provider is a fiber of its own there, and its deletion runs at its turn, after the component's before it.
        [Test]
        public void Given_AComponentDeletingBesideAProviderDeletingAComponent_When_BothAreDeleted_Then_TheComponentsDeletionRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(XThenAProvider)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:X lay-x:X ins-x:E lay-x:E"));
        }

        // GREEN_ON_BASE(characterization): the base's one plan takes the container in tree order, as React reaches the
        // Suspense fiber after the component before it.
        [Test]
        public void Given_AComponentDeletingBesideASuspenseDeletingAComponent_When_BothAreDeleted_Then_TheComponentsDeletionRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(XThenASuspense)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:X lay-x:X ins-x:E lay-x:E"));
        }

        // Deletes two components of its own output.
        [Component]
        private static VNode DropsTwo(bool shown)
            => V.Fragment(
                V.Label(text: "b"),
                shown ? V.Component(Probed, new Named("E1"), key: "e1") : null,
                shown ? V.Component(Probed, new Named("E2"), key: "e2") : null);

        [Component]
        private static VNode TwoDroppedByOneOwner()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(V.Component(DropsTwo, shown, key: "b"));
        }

        // GREEN_ON_BASE(characterization): the base's one plan takes the owner's rows in order.
        [Test]
        public void Given_AnOwnerDeletingTwoComponents_When_BothAreDeleted_Then_TheyAreTakenDownInTheirOrder()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(TwoDroppedByOneOwner)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:E1 lay-x:E1 ins-x:E2 lay-x:E2"));
        }

        [Component(IsErrorBoundary = true, DisplayName = "Guard")]
        private static VNode GuardDeletingItsLeaf(bool shown)
        {
            Hooks.UseFallback(_ => V.Div(name: "fallback"));
            return V.Fragment(V.Label(text: "g"), shown ? V.Component(Probed, new Named("T"), key: "t") : null);
        }

        [Component]
        private static VNode BoundaryThenElement()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                V.Component(GuardDeletingItsLeaf, shown, key: "g"),
                V.Div(key: "x", children: new VNode[] { shown ? V.Component(Probed, new Named("D"), key: "d") : null }));
        }

        [Test]
        public void Given_AnErrorBoundaryDeletingItsLeafAheadOfAnElementDeletingAComponent_When_BothAreDeleted_Then_TheBoundarysDeletionRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(BoundaryThenElement)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:T lay-x:T ins-x:D lay-x:D"));
        }

        // Its second row is a delayed field, which the walk patches at once, applying the writes recorded before it.
        [Component]
        private static VNode DropsBesideADelayedField(bool shown)
            => V.Fragment(
                V.Label(text: shown ? "b" : "b2"),
                V.TextField(value: shown ? "1" : "2", isDelayed: true),
                shown ? V.Component(Probed, new Named("E"), key: "e") : null);

        [Component]
        private static VNode DelayedFieldBeforeTheDeletions()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                V.Component(DropsBesideADelayedField, shown, key: "b"),
                shown ? V.Component(Probed, new Named("F"), key: "f") : null);
        }

        [Test]
        public void Given_APatchAppliedAtOnceInTheWalkBeforeTwoDeletions_When_BothAreDeleted_Then_TheContainersDeletionRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(DelayedFieldBeforeTheDeletions)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:F lay-x:F ins-x:E lay-x:E"));
        }

        [Component]
        private static VNode ListRow(Named props)
        {
            EventLog.UseInsertionAndLayoutEffects(props.Name, Array.Empty<object>());
            return V.Div();
        }

        // Holds a list whose row is a component that mounts its own wrapper element, which a deletion takes down where it is
        // decided rather than where the pass applies its writes.
        [Component]
        private static VNode HoldsAList(bool shown)
            => V.Fragment(shown
                ? V.Div(children: new VNode[]
                {
                    V.VirtualList(
                        items: new[] { "R" },
                        keySelector: item => item,
                        itemHeight: 50f,
                        renderer: item => V.Component(ListRow, new Named(item), key: item),
                        overscan: 0),
                })
                : null);

        [Component]
        private static VNode AComponentThenAList()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                shown ? V.Component(Probed, new Named("A"), key: "a") : null,
                V.Component(HoldsAList, shown, key: "b"));
        }

        // GREEN_ON_BASE(characterization): the base decides whether to defer once for the container, and a list's row keeps
        // the whole deletion in the walk, in tree order.
        [Test]
        public void Given_AComponentDeletedAheadOfAListWhoseRowCannotWait_When_BothAreDeleted_Then_TheComponentsDeletionRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            var root = new VisualElement();
            using var mounted = V.Mount(root, log.Provide(V.Component(AComponentThenAList)));
            mounted.DrainForTest();
            mounted.Root.Reconciler!.Context.VirtualListControllers[root.Q<ScrollView>()]
                .UpdateVisibleRange(scrollY: 0f, viewportHeight: 50f);
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("ins-x:A lay-x:A ins-x:R lay-x:R"));
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

        [TestCase("_turn.Fiber")]
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
            Assert.That(DeletionCommitPool.WalksHolding(field), Is.EqualTo(0));
        }
    }
}
