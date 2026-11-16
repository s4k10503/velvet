using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a deletion lets go of once it has ended: what its pooled walk (<see cref="DeletionCommit"/>) and pooled
    /// plan (<see cref="DeletionPlan"/>) held in each field a case names, and that a deletion whose teardown waits for
    /// the pass's writes to apply returns both to their pools.
    /// </summary>
    [TestFixture]
    internal sealed class DeletionCommitStateTests
    {
        private static StateUpdater<bool> s_setShown;

        // A component dropped from a container of its own: the deletion holds a fiber, so its teardown waits for the pass's
        // recorded writes to apply.
        [Component(Compiler = false)]
        private static VNode DroppedRow() => V.Label(text: "dropped");

        [Component(Compiler = false)]
        private static VNode DropperHost()
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setShown = setShown;
            return V.Div(children: new VNode[] { shown ? V.Component(DroppedRow, key: "row") : null });
        }

        private sealed record Target(VisualElement Element);

        [Component(Compiler = false)]
        private static VNode PortalKid() => V.Label(text: "kid");

        [Component(Compiler = false)]
        private static VNode Nothing() => V.Fragment(Array.Empty<VNode>());

        // A component rendering nothing last among the Portal's children and last in the element, so the walk holds
        // one where the Portal's rows end and one where the element's children end.
        [Component(Compiler = false)]
        private static VNode PortalHolder(Target target)
            => V.Div(children: new VNode[]
            {
                V.Portal(target.Element, new VNode[]
                {
                    V.Component(PortalKid, key: "kid"),
                    V.Component(Nothing, key: "nothing"),
                }, key: "portal"),
                V.Component(Nothing, key: "after"),
            });

        [Component(Compiler = false)]
        private static VNode PortalHolderHost(Target target)
        {
            var (shown, setShown) = Hooks.UseState(true);
            s_setShown = setShown;
            return V.Fragment(shown ? V.Component(PortalHolder, target, key: "holder") : null);
        }

        // What the walks the pool holds still keep in one of their fields, or in a field of one ("outer.inner").
        private static int EntriesLeftInPooledWalks(string path)
        {
            var entries = 0;
            foreach (var walk in PooledWalks())
            {
                var value = FieldPathValue(walk, path);
                entries += (int)value.GetType().GetProperty("Count")!.GetValue(value);
            }
            return entries;
        }

        // What the lists one of their fields keeps for reuse still hold, over the walks the pool holds.
        private static int EntriesLeftInListsOfPooledWalks(string field)
        {
            var entries = 0;
            foreach (var walk in PooledWalks())
            {
                foreach (ICollection list in (IEnumerable)FieldPathValue(walk, field)) entries += list.Count;
            }
            return entries;
        }

        private static Stack<DeletionPlan> PooledPlans()
            => (Stack<DeletionPlan>)typeof(DeletionPlan)
                .GetField("s_pool", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);

        private static Stack<DeletionCommit> PooledWalks()
            => (Stack<DeletionCommit>)typeof(DeletionCommit)
                .GetField("s_pool", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null);

        private static object FieldPathValue(object target, string path)
        {
            foreach (var name in path.Split('.'))
            {
                FieldInfo info = null;
                for (var type = target.GetType(); info == null && type != null; type = type.BaseType)
                {
                    info = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                }
                target = info!.GetValue(target);
            }
            return target;
        }

        [Test]
        public void Given_ADeletionOfAComponentRunInTheApply_When_TheSecondHasEnded_Then_TheSecondReturnedTheOnePlanItRented()
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Component(DropperHost));
            mounted.DrainForTest();
            s_setShown.Invoke(false);
            mounted.DrainForTest();
            var pooledAfterFirst = PooledPlans().Count;
            s_setShown.Invoke(true);
            mounted.DrainForTest();

            // Act
            s_setShown.Invoke(false);
            mounted.DrainForTest();

            // Assert — a first deletion that returned nothing leaves no pool to compare
            Assert.That(pooledAfterFirst > 0 ? PooledPlans().Count - pooledAfterFirst : -1, Is.EqualTo(0));
        }

        [Test]
        public void Given_ADeletionOfAComponentRunInTheApply_When_TheSecondHasEnded_Then_TheSecondReturnedTheOneWalkItRented()
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Component(DropperHost));
            mounted.DrainForTest();
            s_setShown.Invoke(false);
            mounted.DrainForTest();
            var pooledAfterFirst = PooledWalks().Count;
            s_setShown.Invoke(true);
            mounted.DrainForTest();

            // Act
            s_setShown.Invoke(false);
            mounted.DrainForTest();

            // Assert — a first deletion that returned nothing leaves no pool to compare
            Assert.That(pooledAfterFirst > 0 ? PooledWalks().Count - pooledAfterFirst : -1, Is.EqualTo(0));
        }

        [TestCase("_removedRows")]
        [TestCase("_orphanRows")]
        [TestCase("_portalsUnderRoots")]
        [TestCase("_orphans")]
        [TestCase("_collected")]
        [TestCase("_fibers")]
        [TestCase("_startingAt")]
        [TestCase("_endingIn")]
        [TestCase("_endingInPortal")]
        [TestCase("_placed")]
        [TestCase("_rendered._of")]
        public void Given_ADeletionOfAComponentHoldingAPortal_When_ItHasEnded_Then_NoPooledWalkKeepsWhatItHeld(string field)
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Component(PortalHolderHost, new Target(new VisualElement())));
            mounted.DrainForTest();

            // Act
            s_setShown.Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(EntriesLeftInPooledWalks(field), Is.EqualTo(0));
        }

        [Test]
        public void Given_ADeletionOfAComponentHoldingAPortal_When_ItHasEnded_Then_NoPooledWalkKeepsARowOfIt()
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Component(PortalHolderHost, new Target(new VisualElement())));
            mounted.DrainForTest();

            // Act
            s_setShown.Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(System.Linq.Enumerable.Sum(PooledWalks(), walk => walk.PortalRows.Count), Is.EqualTo(0));
        }

        [Test]
        public void Given_ADeletionRunTwice_When_TheSecondHasEnded_Then_TheSecondReturnedTheOnePlanItRented()
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Component(PortalHolderHost, new Target(new VisualElement())));
            mounted.DrainForTest();
            s_setShown.Invoke(false);
            mounted.DrainForTest();
            var pooledAfterFirst = PooledPlans().Count;
            s_setShown.Invoke(true);
            mounted.DrainForTest();

            // Act
            s_setShown.Invoke(false);
            mounted.DrainForTest();

            // Assert — a first deletion that returned nothing leaves no pool to compare
            Assert.That(pooledAfterFirst > 0 ? PooledPlans().Count - pooledAfterFirst : -1, Is.EqualTo(0));
        }

        [TestCase("_childrenAtDepth")]
        public void Given_ADeletionOfAComponentHoldingAPortal_When_ItHasEnded_Then_NoListAPooledWalkKeepsHoldsAnEntry(string field)
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Component(PortalHolderHost, new Target(new VisualElement())));
            mounted.DrainForTest();

            // Act
            s_setShown.Invoke(false);
            mounted.DrainForTest();

            // Assert
            Assert.That(EntriesLeftInListsOfPooledWalks(field), Is.EqualTo(0));
        }
    }
}
