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
    /// Pins what a deletion lets go of once it has ended: a row it registered for its removal
    /// (<c>ReconcilerContext.RemovalsTakenDown</c>) where an abort kept the removal from running, and what its pooled
    /// walk (<see cref="DeletionCommit"/>) held in each field a case names.
    /// </summary>
    [TestFixture]
    internal sealed class DeletionCommitStateTests
    {
        private const string CleanupError = "Deletion commit state test cleanup throw";

        private static StateUpdater<bool> s_setShown;
        private static StateUpdater<int> s_setTick;

        [Component(Compiler = false)]
        private static VNode LayoutCleanupThrower()
        {
            Hooks.UseLayoutEffect(() => () => throw new InvalidOperationException(CleanupError), Array.Empty<object>());
            return V.Label(text: "thrower");
        }

        // The deletion that aborts is in an element the boundary renders, which its fallback removes whole.
        [Component(Compiler = false, IsErrorBoundary = true)]
        private static VNode AbortingBoundary(int tick)
        {
            Hooks.UseFallback(_ => V.Label(text: "fallback"));
            return V.Div(children: new VNode[]
            {
                tick == 0 ? V.Component(LayoutCleanupThrower, key: "thrower") : V.Label(text: "dropped"),
            });
        }

        [Component(Compiler = false)]
        private static VNode AbortingHost()
        {
            var (tick, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            return V.Component(AbortingBoundary, tick, key: "boundary");
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
        public void Given_ADeletionWhoseLayoutCleanupThrowsIntoABoundaryAboveItsElement_When_TheBoundaryFallsBack_Then_NoRowStaysRegisteredAsTakenDown()
        {
            // Arrange
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(AbortingHost), CaughtErrors.Unlogged);
            mounted.DrainForTest();
            var context = mounted.Root.Reconciler!.Context;

            // Act
            s_setTick.Invoke(1);
            mounted.DrainForTest();

            // Assert — a boundary that never fell back aborted nothing, so that reading is a sentinel.
            Assert.That(root.FindLabelByText("fallback") != null ? context.RemovalsTakenDown.Count : -1, Is.EqualTo(0));
        }

        [TestCase("_removedRows")]
        [TestCase("_orphanRows")]
        [TestCase("_roots")]
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
