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

        private static IReadOnlyList<object> PooledWalks() => DeletionCommitPool.Walks();

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

        // GREEN_ON_BASE(characterization): the base returns and clears its pooled walks and plans the same way; only the helper that reads the pool moved.
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

        // GREEN_ON_BASE(characterization): the base returns and clears its pooled walks and plans the same way; only the helper that reads the pool moved.
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

        // GREEN_ON_BASE(characterization): the base returns and clears its pooled walks and plans the same way; only the helper that reads the pool moved.
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

        // GREEN_ON_BASE(characterization): the base returns and clears its pooled walks and plans the same way; only the helper that reads the pool moved.
        [Test]
        public void Given_ADeletionOfAComponentHoldingAPortal_When_ItHasEnded_Then_NoPooledWalkKeepsARowOfIt()
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Component(PortalHolderHost, new Target(new VisualElement())));
            mounted.DrainForTest();

            // Act
            s_setShown.Invoke(false);
            mounted.DrainForTest();

            // Assert — a walk with no such field reads as a count the expected one is not
            var rows = typeof(DeletionCommit).GetField("_portalRows", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(rows == null ? -1 : EntriesLeftInPooledWalks("_portalRows"), Is.EqualTo(0));
        }

        // Keys rows by identity, counting each row it hashes.
        private sealed class CountedOwnerKeys : IEqualityComparer<VisualElement>
        {
            public int Hashes;

            public bool Equals(VisualElement x, VisualElement y) => ReferenceEquals(x, y);

            public int GetHashCode(VisualElement obj)
            {
                Hashes++;
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }

        // Has every pooled walk key the table it finds a detached Portal's owner in through keys, and returns what puts
        // back the tables they had. A walk with no such table is left alone.
        private static Action KeyPooledOwnerTablesThrough(IEqualityComparer<VisualElement> keys)
        {
            var field = typeof(DeletionCommit).GetField("_rowOwners", BindingFlags.NonPublic | BindingFlags.Instance);
            Action restore = () => { };
            if (field == null) return restore;
            foreach (var walk in PooledWalks())
            {
                var had = field.GetValue(walk);
                field.SetValue(walk, Activator.CreateInstance(field.FieldType, keys));
                restore += () => field.SetValue(walk, had);
            }
            return restore;
        }

        [Test]
        public void Given_ADeletionDetachingAPortalWhereThePassesWritesApply_When_ItHasEnded_Then_NoPooledWalkKeepsAnOwnerOfARowItDetached()
        {
            // Arrange — a first deletion leaves its walks in the pool, which then key their owner tables through keys that
            // count, so the deletion below fills one of them only by detaching the Portal
            using var mounted = V.Mount(new VisualElement(), V.Component(PortalHolderHost, new Target(new VisualElement())));
            mounted.DrainForTest();
            s_setShown.Invoke(false);
            mounted.DrainForTest();
            s_setShown.Invoke(true);
            mounted.DrainForTest();
            var keys = new CountedOwnerKeys();
            var restore = KeyPooledOwnerTablesThrough(keys);
            int left;
            try
            {
                // Act
                s_setShown.Invoke(false);
                mounted.DrainForTest();
                left = keys.Hashes > 0 ? EntriesLeftInPooledWalks("_rowOwners") : -1;
            }
            finally
            {
                restore();
            }

            // Assert — a deletion that keyed no row into a table never filled one, which reads as a count the expected one is not
            Assert.That(left, Is.EqualTo(0));
        }

        [TestCase("_rootsInOrder")]
        [TestCase("_portalRowsRead")]
        public void Given_ADeletionOfAComponentHoldingAPortal_When_ItHasEnded_Then_NoPooledWalkKeepsWhatItsWalkOverTheRootsHeld(string field)
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Component(PortalHolderHost, new Target(new VisualElement())));
            mounted.DrainForTest();

            // Act
            s_setShown.Invoke(false);
            mounted.DrainForTest();

            // Assert — a walk with no such field reads as a count the expected one is not
            var held = typeof(DeletionCommit).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(held == null ? -1 : EntriesLeftInPooledWalks(field), Is.EqualTo(0));
        }

        // A round of the walk empties what it met before it ends, so the list holds a Portal past a round only where the
        // round threw. That is stood in for by writing one into it.
        [Test]
        public void Given_AWalkLeftHoldingAPortalItsRoundMet_When_ItEnds_Then_ItKeepsNone()
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Div());
            var ctx = mounted.Root.Reconciler.Context;
            var walk = DeletionCommit.Begin(ctx, new FiberElementCleaner(ctx), container: null, slotStart: 0, occupants: null);
            var met = typeof(DeletionCommit).GetField("_metPortals", BindingFlags.NonPublic | BindingFlags.Instance)
                ?.GetValue(walk) as IList;
            met?.Add(new VisualElement());

            // Act
            walk.End();

            // Assert — a walk with no such list reads as a count the expected one is not
            Assert.That(met == null ? -1 : met.Count, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base returns and clears its pooled walks and plans the same way; only the helper that reads the pool moved.
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

        // GREEN_ON_BASE(characterization): the base returns and clears its pooled walks and plans the same way; only the helper that reads the pool moved.
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
