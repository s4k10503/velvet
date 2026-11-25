using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that a deletion's plan, built over an element holding components with refs and layout effects, takes
    /// nothing down until it is executed, and that executing it takes down what the deletion's own run does
    /// (<see cref="DeletionCommitOrderEventLogTests"/> pins that order through a render).
    /// </summary>
    [TestFixture]
    internal sealed class DeletionPlanTests
    {
        private sealed record Named(string Name);

        [Component(Compiler = false)]
        private static VNode Probed(Named props)
        {
            EventLog.UseInsertionAndLayoutEffects(props.Name, Array.Empty<object>());
            return V.Div(refCallback: EventLog.UseRefCallback<VisualElement>(props.Name));
        }

        [Component(Compiler = false)]
        private static VNode ElementHoldingComponents()
        {
            var holderRef = EventLog.UseRefCallback<VisualElement>("H");
            return V.Div(refCallback: holderRef, children: new VNode[]
            {
                V.Component(Probed, new Named("C1"), key: "c1"),
                V.Component(Probed, new Named("C2"), key: "c2"),
            });
        }

        private const string TakenDownInPreOrder = "ref-x:H ins-x:C1 lay-x:C1 ref-x:C1 ins-x:C2 lay-x:C2 ref-x:C2";

        // The deletion of the row the tree mounted, begun the way a container's reconcile begins it.
        private static DeletionCommit BeginDeletionOfTheRow(
            MountedTree mounted, VisualElement container, out VisualElement row)
        {
            var context = mounted.Root.Reconciler!.Context;
            row = container[0];
            var deletion = DeletionCommit.Begin(context, new FiberElementCleaner(context), container, slotStart: 0, occupants: null);
            deletion.AddRemovedRow(0, row, owner: null);
            return deletion;
        }

        [Test]
        public void Given_APlanBuiltOverAnElementHoldingComponents_When_ItHasNotBeenExecuted_Then_NothingHasBeenTakenDown()
        {
            // Arrange
            var log = new EventLog();
            var container = new VisualElement();
            using var mounted = V.Mount(container, log.Provide(V.Component(ElementHoldingComponents)));
            mounted.DrainForTest();
            log.Clear();
            var deletion = BeginDeletionOfTheRow(mounted, container, out _);

            // Act
            try
            {
                deletion.BuildPlan(new List<ComponentFiber>(), new HashSet<ComponentFiber>(), oldLength: 1);
            }
            finally
            {
                deletion.End();
            }

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(string.Empty));
        }

        [Test]
        public void Given_APlanBuiltAndNeverExecuted_When_TheDeletionEnds_Then_ThePlanHoldsNoStep()
        {
            // Arrange
            var container = new VisualElement();
            using var mounted = V.Mount(container, new EventLog().Provide(V.Component(ElementHoldingComponents)));
            mounted.DrainForTest();
            var deletion = BeginDeletionOfTheRow(mounted, container, out _);
            var plan = deletion.BuildPlan(new List<ComponentFiber>(), new HashSet<ComponentFiber>(), oldLength: 1);
            var stepsBuilt = plan.Steps.Count;

            // Act
            deletion.End();

            // Assert — a plan that recorded nothing leaves nothing to release
            Assert.That(stepsBuilt > 0 ? plan.Steps.Count : -1, Is.EqualTo(0));
        }

        [Test]
        public void Given_APlanBuiltAndNeverRemoved_When_TheDeletionEnds_Then_ThePlanHoldsNoRoot()
        {
            // Arrange
            var container = new VisualElement();
            using var mounted = V.Mount(container, new EventLog().Provide(V.Component(ElementHoldingComponents)));
            mounted.DrainForTest();
            var deletion = BeginDeletionOfTheRow(mounted, container, out _);
            var plan = deletion.BuildPlan(new List<ComponentFiber>(), new HashSet<ComponentFiber>(), oldLength: 1);
            var rootsBuilt = plan.Roots.Count;

            // Act
            deletion.End();

            // Assert — a plan that recorded nothing leaves nothing to release
            Assert.That(rootsBuilt > 0 ? plan.Roots.Count : -1, Is.EqualTo(0));
        }

        [Test]
        public void Given_APlanBuiltOverAnElementHoldingComponents_When_ItIsExecuted_Then_EachComponentAndRefIsTakenDownInPreOrder()
        {
            // Arrange
            var log = new EventLog();
            var container = new VisualElement();
            using var mounted = V.Mount(container, log.Provide(V.Component(ElementHoldingComponents)));
            mounted.DrainForTest();
            log.Clear();
            var deletion = BeginDeletionOfTheRow(mounted, container, out var row);

            // Act
            try
            {
                var plan = deletion.BuildPlan(new List<ComponentFiber>(), new HashSet<ComponentFiber>(), oldLength: 1);
                deletion.Execute();
                mounted.Root.Reconciler!.Context.HostCommit.Remove(container, row, plan);
            }
            finally
            {
                deletion.End();
            }

            // Assert
            Assert.That(log.ToString(), Is.EqualTo(TakenDownInPreOrder));
        }
    }
}
