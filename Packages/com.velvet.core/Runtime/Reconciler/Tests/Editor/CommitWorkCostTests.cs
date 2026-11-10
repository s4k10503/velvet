using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what the commit work that orders a hide's cleanups and a Portal's children costs: a walk whose
    /// <c>V.Suspense</c> boundaries hid and revealed nothing allocates nothing applying that, and twice the components
    /// in one element, hidden or placed after a Portal, read that element no more than twice as often.
    /// </summary>
    [TestFixture]
    internal sealed class CommitWorkCostTests
    {
        private sealed record ListProps(int Rows, VisualElement? Target);

        // Counts the reads of its children that ask for contentContainer; ReadsToHide and ReadsToMount check the two
        // they rely on.
        private sealed class CountingContainer : VisualElement
        {
            internal int Reads;

            public override VisualElement contentContainer
            {
                get
                {
                    Reads++;
                    return this;
                }
            }
        }

        [Component]
        private static VNode Row() => V.Div();

        [Component]
        private static VNode Rows(int count)
        {
            var rows = new VNode[count];
            for (var i = 0; i < count; i++) rows[i] = V.Component(Row, key: i.ToString());
            return V.Fragment(rows);
        }

        [Component]
        private static VNode PortalAheadOfRows(ListProps props)
        {
            var nodes = new VNode?[props.Rows + 1];
            nodes[0] = props.Target == null
                ? null
                : V.Portal(props.Target, new VNode[] { V.Component(Row, key: "portal-row") }, key: "portal");
            for (var i = 0; i < props.Rows; i++) nodes[i + 1] = V.Component(Row, key: i.ToString());
            return V.Fragment(nodes);
        }

        // GREEN_ON_BASE(characterization): the base commits a Portal's children without reading their siblings' element.
        // What this pins is that placing them where the Portal sits reads that element a bounded number of times per row.
        [Test]
        public void Given_APortalAheadOfTwiceTheSiblings_When_Mounted_Then_WhatThePortalAddsToTheReadsOfTheirElementAtMostDoubles()
        {
            // Arrange
            var fewer = ReadsThePortalAdds(500);

            // Act
            var more = ReadsThePortalAdds(1000);

            // Assert — what grows with the siblings cancels here, so what is left is the Portal's own reads; reading
            // the earlier siblings again for each sibling would leave a remainder growing with the square of their
            // count. Where a count could not be taken, the excess is one no bound admits.
            Assert.That(fewer == int.MinValue || more == int.MinValue ? int.MaxValue : more - 2 * fewer, Is.LessThan(500));
        }

        // The reads of the siblings' element a Portal ahead of them adds to their mount, or int.MinValue where the
        // siblings did not mount in that element, or a read of its child count and of a child goes uncounted.
        private static int ReadsThePortalAdds(int rows)
        {
            var withPortal = ReadsToMount(rows, new VisualElement());
            var without = ReadsToMount(rows, null);
            return withPortal < 0 || without < 0 ? int.MinValue : withPortal - without;
        }

        private static int ReadsToMount(int rows, VisualElement? target)
        {
            var container = new CountingContainer();
            using var mounted = V.Mount(container, V.Component(PortalAheadOfRows, new ListProps(rows, target)));
            mounted.DrainForTest();
            var reads = container.Reads;
            container.Reads = 0;
            var children = container.childCount;
            _ = container[0];
            return container.Reads < 2 || children < rows ? -1 : reads;
        }

        // GREEN_ON_BASE(characterization): the base returns before allocating when a walk hid and revealed nothing.
        // What this pins is that applying the hides in React's order kept that.
        [Test]
        public void Given_AWalkWhoseBoundariesHidAndRevealedNothing_When_ItsChangesApply_Then_NothingIsAllocated()
        {
            // Arrange
            using var mounted = V.Mount(new VisualElement(), V.Div());
            var apply = ApplyOffscreenChangesOf(mounted);
            var nothing = new GeneralPathReconciler.GeneralCommitState();
            Action applyNothing = () => apply(nothing);
            Action canary = () => GC.KeepAlive(new object());
            applyNothing();
            canary();

            // Act
            var canaryBlocks = GCAllocationProbe.MedianBlocksDuring(canary);
            var blocks = GCAllocationProbe.MedianBlocksDuring(applyNothing);

            // Assert — a probe that counts nothing for the canary cannot vouch for a zero
            Assert.That(canaryBlocks > 0 ? blocks : -1, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base hides each component without reading the element it sits in.
        // What this pins is that placing them in the walk reads that element a bounded number of times per row.
        [Test]
        public void Given_TwiceTheRowsInOneElement_When_TheirComponentsAreHidden_Then_TheElementIsReadNoMoreThanTwiceAsOften()
        {
            // Arrange
            var fewer = ReadsToHide(1000);

            // Act
            var more = ReadsToHide(2000);

            // Assert — where a count could not be taken, the excess is one no bound admits
            Assert.That(fewer < 0 || more < 0 ? int.MaxValue : more - 2 * fewer, Is.LessThanOrEqualTo(0));
        }

        // The reads of the rows' element while its rows' components are hidden, or -1 where the rows did not mount
        // in it one component each, or where a read of its child count and of a child goes uncounted.
        private static int ReadsToHide(int rows)
        {
            var container = new CountingContainer();
            using var mounted = V.Mount(container, V.Component(Rows, rows));
            mounted.DrainForTest();
            var hidden = new List<(ComponentFiber Fiber, bool Hidden)>();
            FiberTreeTraversal.Visit(mounted.Root, fiber =>
            {
                if (ReferenceEquals(fiber.MountPoint, container) && fiber.MountSlotCount == 1) hidden.Add((fiber, true));
            });
            if (hidden.Count != rows) return -1;
            var apply = ApplyOffscreenChangesOf(mounted);
            var commit = new GeneralPathReconciler.GeneralCommitState { OffscreenChanges = hidden };
            container.Reads = 0;
            _ = container.childCount;
            _ = container[0];
            if (container.Reads < 2) return -1;
            container.Reads = 0;
            apply(commit);
            return container.Reads;
        }

        private static Action<GeneralPathReconciler.GeneralCommitState> ApplyOffscreenChangesOf(MountedTree mounted)
        {
            const BindingFlags members = BindingFlags.Instance | BindingFlags.NonPublic;
            var childPass = typeof(Reconciler).GetProperty("ChildPass", members)!.GetValue(mounted.Root.Reconciler);
            var general = typeof(ChildReconciler).GetField("_general", members)!.GetValue(childPass);
            var method = typeof(GeneralPathReconciler).GetMethod("ApplyOffscreenChanges", members)!;
            return (Action<GeneralPathReconciler.GeneralCommitState>)Delegate.CreateDelegate(
                typeof(Action<GeneralPathReconciler.GeneralCommitState>), general, method);
        }
    }
}
