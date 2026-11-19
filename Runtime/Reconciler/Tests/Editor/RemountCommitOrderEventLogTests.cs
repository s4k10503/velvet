using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a commit does when a slot's child loses its identity, by a new key or a new type, as one
    /// <see cref="EventLog"/> string per case, each what react-reconciler 19.1 commits for the same tree: the
    /// parent's mutation walk takes the old child down first (<c>recursivelyTraverseMutationEffects</c> runs a
    /// parent's deletions ahead of its children), then runs the new one's insertion setup, and the layout phase
    /// attaches its ref and sets its layout effect up; the new instance starts from its initial state. The probes
    /// register no passive effect, for the reason <see cref="DeletionCommitOrderEventLogTests"/> gives.
    /// </summary>
    [TestFixtureSource(typeof(EventLogRenderer), nameof(EventLogRenderer.All))]
    internal sealed class RemountCommitOrderEventLogTests
    {
        private readonly EventLogRenderer _renderer;

        public RemountCommitOrderEventLogTests(EventLogRenderer renderer) => _renderer = renderer;

        private sealed record Named(string Name);

        private static VNode UseCountingProbe(string name)
        {
            var (count, _) = EventLog.UseState("Count", 0);
            EventLog.UseRender(name, count);
            EventLog.UseInsertionAndLayoutEffects(name, Array.Empty<object>());
            return V.Div(refCallback: EventLog.UseRefCallback<VisualElement>(name));
        }

        [Component]
        private static VNode Counting(Named props) => UseCountingProbe(props.Name);

        [Component]
        private static VNode First() => UseCountingProbe("A");

        [Component]
        private static VNode Second() => UseCountingProbe("B");

        [Component]
        private static VNode Rekeyed()
        {
            var (key, _) = EventLog.UseState("Key", "a");
            EventLog.UseRender("P");
            return V.Div(children: new VNode[] { V.Component(Counting, new Named(key), key: key) });
        }

        [Component]
        private static VNode Retyped()
        {
            var (second, _) = EventLog.UseState("Second", false);
            EventLog.UseRender("P");
            return V.Div(children: new VNode[] { second ? V.Component(Second) : V.Component(First) });
        }

        private MountedEventLogTree MountCounted(EventLog log, VNode root)
        {
            var mounted = _renderer.Mount(log.Provide(root));
            mounted.DrainForTest();
            log.Setter<int>("Count").Invoke(1);
            mounted.DrainForTest();
            log.Clear();
            return mounted;
        }

        // GREEN_ON_BASE(characterization): the base mounts a child whose key changed afresh, after the old one's teardown.
        // Have FiberKeying.ResolveInlineRegistryPositionKey take every component as unkeyed and the log reads
        // `render:P render:b=1`.
        [Test]
        public void Given_AStatefulChildWithARef_When_ItsKeyChanges_Then_TheOldIsTakenDownBeforeTheNewSetsUpFromItsInitialState()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = MountCounted(log, V.Component(Rekeyed));

            // Act
            log.Setter<string>("Key").Invoke("b");
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:P render:b=0 ins-x:a lay-x:a ref-x:a ins:b ref:b lay:b"));
        }

        // GREEN_ON_BASE(characterization): the base mounts a component of another type afresh in the slot it takes over.
        // Have ComponentRegistry.GetOrCreateCore give First and Second one identity and the log reads
        // `render:P render:B=1 ref:A`.
        [Test]
        public void Given_ASlotHoldingAStatefulComponent_When_ItRendersAnotherComponentThere_Then_TheOldIsTakenDownBeforeTheNewSetsUpFromItsInitialState()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = MountCounted(log, V.Component(Retyped));

            // Act
            log.Setter<bool>("Second").Invoke(true);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:P render:B=0 ins-x:A lay-x:A ref-x:A ins:B ref:B lay:B"));
        }
    }
}
