using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// <see cref="ContainerRows"/> answers what a scan of the live child list answers (<see cref="LogicalChildSlots"/>
    /// for the conversions it still scans, a walk in this fixture for the count and <c>ToLogical</c>), for a container written only through <see cref="ChildListWrites"/>. Every case drives random writes over
    /// real <c>VisualElement</c>s — rendered children, a trailing-kind marker and a leading-kind marker, inserted,
    /// removed, moved, sorted and reclassified — and compares one answer with the scan's after each write. Sizes
    /// 12 and 700 put one block and several blocks under the same writes.
    /// </summary>
    [TestFixture]
    internal sealed class ContainerRowsTests
    {
        private const int Writes = 400;

        private static VisualElement NewChild(System.Random random)
        {
            var child = new VisualElement();
            switch (random.Next(5))
            {
                case 0:
                    child.AddToClassList(SilhouetteBoundsSpacer.MarkerClass);
                    break;
                case 1:
                    child.AddToClassList(RingOverlay.InsetMarkerClass);
                    break;
            }
            return child;
        }

        // Applies one random write to parent through ChildListWrites and calls check after it.
        private static string Drive(int size, int seed, Func<VisualElement, ContainerRows, string?> check)
        {
            var random = new System.Random(seed);
            var parent = new VisualElement();
            for (var i = 0; i < size; i++) ChildListWrites.Add(parent, NewChild(random));
            var report = new StringBuilder();
            for (var step = 0; step < Writes && report.Length == 0; step++)
            {
                var count = parent.childCount;
                switch (random.Next(7))
                {
                    case 0:
                    case 1:
                        ChildListWrites.Insert(parent, random.Next(count + 1), NewChild(random));
                        break;
                    case 2:
                        if (count > 0) ChildListWrites.RemoveAt(parent, random.Next(count));
                        break;
                    case 3:
                        if (count > 0) ChildListWrites.RemoveFromHierarchy(parent[random.Next(count)]);
                        break;
                    case 4:
                        if (count > 0)
                        {
                            var moved = parent[random.Next(count)];
                            if (random.Next(3) == 0) ChildListWrites.Add(parent, moved);
                            else ChildListWrites.Insert(parent, random.Next(count + 1), moved);
                        }
                        break;
                    case 5:
                        if (count > 0)
                        {
                            var flipped = parent[random.Next(count)];
                            flipped.EnableInClassList(SilhouetteBoundsSpacer.MarkerClass,
                                !flipped.ClassListContains(SilhouetteBoundsSpacer.MarkerClass));
                            ChildListWrites.Reclassify(flipped);
                        }
                        break;
                    default:
                        var keys = new Dictionary<VisualElement, int>();
                        for (var i = 0; i < count; i++) keys[parent[i]] = random.Next(1000) * 1000 + i;
                        ChildListWrites.HierarchySort(parent, (a, b) => keys[a].CompareTo(keys[b]));
                        break;
                }
                var rows = RowModelAccess.Peek(parent);
                if (rows == null)
                {
                    if (parent.childCount != 0) report.Append($"step {step}: no model for {parent.childCount} children");
                    continue;
                }
                var difference = check(parent, rows);
                if (difference != null) report.Append($"step {step}: {difference}");
            }
            return report.ToString();
        }

        private static string? CompareToPhysical(VisualElement parent, ContainerRows rows)
        {
            for (var slot = -1; slot <= parent.childCount + 1; slot++)
            {
                var expected = LogicalChildSlots.ToPhysical(parent, slot);
                var actual = RowModelAccess.ToPhysical(rows, slot);
                if (expected != actual) return $"ToPhysical({slot}) {actual}, scan {expected}";
            }
            return null;
        }

        private static string? CompareTryGetPhysical(VisualElement parent, ContainerRows rows)
        {
            for (var slot = -1; slot <= parent.childCount + 1; slot++)
            {
                var expected = LogicalChildSlots.TryGetPhysical(parent, slot, out var expectedAt);
                var actual = RowModelAccess.TryGetPhysical(rows, slot, out var actualAt);
                if (expected != actual || expectedAt != actualAt)
                {
                    return $"TryGetPhysical({slot}) {actual}/{actualAt}, scan {expected}/{expectedAt}";
                }
            }
            return null;
        }

        private static string? CompareRowAt(VisualElement parent, ContainerRows rows)
        {
            for (var slot = -1; slot <= parent.childCount + 1; slot++)
            {
                var expected = LogicalChildSlots.TryGetPhysical(parent, slot, out var physical) ? parent[physical] : null;
                if (!ReferenceEquals(expected, rows.RowAt(slot))) return $"RowAt({slot}) is not the scan's child";
            }
            return null;
        }

        private static string? ComparePhysicalRange(VisualElement parent, ContainerRows rows)
        {
            var count = parent.childCount;
            var found = new List<VisualElement>();
            foreach (var (start, end) in new[] { (0, count), (count / 3, count - count / 4), (count - 1, count + 2), (-2, 2) })
            {
                found.Clear();
                rows.PhysicalRange(start, end, found);
                var expected = new List<VisualElement>();
                for (var i = Math.Max(start, 0); i < Math.Min(end, count); i++) expected.Add(parent[i]);
                if (!expected.SequenceEqual(found)) return $"PhysicalRange({start}, {end}) holds {found.Count} children, scan {expected.Count}";
            }
            return null;
        }

        private static string? CompareToLogical(VisualElement parent, ContainerRows rows)
        {
            for (var physical = -1; physical <= parent.childCount + 1; physical++)
            {
                var expected = ScanToLogical(parent, physical);
                var actual = RowModelAccess.ToLogical(rows, physical);
                if (expected != actual) return $"ToLogical({physical}) {actual}, scan {expected}";
            }
            return null;
        }

        private static string? CompareCount(VisualElement parent, ContainerRows rows)
            => rows.RenderedCount == ScanCount(parent)
                ? null
                : $"RenderedCount {rows.RenderedCount}, scan {ScanCount(parent)}";

        // A walk of the live child list: LogicalChildSlots.Count and ToLogical read the model, so comparing the model
        // with them would compare it with itself.
        private static int ScanCount(VisualElement parent) => ScanToLogical(parent, parent.childCount);

        private static int ScanToLogical(VisualElement parent, int physical)
        {
            var rendered = 0;
            for (var i = 0; i < Math.Min(physical, parent.childCount); i++)
            {
                if (!LogicalChildSlots.IsInvisible(parent[i])) rendered++;
            }
            return rendered;
        }

        private static string? ComparePhysicalIndexOf(VisualElement parent, ContainerRows rows)
        {
            for (var i = 0; i < parent.childCount; i++)
            {
                if (RowModelAccess.PhysicalIndexOf(rows, parent[i]) != i) return $"PhysicalIndexOf of child {i} is {RowModelAccess.PhysicalIndexOf(rows, parent[i])}";
            }
            return null;
        }

        private static string? CompareSlotOf(VisualElement parent, ContainerRows rows)
        {
            var slot = 0;
            for (var i = 0; i < parent.childCount; i++)
            {
                var expected = LogicalChildSlots.IsInvisible(parent[i]) ? -1 : slot++;
                if (RowModelAccess.SlotOf(rows, parent[i]) != expected) return $"SlotOf child {i} is {RowModelAccess.SlotOf(rows, parent[i])}, scan {expected}";
            }
            return null;
        }

        internal static string? CompareOrder(VisualElement parent, ContainerRows rows)
        {
            if (rows.TotalCount != parent.childCount) return $"TotalCount {rows.TotalCount}, live {parent.childCount}";
            for (var i = 0; i < parent.childCount; i++)
            {
                if (!ReferenceEquals(rows.ElementAt(i), parent[i])) return $"ElementAt({i}) is another element";
            }
            return null;
        }

        // GREEN_ON_BASE(refactor): the model is compared with the same scan as before; only the helper that walks the list moved into this fixture.
        [TestCase(12)]
        [TestCase(700)]
        public void Given_RandomWrites_When_EverySlotIsConverted_Then_ToPhysicalMatchesTheScan(int size)
        {
            // Arrange
            var check = (Func<VisualElement, ContainerRows, string?>)CompareToPhysical;

            // Act
            var report = Drive(size, 1, check);

            // Assert
            Assert.That(report, Is.Empty);
        }

        // GREEN_ON_BASE(refactor): the model is compared with the same scan as before; only the helper that walks the list moved into this fixture.
        [TestCase(12)]
        [TestCase(700)]
        public void Given_RandomWrites_When_EverySlotIsAsked_Then_TryGetPhysicalMatchesTheScan(int size)
        {
            // Arrange
            var check = (Func<VisualElement, ContainerRows, string?>)CompareTryGetPhysical;

            // Act
            var report = Drive(size, 2, check);

            // Assert
            Assert.That(report, Is.Empty);
        }

        [TestCase(12)]
        [TestCase(700)]
        public void Given_RandomWrites_When_EverySlotIsRead_Then_RowAtIsTheChildTheScanFinds(int size)
        {
            // Arrange
            var check = (Func<VisualElement, ContainerRows, string?>)CompareRowAt;

            // Act
            var report = Drive(size, 7, check);

            // Assert
            Assert.That(report, Is.Empty);
        }

        [TestCase(12)]
        [TestCase(700)]
        public void Given_RandomWrites_When_PhysicalRangesAreRead_Then_PhysicalRangeIsTheScansSlice(int size)
        {
            // Arrange
            var check = (Func<VisualElement, ContainerRows, string?>)ComparePhysicalRange;

            // Act
            var report = Drive(size, 8, check);

            // Assert
            Assert.That(report, Is.Empty);
        }

        // GREEN_ON_BASE(refactor): the model is compared with the same scan as before; only the helper that walks the list moved into this fixture.
        [TestCase(12)]
        [TestCase(700)]
        public void Given_RandomWrites_When_EveryPhysicalIndexIsConverted_Then_ToLogicalMatchesTheScan(int size)
        {
            // Arrange
            var check = (Func<VisualElement, ContainerRows, string?>)CompareToLogical;

            // Act
            var report = Drive(size, 3, check);

            // Assert
            Assert.That(report, Is.Empty);
        }

        // GREEN_ON_BASE(refactor): the model is compared with the same scan as before; only the helper that walks the list moved into this fixture.
        [TestCase(12)]
        [TestCase(700)]
        public void Given_RandomWrites_When_RowsAreCounted_Then_RenderedCountMatchesTheScan(int size)
        {
            // Arrange
            var check = (Func<VisualElement, ContainerRows, string?>)CompareCount;

            // Act
            var report = Drive(size, 4, check);

            // Assert
            Assert.That(report, Is.Empty);
        }

        // GREEN_ON_BASE(refactor): the model is compared with the same scan as before; only the helper that walks the list moved into this fixture.
        [TestCase(12)]
        [TestCase(700)]
        public void Given_RandomWrites_When_EveryChildIsLookedUp_Then_PhysicalIndexOfIsItsChildIndex(int size)
        {
            // Arrange
            var check = (Func<VisualElement, ContainerRows, string?>)ComparePhysicalIndexOf;

            // Act
            var report = Drive(size, 5, check);

            // Assert
            Assert.That(report, Is.Empty);
        }

        // GREEN_ON_BASE(refactor): the model is compared with the same scan as before; only the helper that walks the list moved into this fixture.
        [TestCase(12)]
        [TestCase(700)]
        public void Given_RandomWrites_When_EveryChildIsLookedUp_Then_SlotOfMatchesTheScan(int size)
        {
            // Arrange
            var check = (Func<VisualElement, ContainerRows, string?>)CompareSlotOf;

            // Act
            var report = Drive(size, 6, check);

            // Assert
            Assert.That(report, Is.Empty);
        }

        // GREEN_ON_BASE(refactor): reads the model through the surface step 1 added, which this change leaves as it was.
        [TestCase(12)]
        [TestCase(700)]
        public void Given_RandomWrites_When_TheModelIsRead_Then_ItHoldsTheLiveChildrenInOrder(int size)
        {
            // Arrange
            var check = (Func<VisualElement, ContainerRows, string?>)CompareOrder;

            // Act
            var report = Drive(size, 7, check);

            // Assert
            Assert.That(report, Is.Empty);
        }

        // GREEN_ON_BASE(refactor): reads the model through the surface step 1 added, which this change leaves as it was.
        [Test]
        public void Given_AForeignMoveThatKeepsTheEndsAndAWriteAfterIt_When_APassBegins_Then_TheModelHoldsTheLiveOrder()
        {
            // Arrange
            var parent = new VisualElement();
            var moved = new VisualElement();
            ChildListWrites.Add(parent, new VisualElement());
            ChildListWrites.Add(parent, moved);
            ChildListWrites.Add(parent, new VisualElement());
            ChildListWrites.Add(parent, new VisualElement());
            parent.Remove(moved);
            parent.Insert(2, moved);
            ChildListWrites.Insert(parent, 3, new VisualElement());
            LogAssert.Expect(LogType.Assert, new System.Text.RegularExpressions.Regex("ContainerRows drifted"));

            // Act
            ChildListWrites.BeginPass();

            // Assert
            Assert.That(CompareOrder(parent, RowModelAccess.Peek(parent)!), Is.Null);
        }

        // GREEN_ON_BASE(refactor): reads the model through the surface step 1 added, which this change leaves as it was.
        [Test]
        public void Given_AChildAlreadyInside_When_ItIsAddedAgain_Then_TheModelFollowsItToTheEnd()
        {
            // Arrange
            var parent = new VisualElement();
            var first = new VisualElement();
            ChildListWrites.Add(parent, first);
            ChildListWrites.Add(parent, new VisualElement());
            ChildListWrites.Add(parent, new VisualElement());

            // Act
            ChildListWrites.Add(parent, first);

            // Assert
            Assert.That(CompareOrder(parent, RowModelAccess.Peek(parent)!), Is.Null);
        }

        // GREEN_ON_BASE(refactor): reads the model through the surface step 1 added, which this change leaves as it was.
        [Test]
        public void Given_AChildMovedToAnotherContainer_When_ItsFormerContainerIsEmptied_Then_ItKeepsNoModel()
        {
            // Arrange
            var from = new VisualElement();
            var to = new VisualElement();
            var child = new VisualElement();
            ChildListWrites.Add(from, child);

            // Act
            ChildListWrites.Add(to, child);

            // Assert
            Assert.That(RowModelAccess.Peek(from), Is.Null);
        }

        // GREEN_ON_BASE(refactor): reads the model through the surface step 1 added, which this change leaves as it was.
        [Test]
        public void Given_ABigModelWhoseLastChildWasMovedBehindItsBack_When_TheNextWriteArrives_Then_TheModelHoldsTheLiveOrder()
        {
            // Arrange
            var parent = new VisualElement();
            for (var i = 0; i < 300; i++) ChildListWrites.Add(parent, new VisualElement());
            parent[0].BringToFront();

            // Act
            ChildListWrites.Add(parent, new VisualElement());

            // Assert
            Assert.That(CompareOrder(parent, RowModelAccess.Peek(parent)!), Is.Null);
        }

        // GREEN_ON_BASE(refactor): reads the model through the surface step 1 added, which this change leaves as it was.
        [Test]
        public void Given_AnInteriorMoveBehindTheModelsBackThatWasAnnounced_When_TheNextWriteArrives_Then_TheModelHoldsTheLiveOrder()
        {
            // Arrange
            var parent = new VisualElement();
            for (var i = 0; i < 300; i++) ChildListWrites.Add(parent, new VisualElement());
            parent[100].PlaceBehind(parent[200]);
            ChildListWrites.NoteHierarchyChange(parent[100]);

            // Act
            ChildListWrites.Add(parent, new VisualElement());

            // Assert
            Assert.That(CompareOrder(parent, RowModelAccess.Peek(parent)!), Is.Null);
        }

        // GREEN_ON_BASE(characterization): the base takes the stale mark off with the rows it rebuilds. Have
        // ContainerRows.Clear set `Stale` to true and the model stays marked, so the next read outside a write
        // rebuilds it again.
        [Test]
        public void Given_AModelMarkedStaleByAnAnnouncedChange_When_AReadRebuildsIt_Then_ItIsNoLongerMarkedStale()
        {
            // Arrange
            var parent = new VisualElement();
            ChildListWrites.Add(parent, new VisualElement());
            ChildListWrites.Add(parent, new VisualElement());
            ChildListWrites.NoteHierarchyChange(parent[0]);
            var marked = RowModelAccess.Peek(parent)!.Stale;

            // Act
            _ = ChildListWrites.RenderedCount(parent);

            // Assert — read as null where the announcement marked nothing for the read to rebuild.
            Assert.That(marked ? RowModelAccess.Peek(parent)!.Stale : (bool?)null, Is.False);
        }

        // GREEN_ON_BASE(characterization): the base empties a stale model before a pass's start fills it again. Delete
        // `rows.Clear();` from the stale branch of ChildListWrites.Verify and the fill lands on the rows already held.
        [Test]
        public void Given_AModelMarkedStaleByAnAnnouncedChange_When_APassBegins_Then_TheModelHoldsEachChildOnce()
        {
            // Arrange
            var parent = new VisualElement();
            ChildListWrites.Add(parent, new VisualElement());
            ChildListWrites.Add(parent, new VisualElement());
            ChildListWrites.NoteHierarchyChange(parent[0]);
            var marked = RowModelAccess.Peek(parent)!.Stale;

            // Act
            ChildListWrites.BeginPass();

            // Assert — read as -1 where the announcement marked nothing for the pass to rebuild.
            Assert.That(marked ? RowModelAccess.Peek(parent)!.TotalCount : -1, Is.EqualTo(parent.childCount));
        }

        // GREEN_ON_BASE(refactor): reads the model through the surface step 1 added, which this change leaves as it was.
        [Test]
        public void Given_AChildAddedBehindTheModelsBack_When_TheNextWriteArrives_Then_TheModelHoldsEveryChild()
        {
            // Arrange
            var parent = new VisualElement();
            ChildListWrites.Add(parent, new VisualElement());
            parent.Add(new VisualElement());

            // Act
            ChildListWrites.Add(parent, new VisualElement());

            // Assert
            Assert.That(RowModelAccess.Peek(parent)!.TotalCount, Is.EqualTo(3));
        }

        // GREEN_ON_BASE(refactor): reads the model through the surface step 1 added, which this change leaves as it was.
        [Test]
        public void Given_ALeadingMarkerAndARenderedChild_When_TheMarkerIsReclassifiedAsTrailing_Then_AnAppendLandsBeforeIt()
        {
            // Arrange
            var parent = new VisualElement();
            var marker = new VisualElement();
            marker.AddToClassList(RingOverlay.InsetMarkerClass);
            ChildListWrites.Add(parent, marker);
            ChildListWrites.Add(parent, new VisualElement());
            marker.RemoveFromClassList(RingOverlay.InsetMarkerClass);
            marker.AddToClassList(SilhouetteBoundsSpacer.MarkerClass);

            // Act
            ChildListWrites.Reclassify(marker);

            // Assert
            Assert.That(RowModelAccess.ToPhysical(RowModelAccess.Peek(parent)!, 1), Is.EqualTo(LogicalChildSlots.ToPhysical(parent, 1)));
        }

        // GREEN_ON_BASE(refactor): reads the model through the surface step 1 added, which this change leaves as it was.
        [Test]
        public void Given_AContainerEmptiedThroughTheWrites_When_ItsModelIsPeeked_Then_ThereIsNone()
        {
            // Arrange
            var parent = new VisualElement();
            var child = new VisualElement();
            ChildListWrites.Add(parent, child);

            // Act
            ChildListWrites.RemoveFromHierarchy(child);

            // Assert
            Assert.That(RowModelAccess.Peek(parent), Is.Null);
        }

        // GREEN_ON_BASE(refactor): reads the model through the surface step 1 added, which this change leaves as it was.
        [Test]
        public void Given_AWriteThatReorderedBehindTheModelsBack_When_TheVerifierRuns_Then_ItLogsTheDrift()
        {
            // Arrange
            var parent = new VisualElement();
            ChildListWrites.Add(parent, new VisualElement());
            ChildListWrites.Add(parent, new VisualElement());
            parent[0].BringToFront();
            LogAssert.Expect(LogType.Assert, new System.Text.RegularExpressions.Regex("ContainerRows drifted"));

            // Act
            ChildListWrites.VerifyTouched();

            // Assert
            Assert.That(RowModelAccess.Peek(parent)!.ElementAt(0), Is.SameAs(parent[0]));
        }

        [Test]
        public void Given_AScrollViewWrittenThroughItsHierarchy_When_ThePlacedChildIsLookedUp_Then_ItsHierarchyIndexIsAnswered()
        {
            // Arrange
            var scroll = new ScrollView();
            var first = new VisualElement();
            var second = new VisualElement();
            ChildListWrites.HierarchyAdd(scroll, first);
            ChildListWrites.HierarchyInsert(scroll, 0, second);

            // Act
            var index = ChildListWrites.HierarchyIndexOf(scroll, second);

            // Assert
            Assert.That(index, Is.EqualTo(scroll.hierarchy.IndexOf(second)));
        }

        // GREEN_ON_BASE(characterization): the base drops the model of an element whose side tables it clears. Drop
        // `ChildListWrites.Drop(element);` and a removed container keeps the model of the children it held.
        [Test]
        public void Given_AMountedContainerWithChildren_When_ItIsUnmounted_Then_ItsRowModelIsGone()
        {
            // Arrange
            using var scope = new Velvet.TestUtilities.ReconcilerScope();
            var tree = new VNode[] { V.Div(name: "card", children: new VNode[] { V.Label(text: "a"), V.Label(text: "b") }) };
            scope.Reconciler.Reconcile(scope.Root, System.Array.Empty<VNode>(), tree);
            var card = scope.Root.Q<VisualElement>("card");
            var modeledWhileMounted = RowModelAccess.Peek(card) != null;

            // Act
            scope.Reconciler.Reconcile(scope.Root, tree, System.Array.Empty<VNode>());

            // Assert — a container that never had a model has none to drop, so the value read is one that is not
            // null for it.
            Assert.That(modeledWhileMounted ? RowModelAccess.Peek(card) : new object(), Is.Null);
        }

        // An element whose child-list calls land on another one, as a ScrollView's do on its content container.
        private sealed class Forwarder : VisualElement
        {
            internal VisualElement? Target;

            public override VisualElement contentContainer => Target ?? this;
        }

        // Forwarders 0..length-1, each handing its child-list calls to the next; the last holds its own children.
        private static Forwarder[] Chain(int length)
        {
            var chain = new Forwarder[length];
            for (var i = 0; i < length; i++) chain[i] = new Forwarder();
            for (var i = 0; i + 1 < length; i++) chain[i].Target = chain[i + 1];
            return chain;
        }

        private static List<VisualElement> Rendered(int count)
        {
            var elements = new List<VisualElement>();
            for (var i = 0; i < count; i++) elements.Add(new VisualElement());
            return elements;
        }

        private static ContainerRows ModelOf(List<VisualElement> elements)
        {
            var rows = new ContainerRows();
            for (var i = 0; i < elements.Count; i++) rows.Insert(i, elements[i], RowKind.Rendered);
            return rows;
        }

        // GREEN_ON_BASE(characterization): the model of a forwarding parent's content element is the one that is written.
        [Test]
        public void Given_AParentThatForwardsItsChildren_When_AChildIsAdded_Then_TheModelOfTheForwardedElementHoldsIt()
        {
            // Arrange
            var forwarded = new VisualElement();
            var parent = new Forwarder { Target = forwarded };

            // Act
            ChildListWrites.Add(parent, new VisualElement());

            // Assert
            Assert.That(RowModelAccess.Peek(forwarded)?.TotalCount, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): a forwarding loop is followed for exactly eight hops, which ends on its first element.
        [Test]
        public void Given_TwoParentsThatForwardToEachOther_When_AChildOfTheFirstIsLookedUp_Then_ItIsFoundInTheFirst()
        {
            // Arrange
            var first = new Forwarder();
            var second = new Forwarder { Target = first };
            first.Target = second;
            var child = new VisualElement();
            first.hierarchy.Add(child);

            // Act
            var index = ChildListWrites.PhysicalIndexOf(first, child);

            // Assert
            Assert.That(index, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): an insert through a chain longer than the hop cap lands where the head's own Insert puts it.
        [Test]
        public void Given_AChainOfForwardersLongerThanTheHopCap_When_AChildIsInsertedAtItsHead_Then_ItLandsWhereTheHeadsOwnInsertPutsIt()
        {
            // Arrange
            var chain = Chain(10);

            // Act
            ChildListWrites.Insert(chain[0], 0, new VisualElement());

            // Assert
            Assert.That(chain[9].hierarchy.childCount, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): a clear through a chain longer than the hop cap reaches the end of the chain when the element at the cap is empty.
        [Test]
        public void Given_AChainOfForwardersLongerThanTheHopCap_When_TheHeadIsCleared_Then_TheChildrenAtTheEndGo()
        {
            // Arrange
            var chain = Chain(10);
            chain[9].hierarchy.Add(new VisualElement());

            // Act
            ChildListWrites.Clear(chain[0]);

            // Assert
            Assert.That(chain[9].hierarchy.childCount, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the hierarchy form writes the parent's own list even where the parent forwards.
        [Test]
        public void Given_AParentThatForwardsItsChildren_When_AChildIsInsertedThroughItsHierarchy_Then_ItIsTheParentsOwnChild()
        {
            // Arrange
            var parent = new Forwarder { Target = new VisualElement() };

            // Act
            ChildListWrites.HierarchyInsert(parent, 0, new VisualElement());

            // Assert
            Assert.That(parent.hierarchy.childCount, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the hierarchy form writes the parent's own list even where the parent forwards.
        [Test]
        public void Given_AParentThatForwardsItsChildren_When_AChildIsAddedThroughItsHierarchy_Then_ItIsTheParentsOwnChild()
        {
            // Arrange
            var parent = new Forwarder { Target = new VisualElement() };

            // Act
            ChildListWrites.HierarchyAdd(parent, new VisualElement());

            // Assert
            Assert.That(parent.hierarchy.childCount, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): a child inserted at the front leaves the model of the container it came from.
        [Test]
        public void Given_AChildOfAContainerWithTwoChildren_When_ItIsInsertedAtTheFrontOfAnother_Then_ItsFormerModelHoldsOne()
        {
            // Arrange
            var from = new VisualElement();
            var to = new VisualElement();
            var moved = new VisualElement();
            ChildListWrites.Add(from, moved);
            ChildListWrites.Add(from, new VisualElement());

            // Act
            ChildListWrites.Insert(to, 0, moved);

            // Assert
            Assert.That(RowModelAccess.Peek(from)!.TotalCount, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): the container a child leaves is compared with its model when the pass is verified.
        [Test]
        public void Given_AFormerContainerWhoseInteriorWasSwappedBehindItsModel_When_AChildLeavesItAndThePassIsVerified_Then_TheDriftIsLogged()
        {
            // Arrange
            var from = new VisualElement();
            var to = new VisualElement();
            var leaving = new VisualElement();
            var second = new VisualElement();
            var third = new VisualElement();
            ChildListWrites.Add(from, leaving);
            ChildListWrites.Add(from, second);
            ChildListWrites.Add(from, third);
            ChildListWrites.Add(from, new VisualElement());
            ChildListWrites.Add(to, new VisualElement());
            LogAssert.ignoreFailingMessages = true;
            ChildListWrites.VerifyTouched();
            LogAssert.ignoreFailingMessages = false;
            third.PlaceBehind(second);
            ChildListWrites.Insert(to, 1, leaving);
            LogAssert.Expect(LogType.Assert, new System.Text.RegularExpressions.Regex("ContainerRows drifted"));

            // Act
            ChildListWrites.VerifyTouched();

            // Assert
            Assert.That(RowModelAccess.Peek(from)!.ElementAt(0), Is.SameAs(from[0]));
        }

        // GREEN_ON_BASE(characterization): a slot converted over a model that drifted between the probes is answered from the rebuilt model.
        [Test]
        public void Given_AnInteriorPairSwappedBehindTheModel_When_ASlotIsConverted_Then_TheRebuiltModelAnswers()
        {
            // Arrange
            var parent = new VisualElement();
            var first = new VisualElement();
            var second = new VisualElement();
            var third = new VisualElement();
            ChildListWrites.Add(parent, first);
            ChildListWrites.Add(parent, second);
            ChildListWrites.Add(parent, third);
            ChildListWrites.Add(parent, new VisualElement());
            third.PlaceBehind(second);

            // Act
            var physical = ChildListWrites.ToPhysical(parent, 1);

            // Assert
            Assert.That(physical, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): a cleared container keeps no model, as one emptied through RemoveFromHierarchy keeps none.
        [Test]
        public void Given_AContainerWithChildren_When_ItIsCleared_Then_ItKeepsNoModel()
        {
            // Arrange
            var parent = new VisualElement();
            ChildListWrites.Add(parent, new VisualElement());
            ChildListWrites.Add(parent, new VisualElement());

            // Act
            ChildListWrites.Clear(parent);

            // Assert
            Assert.That(RowModelAccess.Peek(parent), Is.Null);
        }

        // GREEN_ON_BASE(characterization): a container emptied through its hierarchy keeps no model, as one emptied through RemoveFromHierarchy keeps none.
        [Test]
        public void Given_AContainerWithOneChild_When_ThatChildIsRemovedThroughTheHierarchy_Then_ItKeepsNoModel()
        {
            // Arrange
            var parent = new VisualElement();
            var child = new VisualElement();
            ChildListWrites.Add(parent, child);

            // Act
            ChildListWrites.HierarchyRemove(parent, child);

            // Assert
            Assert.That(RowModelAccess.Peek(parent), Is.Null);
        }

        // GREEN_ON_BASE(characterization): a container emptied by index keeps no model, as one emptied through RemoveFromHierarchy keeps none.
        [Test]
        public void Given_AContainerWithOneChild_When_ThatChildIsRemovedByIndex_Then_ItKeepsNoModel()
        {
            // Arrange
            var parent = new VisualElement();
            ChildListWrites.Add(parent, new VisualElement());

            // Act
            ChildListWrites.RemoveAt(parent, 0);

            // Assert
            Assert.That(RowModelAccess.Peek(parent), Is.Null);
        }

        // GREEN_ON_BASE(characterization): physical index zero converts to slot zero without building a model for it.
        [Test]
        public void Given_ChildrenNoWriteMade_When_PhysicalIndexZeroIsConvertedToASlot_Then_NoModelIsBuilt()
        {
            // Arrange
            var parent = new VisualElement();
            parent.Add(new VisualElement());

            // Act
            LogicalChildSlots.ToLogical(parent, 0);

            // Assert
            Assert.That(RowModelAccess.Peek(parent), Is.Null);
        }

        // GREEN_ON_BASE(characterization): a split leaves the tail block sized for a full block, so rows added to it grow nothing.
        [Test]
        public void Given_OneRowMoreThanABlockHolds_When_TheModelSplits_Then_TheTailHasRoomForAFullBlock()
        {
            // Arrange
            var rows = ModelOf(Rendered(ContainerRows.BlockMax + 1));

            // Act
            var capacity = RowModelAccess.LastBlockElements(rows).Length;

            // Assert
            Assert.That(capacity, Is.EqualTo(ContainerRows.BlockMax + 1));
        }

        // GREEN_ON_BASE(characterization): a block holds BlockMax rows and splits only when one more arrives.
        [Test]
        public void Given_ExactlyAsManyRowsAsABlockHolds_When_TheyAreInserted_Then_TheModelIsOneBlock()
        {
            // Arrange
            var rows = ModelOf(Rendered(ContainerRows.BlockMax));

            // Act
            var blocks = RowModelAccess.BlockCount(rows);

            // Assert
            Assert.That(blocks, Is.EqualTo(1));
        }

        // GREEN_ON_BASE(characterization): Reserve sizes the first block for the rows a rebuild is about to insert.
        [Test]
        public void Given_AnEmptyModel_When_ItIsReservedForTenRows_Then_ItsFirstBlockHoldsTenWithoutGrowing()
        {
            // Arrange
            var rows = new ContainerRows();

            // Act
            rows.Reserve(10);

            // Assert
            Assert.That(RowModelAccess.FirstBlockElements(rows).Length, Is.EqualTo(10));
        }

        // GREEN_ON_BASE(characterization): a removed row leaves no reference behind in the slot the others shifted out of.
        [Test]
        public void Given_AModelOfThreeRows_When_TheFirstIsRemoved_Then_TheSlotItsLastRowLeftHoldsNothing()
        {
            // Arrange
            var elements = Rendered(3);
            var rows = ModelOf(elements);

            // Act
            rows.Remove(elements[0]);

            // Assert
            Assert.That(RowModelAccess.FirstBlockElements(rows)[2], Is.Null);
        }

        // GREEN_ON_BASE(characterization): a cleared model keeps its arrays and holds no reference in them.
        [Test]
        public void Given_AModelOfTwoRows_When_ItIsCleared_Then_ItsFirstSlotHoldsNothing()
        {
            // Arrange
            var rows = ModelOf(Rendered(2));

            // Act
            rows.Clear();

            // Assert
            Assert.That(RowModelAccess.FirstBlockElements(rows)[0], Is.Null);
        }

        // GREEN_ON_BASE(characterization): the first block of a model that shrank to one block is the block that survived.
        [Test]
        public void Given_ATwoBlockModelWhoseFirstBlockWasEmptied_When_ItsFirstRowIsRead_Then_ItIsTheFirstRowLeft()
        {
            // Arrange
            var elements = Rendered(ContainerRows.BlockMax + 1);
            var rows = ModelOf(elements);
            for (var i = 0; i < (ContainerRows.BlockMax + 1) / 2; i++) rows.Remove(elements[i]);

            // Act
            var first = rows.ElementAt(0);

            // Assert
            Assert.That(first, Is.SameAs(elements[(ContainerRows.BlockMax + 1) / 2]));
        }

        // GREEN_ON_BASE(characterization): a model that shrank to one block drops the element table a container of several blocks needs.
        [Test]
        public void Given_ATwoBlockModelWhoseFirstBlockWasEmptied_When_ItIsRead_Then_ItKeepsNoElementTable()
        {
            // Arrange
            var elements = Rendered(ContainerRows.BlockMax + 1);
            var rows = ModelOf(elements);

            // Act
            for (var i = 0; i < (ContainerRows.BlockMax + 1) / 2; i++) rows.Remove(elements[i]);

            // Assert
            Assert.That(RowModelAccess.HasElementTable(rows), Is.False);
        }

        // GREEN_ON_BASE(characterization): a physical index past the last row is refused rather than wrapped onto the first block.
        [Test]
        public void Given_AModelOfThreeRows_When_TheElementPastTheLastIsRead_Then_ItIsRefused()
        {
            // Arrange
            var rows = ModelOf(Rendered(3));

            // Act + Assert
            Assert.That(() => rows.ElementAt(3), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        // GREEN_ON_BASE(characterization): the element table of a several-block model answers no for an element it never held.
        [Test]
        public void Given_AModelOfSeveralBlocks_When_AnElementItNeverHeldIsLookedUp_Then_ItIsNotContained()
        {
            // Arrange
            var rows = ModelOf(Rendered(ContainerRows.BlockMax + 1));

            // Act
            var contained = rows.Contains(new VisualElement());

            // Assert
            Assert.That(contained, Is.False);
        }

        // GREEN_ON_BASE(characterization): the rows read for a slot range are the range's, and no more.
        [Test]
        public void Given_AModelOfFiveRenderedRows_When_SlotsOneToThreeAreRead_Then_OnlyTheRowsOfSlotsOneAndTwoAreAppended()
        {
            // Arrange
            var elements = Rendered(5);
            var rows = ModelOf(elements);
            var into = new List<VisualElement?>();

            // Act
            rows.Rows(1, 3, into);

            // Assert
            Assert.That(into, Is.EqualTo(new[] { elements[1], elements[2] }));
        }

        // GREEN_ON_BASE(characterization): a start before the first slot reads each row once.
        [Test]
        public void Given_AModelOfThreeRenderedRows_When_RowsAreReadFromBeforeTheFirstSlot_Then_NoRowIsAppendedTwice()
        {
            // Arrange
            var rows = ModelOf(Rendered(3));
            var into = new List<VisualElement?>();

            // Act
            rows.Rows(-2, 2, into);

            // Assert
            Assert.That(into, Is.Unique);
        }
    }
}
