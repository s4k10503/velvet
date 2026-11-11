using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what the commit work that orders a hide's cleanups and a Portal's children costs: a walk whose
    /// <c>V.Suspense</c> boundaries hid and revealed nothing allocates nothing applying that, and twice the components
    /// in one element, hidden or placed after a Portal, external to it or rendering into it, read and search that
    /// element no more than twice as much.
    /// </summary>
    [TestFixture]
    internal sealed class CommitWorkCostTests
    {
        private sealed record ListProps(int Rows, VisualElement? Target);

        // Counts the reads of its children that ask for contentContainer; ReadsToHide and ReadsToMount check the ones
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

        // Counts the comparisons an equality search makes against it; ReadsToMount checks that a search of a child
        // list is counted once for each child it passes.
        private sealed class CountingChild : VisualElement
        {
            internal static int Comparisons;

            public override bool Equals(object? other)
            {
                Comparisons++;
                return ReferenceEquals(this, other);
            }

            public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
        }

        [Component]
        private static VNode Row() => V.Div();

        [Component]
        private static VNode CountedRow() => V.Custom<CountingChild>();

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
                : V.Portal(props.Target, new VNode[] { V.Component(CountedRow, key: "portal-row") }, key: "portal");
            for (var i = 0; i < props.Rows; i++) nodes[i + 1] = V.Component(CountedRow, key: i.ToString());
            return V.Fragment(nodes);
        }

        // GREEN_ON_BASE(characterization): the base commits a Portal's children without reading their siblings' element.
        // What this pins is that placing them where the Portal sits reads that element a bounded number of times per row.
        [Test]
        public void Given_APortalAheadOfTwiceTheSiblings_When_Mounted_Then_WhatThePortalAddsToReadingTheirElementAtMostDoubles()
        {
            // Arrange
            var fewer = ReadingThePortalAdds(500, intoTheirElement: false);

            // Act
            var more = ReadingThePortalAdds(1000, intoTheirElement: false);

            // Assert — a cost linear in the siblings leaves only the negated part that does not grow with them, while
            // reading the earlier siblings again for each sibling leaves a remainder growing with the square of their
            // count. Where a count could not be taken, the excess is one no bound admits.
            Assert.That(fewer == int.MinValue || more == int.MinValue ? int.MaxValue : more - 2 * fewer, Is.LessThan(500));
        }

        // GREEN_ON_BASE(characterization): the base commits a Portal's children without searching their siblings' element.
        // What this pins is that placing them where the Portal sits finds each sibling's position there once.
        [Test]
        public void Given_APortalIntoTheElementOfTwiceTheSiblingsAfterIt_When_Mounted_Then_WhatThePortalAddsToReadingThatElementAtMostDoubles()
        {
            // Arrange
            var fewer = ReadingThePortalAdds(500, intoTheirElement: true);

            // Act
            var more = ReadingThePortalAdds(1000, intoTheirElement: true);

            // Assert — as above, with the Portal's rows in the siblings' own element, whose positions are read there
            Assert.That(fewer == int.MinValue || more == int.MinValue ? int.MaxValue : more - 2 * fewer, Is.LessThan(500));
        }

        // The reads of the siblings' element, and the comparisons searches of it make, that a Portal ahead of them adds
        // to their mount; int.MinValue where a count could not be taken.
        private static int ReadingThePortalAdds(int rows, bool intoTheirElement)
        {
            var withPortal = ReadingToMount(rows, withPortal: true, intoTheirElement);
            var without = ReadingToMount(rows, withPortal: false, intoTheirElement);
            return withPortal < 0 || without < 0 ? int.MinValue : withPortal - without;
        }

        // -1 where the siblings did not mount in the element, or a read of its child count, of a child or of a child's
        // index goes uncounted, or a search for its last child counts fewer comparisons than half the siblings.
        private static int ReadingToMount(int rows, bool withPortal, bool intoTheirElement)
        {
            var container = new CountingContainer();
            var target = !withPortal ? null : intoTheirElement ? container : new VisualElement();
            CountingChild.Comparisons = 0;
            using var mounted = V.Mount(container, V.Component(PortalAheadOfRows, new ListProps(rows, target)));
            mounted.DrainForTest();
            var reading = container.Reads + CountingChild.Comparisons;
            container.Reads = 0;
            CountingChild.Comparisons = 0;
            var children = container.childCount;
            _ = container.IndexOf(container[children - 1]);
            var counted = container.Reads >= 3 && CountingChild.Comparisons >= rows / 2;
            return !counted || children < rows ? -1 : reading;
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
