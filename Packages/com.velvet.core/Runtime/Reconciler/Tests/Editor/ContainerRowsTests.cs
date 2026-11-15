using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// <see cref="ContainerRows"/> answers what <see cref="LogicalChildSlots"/> answers by scanning the live child
    /// list, for a container written only through <see cref="ChildListWrites"/>. Every case drives random writes over
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

        private static string? CompareToLogical(VisualElement parent, ContainerRows rows)
        {
            for (var physical = -1; physical <= parent.childCount + 1; physical++)
            {
                var expected = LogicalChildSlots.ToLogical(parent, physical);
                var actual = RowModelAccess.ToLogical(rows, physical);
                if (expected != actual) return $"ToLogical({physical}) {actual}, scan {expected}";
            }
            return null;
        }

        private static string? CompareCount(VisualElement parent, ContainerRows rows)
            => rows.RenderedCount == LogicalChildSlots.Count(parent)
                ? null
                : $"RenderedCount {rows.RenderedCount}, scan {LogicalChildSlots.Count(parent)}";

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
        public void Given_RandomWrites_When_EveryPhysicalIndexIsConverted_Then_ToLogicalMatchesTheScan(int size)
        {
            // Arrange
            var check = (Func<VisualElement, ContainerRows, string?>)CompareToLogical;

            // Act
            var report = Drive(size, 3, check);

            // Assert
            Assert.That(report, Is.Empty);
        }

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
    }
}
