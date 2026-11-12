using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a commit that deletes components runs, as one <see cref="EventLog"/> string per case, each the
    /// sequence react-reconciler 19.1's <c>ReactFiberCommitWork</c> produces for the same tree: the deletion walk
    /// (<c>commitDeletionEffectsOnFiber</c>) runs a component's insertion and layout destroys and detaches an
    /// element's ref where its pre-order walk reaches them, and the passive destroys run in the passive phase
    /// (<c>commitPassiveUnmountEffectsInsideOfDeletedTree_begin</c>), where that phase's walk enters the deleted
    /// fiber's parent. A case whose name says it reads the passive phase renders the deletion first and logs only
    /// the drain after it: the walk takes deletions where the reconcile reaches them, ahead of the mutation work
    /// React gives the components that come before them.
    /// </summary>
    [TestFixture]
    internal sealed class DeletionCommitOrderEventLogTests
    {
        private sealed record Named(string Name);

        [Component]
        private static VNode Probed(Named props)
        {
            EventLog.UseEffects(props.Name, Array.Empty<object>());
            return V.Div(refCallback: EventLog.UseRefCallback(props.Name));
        }

        [Component]
        private static VNode Rendered(Named props)
        {
            EventLog.UseRender(props.Name);
            EventLog.UseEffects(props.Name, Array.Empty<object>());
            return V.Div(refCallback: EventLog.UseRefCallback(props.Name));
        }

        [Component]
        private static VNode DeletedParent()
        {
            EventLog.UseEffects("P", Array.Empty<object>());
            return V.Div(refCallback: EventLog.UseRefCallback("P"), children: new VNode[]
            {
                V.Component(Probed, new Named("C1"), key: "c1"),
                V.Component(Probed, new Named("C2"), key: "c2"),
            });
        }

        [Component]
        private static VNode ParentHost()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            EventLog.UseRender("Root");
            EventLog.UseEffects("Root", Array.Empty<object>());
            return V.Fragment(shown ? V.Component(DeletedParent, key: "p") : null);
        }

        [Component]
        private static VNode ElementHost()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            EventLog.UseRender("Root");
            var holderRef = EventLog.UseRefCallback("H");
            return V.Fragment(shown
                ? V.Div(refCallback: holderRef, children: new VNode[]
                {
                    V.Component(Probed, new Named("C1"), key: "c1"),
                    V.Component(Probed, new Named("C2"), key: "c2"),
                })
                : null);
        }

        [Component]
        private static VNode KeyChanger()
        {
            var (second, _) = EventLog.UseState("Key", false);
            EventLog.UseRender("Key");
            return V.Fragment(second
                ? V.Component(Rendered, new Named("Cb"), key: "b")
                : V.Component(Rendered, new Named("Ca"), key: "a"));
        }

        [Component]
        private static VNode SwappedOut()
        {
            EventLog.UseEffects("A", Array.Empty<object>());
            return V.Div(refCallback: EventLog.UseRefCallback("A"));
        }

        [Component]
        private static VNode SwappedIn()
        {
            EventLog.UseRender("B");
            EventLog.UseEffects("B", Array.Empty<object>());
            return V.Div(refCallback: EventLog.UseRefCallback("B"));
        }

        [Component]
        private static VNode TypeSwapper()
        {
            var (swapped, _) = EventLog.UseState("Type", false);
            EventLog.UseRender("Type");
            return V.Fragment(swapped ? V.Component(SwappedIn) : V.Component(SwappedOut));
        }

        private sealed record Dep(string Name, int Value);

        [Component]
        private static VNode Dependent(Dep props)
        {
            EventLog.UseRender(props.Name);
            EventLog.UseEffects(props.Name, new object[] { props.Value });
            return V.Div();
        }

        // The deleted child shares its element with a sibling the same render updates.
        [Component]
        private static VNode SiblingsInOneElement()
        {
            var (dep, _) = EventLog.UseState("P", 0);
            EventLog.UseRender("P");
            EventLog.UseEffects("P", new object[] { dep });
            return V.Div(refCallback: EventLog.UseRefCallback("P"), children: new VNode[]
            {
                V.Component(Dependent, new Dep("S", dep), key: "s"),
                dep == 0 ? V.Component(Probed, new Named("D"), key: "d") : null,
            });
        }

        // The deleted child sits in an element that follows a sibling the same render updates.
        [Component]
        private static VNode SiblingBeforeTheElement()
        {
            var (dep, _) = EventLog.UseState("P", 0);
            EventLog.UseEffects("P", new object[] { dep });
            return V.Fragment(
                V.Component(Dependent, new Dep("S", dep), key: "s"),
                V.Div(children: new VNode[] { dep == 0 ? V.Component(Probed, new Named("D"), key: "d") : null }));
        }

        [Component]
        private static VNode Stateful()
        {
            var (value, _) = EventLog.UseState("S", 0);
            EventLog.UseEffects("S", new object[] { value });
            return V.Div();
        }

        [Component]
        private static VNode Dropper()
        {
            var (shown, _) = EventLog.UseState("M", true);
            return V.Fragment(shown ? V.Component(Probed, new Named("D"), key: "d") : null);
        }

        // The deleted child's parent is a component that follows a sibling updated in the same commit.
        [Component]
        private static VNode SiblingBeforeTheParent()
            => V.Div(children: new VNode[] { V.Component(Stateful, key: "s"), V.Component(Dropper, key: "m") });

        // Two deletions in one render: one inside an element, and one in the component's own rows after it.
        [Component]
        private static VNode TwoDeletions()
        {
            var (shown, _) = EventLog.UseState("Shown", true);
            return V.Fragment(
                V.Div(children: new VNode[] { shown ? V.Component(Probed, new Named("A"), key: "a") : null }),
                shown ? V.Component(Probed, new Named("B"), key: "b") : null);
        }

        private static MountedTree Mount(EventLog log, VNode root)
        {
            var mounted = V.Mount(new VisualElement(), log.Provide(root));
            mounted.DrainForTest();
            log.Clear();
            return mounted;
        }

        [Test]
        public void Given_AComponentWithNestedComponentsAndRefs_When_ItsParentStopsRenderingIt_Then_EachIsTakenDownInPreOrderAndItsPassiveCleanupsRunAfterTheCommit()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(ParentHost));

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "render:Root ins-x:P lay-x:P ref-x:P ins-x:C1 lay-x:C1 ref-x:C1 ins-x:C2 lay-x:C2 ref-x:C2"
                + " eff-x:P eff-x:C1 eff-x:C2"));
        }

        [Test]
        public void Given_AnElementHoldingComponentsWithRefs_When_ItsParentStopsRenderingIt_Then_ItsRefDetachesAheadOfTheComponentsInside()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(ElementHost));

            // Act
            log.Setter<bool>("Shown").Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "render:Root ref-x:H ins-x:C1 lay-x:C1 ref-x:C1 ins-x:C2 lay-x:C2 ref-x:C2 eff-x:C1 eff-x:C2"));
        }

        [Test]
        public void Given_AKeyedChild_When_ItsKeyChanges_Then_TheOldChildsPassiveCleanupRunsAfterTheNewChildsLayoutSetup()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(KeyChanger));

            // Act
            log.Setter<bool>("Key").Invoke(true);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "render:Key render:Cb ins-x:Ca lay-x:Ca ref-x:Ca ins:Cb ref:Cb lay:Cb eff-x:Ca eff:Cb"));
        }

        [Test]
        public void Given_AChild_When_ItsTypeChanges_Then_TheOldChildsPassiveCleanupRunsAfterTheNewChildsLayoutSetup()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(TypeSwapper));

            // Act
            log.Setter<bool>("Type").Invoke(true);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "render:Type render:B ins-x:A lay-x:A ref-x:A ins:B ref:B lay:B eff-x:A eff:B"));
        }

        [Test]
        public void Given_AChildDeletedFromAnElementBesideASiblingThatUpdates_When_TheCommitRuns_Then_TheDeletionsPassiveCleanupRunsAheadOfTheSiblings()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(SiblingsInOneElement));

            // Act
            log.Setter<int>("P").Invoke(1);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "render:P render:S ins-x:D lay-x:D ref-x:D ins-x:S ins:S lay-x:S ins-x:P ins:P lay-x:P lay:S lay:P"
                + " eff-x:D eff-x:S eff-x:P eff:S eff:P"));
        }

        [Test]
        public void Given_AChildDeletedFromAnElementAfterASiblingThatUpdates_When_ThePassivePhaseRuns_Then_TheSiblingsCleanupRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(SiblingBeforeTheElement));
            log.Setter<int>("P").Invoke(1);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Clear();

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("eff-x:S eff-x:D eff-x:P eff:S eff:P"));
        }

        [Test]
        public void Given_AChildDeletedByAComponentAfterASiblingThatUpdates_When_ThePassivePhaseRuns_Then_TheSiblingsCleanupRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(SiblingBeforeTheParent));
            log.Setter<int>("S").Invoke(1);
            log.Setter<bool>("M").Invoke(false);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Clear();

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("eff-x:S eff-x:D eff:S"));
        }

        [Test]
        public void Given_ADeletionInsideAnElementAndOneAfterIt_When_ThePassivePhaseRuns_Then_TheOneTheParentHoldsDirectlyRunsFirst()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = Mount(log, V.Component(TwoDeletions));
            log.Setter<bool>("Shown").Invoke(false);
            mounted.GetSchedulerForTest().DrainImmediateForTest();
            log.Clear();

            // Act
            mounted.FlushEffectsForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("eff-x:B eff-x:A"));
        }
    }
}
