using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the shifts <see cref="PortalSlotTracker"/> makes to the ranges <see cref="PortalRangeStore"/> records:
    /// the starts and last ends they leave are those a move of every range behind the changed one, decided range by
    /// range, leaves. A shift behind the first range on a target, as it leaves, looks up and rewrites no more ranges
    /// where the target holds twice as many, and one behind a range near the last rewrites no more either.
    /// </summary>
    [TestFixture]
    internal sealed class PortalRangeShiftTests
    {
        private sealed class CountedPlaceholder : VisualElement
        {
            private readonly int[] _hashes;

            internal CountedPlaceholder(int[] hashes) => _hashes = hashes;

            public override int GetHashCode()
            {
                _hashes[0]++;
                return base.GetHashCode();
            }

            public override bool Equals(object obj) => ReferenceEquals(this, obj);
        }

        // The lookups of the placeholders made while the first of count one-row ranges on a target leaves and the
        // ranges behind it move up.
        private static int LookupsWhileTheFirstOfLeaves(int count)
        {
            var hashes = new int[1];
            var store = new PortalRangeStore();
            var target = new VisualElement();
            var placeholders = new VisualElement[count];
            for (var i = 0; i < count; i++)
            {
                placeholders[i] = new CountedPlaceholder(hashes);
                store[placeholders[i]] = new PortalSlotInfo(target, i, 1);
            }
            hashes[0] = 0;
            store.Remove(placeholders[0]);
            PortalSlotTracker.ShiftRangesBehind(store, target, placeholders[0], new PortalSlotInfo(target, 0, 1), -1);
            return hashes[0];
        }

        // The records of the store's table, read by reflection, that the removal of the range at index among count
        // one-row ranges on a target and the shift behind it changed; -1 where the table cannot be read.
        private static int RecordsRewrittenWhenOneOfLeaves(int count, int index)
        {
            var store = new PortalRangeStore();
            var target = new VisualElement();
            var placeholders = new VisualElement[count];
            for (var i = 0; i < count; i++)
            {
                placeholders[i] = new VisualElement();
                store[placeholders[i]] = new PortalSlotInfo(target, i, 1);
            }
            var before = Records(store);
            store.Remove(placeholders[index]);
            PortalSlotTracker.ShiftRangesBehind(store, target, placeholders[index], new PortalSlotInfo(target, index, 1), -1);
            var after = Records(store);
            if (before == null || after == null) return -1;
            var rewritten = 0;
            foreach (var pair in after)
            {
                if (before[pair.Key] != pair.Value) rewritten++;
            }
            return rewritten;
        }

        // Each record as text: a value's own, or the values of a reference's fields, a reference among them by identity.
        private static Dictionary<object, string> Records(PortalRangeStore store)
        {
            var field = typeof(PortalRangeStore).GetField("_map", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field?.GetValue(store) is not IDictionary table) return null;
            var records = new Dictionary<object, string>();
            foreach (DictionaryEntry entry in table)
            {
                var record = entry.Value!;
                records[entry.Key] = record.GetType().IsValueType
                    ? record.ToString()
                    : string.Join("|", record.GetType()
                        .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Select(each => each.FieldType.IsValueType
                            ? each.GetValue(record)?.ToString()
                            : RuntimeHelpers.GetHashCode(each.GetValue(record)).ToString()));
            }
            return records;
        }

        [Test]
        public void Given_OneRowRangesOnATarget_When_TheFirstLeavesAndTheRangesBehindItMoveUp_Then_AsManyRangesAreLookedUpAtTwiceTheCount()
        {
            // Arrange
            var atCount = LookupsWhileTheFirstOfLeaves(8);

            // Act
            var atTwiceTheCount = LookupsWhileTheFirstOfLeaves(16);

            // Assert — a run that looked nothing up is not one the growth of a lookup can be read from
            Assert.That(atCount > 0 ? atTwiceTheCount - atCount : int.MinValue, Is.EqualTo(0));
        }

        [Test]
        public void Given_OneRowRangesOnATarget_When_TheFirstLeavesAndTheRangesBehindItMoveUp_Then_AsManyRecordsAreRewrittenAtTwiceTheCount()
        {
            // Arrange
            var atCount = RecordsRewrittenWhenOneOfLeaves(8, 0);

            // Act
            var atTwiceTheCount = RecordsRewrittenWhenOneOfLeaves(16, 0);

            // Assert — a table that could not be read is not one the growth of a rewrite can be read from
            Assert.That(atCount >= 0 ? atTwiceTheCount - atCount : int.MinValue, Is.EqualTo(0));
        }

        // GREEN_ON_BASE(characterization): the base rewrites only the two ranges behind the one that leaves, at either count.
        // The offset this change adds must not trade that for a rewrite of every range ahead.
        [Test]
        public void Given_OneRowRangesOnATarget_When_TheThirdFromLastLeavesAndTheRangesBehindItMoveUp_Then_AsManyRecordsAreRewrittenAtTwiceTheCount()
        {
            // Arrange
            var atCount = RecordsRewrittenWhenOneOfLeaves(8, 5);

            // Act
            var atTwiceTheCount = RecordsRewrittenWhenOneOfLeaves(16, 13);

            // Assert — a table that could not be read is not one the growth of a rewrite can be read from
            Assert.That(atCount >= 0 ? atTwiceTheCount - atCount : int.MinValue, Is.EqualTo(0));
        }

        // Every range kept as plainly as it can be: a shift behind reads each range and moves those IsBehind names.
        private sealed class EveryRange
        {
            internal readonly Dictionary<VisualElement, PortalSlotInfo> Ranges = new();

            internal void ShiftBehind(VisualElement target, VisualElement changedPlaceholder, PortalSlotInfo changed, int delta)
            {
                var behind = new List<VisualElement>();
                foreach (var pair in Ranges)
                {
                    if (ReferenceEquals(pair.Key, changedPlaceholder) || !ReferenceEquals(pair.Value.Target, target)) continue;
                    if (PortalSlotTracker.IsBehind(pair.Value.SlotStart, pair.Value.SlotLength > 0, pair.Key, changedPlaceholder, changed))
                    {
                        behind.Add(pair.Key);
                    }
                }
                foreach (var placeholder in behind) Move(placeholder, delta);
            }

            internal void ShiftOn(VisualElement target, int delta)
            {
                var on = new List<VisualElement>();
                foreach (var pair in Ranges)
                {
                    if (ReferenceEquals(pair.Value.Target, target)) on.Add(pair.Key);
                }
                foreach (var placeholder in on) Move(placeholder, delta);
            }

            internal int? LastEndOn(VisualElement target)
            {
                int? last = null;
                foreach (var range in Ranges.Values)
                {
                    if (!ReferenceEquals(range.Target, target)) continue;
                    var end = range.SlotStart + range.SlotLength;
                    if (last == null || end > last) last = end;
                }
                return last;
            }

            private void Move(VisualElement placeholder, int delta)
                => Ranges[placeholder] = Ranges[placeholder] with { SlotStart = Ranges[placeholder].SlotStart + delta };
        }

        // A run of writes, removals, shifts and reads of a last end over three targets and ten placeholders that share
        // one parent, so an empty range's tie is decided by tree order, applied to the store and to EveryRange, each
        // answering in the same words after every step. Starts and lengths are drawn so ranges overlap and nest, and a
        // write now and then leaves its range on no target.
        private static (string Store, string Plain) Run(Random random)
        {
            var parent = new VisualElement();
            var placeholders = new VisualElement[10];
            for (var i = 0; i < placeholders.Length; i++) parent.Add(placeholders[i] = new VisualElement());
            var targets = new VisualElement[] { new(), new(), new() };
            var store = new PortalRangeStore();
            var plain = new EveryRange();
            var storeLog = new StringBuilder();
            var plainLog = new StringBuilder();
            for (var step = 0; step < 2000; step++)
            {
                var placeholder = placeholders[random.Next(placeholders.Length)];
                var target = targets[random.Next(targets.Length)];
                var range = new PortalSlotInfo(target, random.Next(-2, 14), random.Next(0, 4));
                var written = random.Next(8) == 0 ? range with { Target = null } : range;
                var delta = random.Next(-3, 4);
                switch (random.Next(6))
                {
                    case 0:
                    case 1:
                        store[placeholder] = written;
                        plain.Ranges[placeholder] = written;
                        break;
                    case 2:
                        store.Remove(placeholder);
                        plain.Ranges.Remove(placeholder);
                        break;
                    case 3:
                        PortalSlotTracker.ShiftRangesBehind(store, target, placeholder, range, delta);
                        plain.ShiftBehind(target, placeholder, range, delta);
                        break;
                    case 4:
                        PortalSlotTracker.ShiftRangesOn(store, target, delta);
                        plain.ShiftOn(target, delta);
                        break;
                    default:
                        storeLog.Append(" end ").Append(store.LastEndOn(target));
                        plainLog.Append(" end ").Append(plain.LastEndOn(target));
                        break;
                }
                storeLog.Append('\n').Append(step).Append(':');
                plainLog.Append('\n').Append(step).Append(':');
                for (var i = 0; i < placeholders.Length; i++)
                {
                    storeLog.Append(' ').Append(Describe(store.TryGetValue(placeholders[i], out var stored), stored, targets));
                    plainLog.Append(' ').Append(Describe(plain.Ranges.TryGetValue(placeholders[i], out var kept), kept, targets));
                }
            }
            return (storeLog.ToString(), plainLog.ToString());
        }

        private static string Describe(bool recorded, PortalSlotInfo range, VisualElement[] targets)
            => recorded ? $"{Array.IndexOf(targets, range.Target)}@{range.SlotStart}+{range.SlotLength}" : "-";

        // GREEN_ON_BASE(refactor): the starts and last ends the base's store answers after every step of the run.
        // Holding each start against an offset its target keeps is meant to reproduce them exactly.
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void Given_ASeededRunOfWritesShiftsAndRemovals_When_EachStepIsAppliedToTheStoreAndToEveryRangeInTurn_Then_BothAnswerAlike(int seed)
        {
            // Arrange
            var random = new Random(seed);

            // Act
            var (store, plain) = Run(random);

            // Assert
            Assert.That(store, Is.EqualTo(plain));
        }

        // Placeholders that share one parent, in tree order.
        private static VisualElement[] Siblings(int count)
        {
            var parent = new VisualElement();
            var placeholders = new VisualElement[count];
            for (var i = 0; i < count; i++) parent.Add(placeholders[i] = new VisualElement());
            return placeholders;
        }

        // GREEN_ON_BASE(refactor): the base moves the ranges behind one by one, so the last end follows the one that passes.
        // Here the end the target keeps was a range's ahead of the shift, and three ranges behind outnumber the one ahead.
        [Test]
        public void Given_ARangeAheadHoldingTheLastEnd_When_TheRangesBehindMovePastIt_Then_TheLastEndIsTheirs()
        {
            // Arrange
            var store = new PortalRangeStore();
            var target = new VisualElement();
            var placeholders = Siblings(5);
            store[placeholders[0]] = new PortalSlotInfo(target, 0, 30);
            store[placeholders[2]] = new PortalSlotInfo(target, 5, 1);
            store[placeholders[3]] = new PortalSlotInfo(target, 6, 1);
            store[placeholders[4]] = new PortalSlotInfo(target, 7, 18);
            store.LastEndOn(target);
            PortalSlotTracker.ShiftRangesBehind(store, target, placeholders[1], new PortalSlotInfo(target, 2, 1), 10);

            // Act
            var end = store.LastEndOn(target);

            // Assert
            Assert.That(end, Is.EqualTo(35));
        }

        // GREEN_ON_BASE(refactor): the base reads every range at each shift, so no order among them can hide one.
        // The first shift moves a range below one ahead that starts inside the changed range, and the second has to find both.
        [Test]
        public void Given_ARangeMovedBelowOneStartingInsideTheChangedRange_When_ALaterShiftReachesTheOne_Then_ItMoves()
        {
            // Arrange
            var store = new PortalRangeStore();
            var target = new VisualElement();
            var placeholders = Siblings(4);
            store[placeholders[0]] = new PortalSlotInfo(target, 3, 0);
            store[placeholders[1]] = new PortalSlotInfo(target, 5, 1);
            PortalSlotTracker.ShiftRangesBehind(store, target, placeholders[2], new PortalSlotInfo(target, 0, 4), -4);

            // Act
            PortalSlotTracker.ShiftRangesBehind(store, target, placeholders[3], new PortalSlotInfo(target, 1, 1), 5);

            // Assert
            Assert.That(store[placeholders[0]].SlotStart, Is.EqualTo(8));
        }

        // GREEN_ON_BASE(refactor): the base decides each empty range at the changed one's slot by tree order, as this keeps.
        [Test]
        public void Given_EmptyRangesAtAnEmptyChangedRangesSlot_When_TheRangesBehindItMove_Then_OnlyTheOneLaterInTheTreeMoves()
        {
            // Arrange
            var store = new PortalRangeStore();
            var target = new VisualElement();
            var placeholders = Siblings(3);
            store[placeholders[0]] = new PortalSlotInfo(target, 5, 0);
            store[placeholders[2]] = new PortalSlotInfo(target, 5, 0);

            // Act
            PortalSlotTracker.ShiftRangesBehind(store, target, placeholders[1], new PortalSlotInfo(target, 5, 0), 2);

            // Assert
            Assert.That($"{store[placeholders[0]].SlotStart} {store[placeholders[2]].SlotStart}", Is.EqualTo("5 7"));
        }
    }
}
