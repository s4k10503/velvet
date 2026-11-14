using System;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins when a commit detaches and attaches host refs, as one <see cref="EventLog"/> string per case, each the
    /// sequence react-reconciler 19.1 produces for the same tree: its mutation phase detaches an element's old ref
    /// (<c>safelyDetachRef</c>) where the walk reaches the element, and its layout phase attaches the new one
    /// (<c>safelyAttachRef</c>) at the element's place among the layout effects.
    /// </summary>
    [TestFixtureSource(typeof(EventLogRenderer), nameof(EventLogRenderer.All))]
    internal sealed class RefCommitOrderEventLogTests
    {
        private readonly EventLogRenderer _renderer;

        public RefCommitOrderEventLogTests(EventLogRenderer renderer) => _renderer = renderer;

        [Component]
        private static VNode Restyled()
        {
            var (value, _) = EventLog.UseState("Value", 0);
            EventLog.UseRender("H");
            return V.Div(children: new VNode[]
            {
                V.Label(text: "text" + value, className: value == 0 ? "p-1" : "p-2", name: "label" + value,
                    refCallback: EventLog.UseRefCallback<VisualElement>("H")),
            });
        }

        [Component]
        private static VNode LaysOut(bool dep)
        {
            var log = EventLog.UseLog();
            Hooks.UseLayoutEffect(() =>
            {
                log.Add("lay:S");
                return (Action?)null;
            }, new object[] { dep });
            return V.Div();
        }

        [Component]
        private static VNode SwapsARef()
        {
            var (swapped, _) = EventLog.UseState("Swapped", false);
            EventLog.UseRender("P");
            EventLog.UseEffects("P", new object[] { swapped });
            var refA = EventLog.UseRefCallback<VisualElement>("A");
            var refB = EventLog.UseRefCallback<VisualElement>("B");
            return V.Div(children: new VNode[]
            {
                V.Component(LaysOut, swapped, key: "s"),
                V.Div(key: "d", refCallback: swapped ? refB : refA),
            });
        }

        [Component]
        private static VNode ChangesTheRefdType()
        {
            var (label, _) = EventLog.UseState("Label", false);
            EventLog.UseRender("P");
            EventLog.UseEffects("P", new object[] { label });
            var refR = EventLog.UseRefCallback<VisualElement>("R");
            return V.Div(children: new VNode[] { label ? V.Label(text: "r", refCallback: refR) : V.Div(refCallback: refR) });
        }

        // GREEN_ON_BASE(characterization): the base patches the label in place, so its stable ref never cycles.
        [Test]
        public void Given_AnElementWithAStableRefCallback_When_ItsTextClassesAndNameChange_Then_NoRefDetachOrAttachIsLogged()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(Restyled)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<int>("Value").Invoke(1);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo("render:H"));
        }

        // GREEN_ON_BASE(characterization): the base detaches the old ref first and attaches the new one in post-order.
        [Test]
        public void Given_AnElementAfterASiblingComponent_When_ItsRefCallbackIsSwapped_Then_TheOldDetachesWithTheMutationsAndTheNewAttachesAtItsPostOrderSlot()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(SwapsARef)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Swapped").Invoke(true);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "render:P ref-x:A ins-x:P ins:P lay-x:P lay:S ref:B lay:P eff-x:P eff:P"));
        }

        // GREEN_ON_BASE(characterization): the base detaches the replaced element's ref before the new one attaches.
        [Test]
        public void Given_ARefdElement_When_ItsTypeChanges_Then_TheOldInstanceDetachesBeforeTheNewOneAttaches()
        {
            // Arrange
            var log = new EventLog();
            using var mounted = _renderer.Mount(log.Provide(V.Component(ChangesTheRefdType)));
            mounted.DrainForTest();
            log.Clear();

            // Act
            log.Setter<bool>("Label").Invoke(true);
            mounted.DrainForTest();

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(
                "render:P ref-x:R ins-x:P ins:P lay-x:P ref:R lay:P eff-x:P eff:P"));
        }

        private sealed class Slicing
        {
            internal ComponentFiber? Fiber;
            internal int Version;
        }

        [Component]
        private static VNode SlicedRow(int version) => V.Fragment(Array.Empty<VNode>());

        // Built as PortalCommitOrderEventLogTests.SlicedRows is, and for its reasons.
        [Component(Compiler = false)]
        private static VNode SwapsRowRefs(Slicing slicing)
        {
            slicing.Fiber = FiberAmbientStack.Current;
            EventLog.UseRender("Rows");
            var version = slicing.Version;
            var oldRefs = new[]
            {
                EventLog.UseRefCallback<VisualElement>("A0"), EventLog.UseRefCallback<VisualElement>("A1"),
                EventLog.UseRefCallback<VisualElement>("A2"), EventLog.UseRefCallback<VisualElement>("A3"),
            };
            var newRefs = new[]
            {
                EventLog.UseRefCallback<VisualElement>("B0"), EventLog.UseRefCallback<VisualElement>("B1"),
                EventLog.UseRefCallback<VisualElement>("B2"), EventLog.UseRefCallback<VisualElement>("B3"),
            };
            var rows = new VNode[oldRefs.Length];
            for (var i = 0; i < rows.Length; i++)
            {
                rows[i] = V.Div(key: "row" + i, refCallback: version == 0 ? oldRefs[i] : newRefs[i],
                    children: new VNode?[] { V.Component(SlicedRow, version, key: "row") });
            }
            return V.Fragment(rows);
        }

        [TestFixture]
        internal sealed class UIToolkitOnly
        {
            // GREEN_ON_BASE(characterization): the base holds a parked pass's ref cleanups and setups until it completes.
            [Test]
            public void Given_ATimeSlicedTransitionSwappingRowRefs_When_ItsFirstSliceParks_Then_NoRefDetachesOrAttachesUntilItsLastSliceCompletes()
            {
                // Arrange
                var log = new EventLog();
                var slicing = new Slicing();
                using var mounted = V.Mount(new VisualElement(), log.Provide(V.Component(SwapsRowRefs, slicing)));
                mounted.DrainForTest();
                log.Clear();
                slicing.Version = 1;

                // Act
                slicing.Fiber!.ScheduleRerenderForTest(FiberUpdatePriority.Transition);
                slicing.Fiber.FlushStateWithTinyBudgetForTest();
                log.Add(slicing.Fiber.HasPendingReconcileWorkForTest() ? "parked" : "never-parked");
                slicing.Fiber.DrainTimeSlicedReconcileForTest();
                mounted.DrainForTest();

                // Assert — a pass that never parked commits in its first slice, so that reading is a sentinel
                Assert.That(log.ToString(), Is.EqualTo(
                    "render:Rows parked ref-x:A0 ref-x:A1 ref-x:A2 ref-x:A3 ref:B0 ref:B1 ref:B2 ref:B3"));
            }
        }
    }
}
