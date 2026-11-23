using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the end of the last range on a target that <see cref="PortalRangeStore"/> keeps beside the ranges,
    /// which <see cref="PortalSlotTracker.RowsBehindRanges"/> reads in place of every range: it follows a range that
    /// grows, shrinks, leaves its target or is removed, and answers null for a target no range is on. Each case
    /// reads the end once before the write, so a stale answer is one the store kept rather than computed afresh.
    /// </summary>
    [TestFixture]
    internal sealed class PortalRangeStoreTests
    {
        private static PortalSlotInfo Range(VisualElement target, int start, int length) => new(target, start, length);

        private static (PortalRangeStore Store, VisualElement Target, VisualElement First, VisualElement Second) TwoRanges()
        {
            var store = new PortalRangeStore();
            var target = new VisualElement();
            var first = new VisualElement();
            var second = new VisualElement();
            store[first] = Range(target, 0, 2);
            store[second] = Range(target, 2, 3);
            return (store, target, first, second);
        }

        [Test]
        public void Given_ARangeReadOnce_When_ALaterRangeIsRecordedOnTheTarget_Then_TheLastEndIsTheLaterOnes()
        {
            // Arrange
            var (store, target, _, _) = TwoRanges();
            store.LastEndOn(target);
            store[new VisualElement()] = Range(target, 5, 4);

            // Act
            var end = store.LastEndOn(target);

            // Assert
            Assert.That(end, Is.EqualTo(9));
        }

        [Test]
        public void Given_TheLastRangeReadOnce_When_ItIsRemoved_Then_TheLastEndIsTheEarlierOnes()
        {
            // Arrange
            var (store, target, _, second) = TwoRanges();
            store.LastEndOn(target);
            store.Remove(second);

            // Act
            var end = store.LastEndOn(target);

            // Assert
            Assert.That(end, Is.EqualTo(2));
        }

        [Test]
        public void Given_TheLastRangeReadOnce_When_ItShrinks_Then_TheLastEndIsTheShorterOnes()
        {
            // Arrange
            var (store, target, _, second) = TwoRanges();
            store.LastEndOn(target);
            store[second] = Range(target, 2, 1);

            // Act
            var end = store.LastEndOn(target);

            // Assert
            Assert.That(end, Is.EqualTo(3));
        }

        [Test]
        public void Given_TheLastRangeReadOnce_When_ItMovesToAnotherTarget_Then_TheFirstTargetEndsWhereTheEarlierRangeDoes()
        {
            // Arrange
            var (store, target, _, second) = TwoRanges();
            store.LastEndOn(target);
            store[second] = Range(new VisualElement(), 2, 3);

            // Act
            var end = store.LastEndOn(target);

            // Assert
            Assert.That(end, Is.EqualTo(2));
        }

        [Test]
        public void Given_ARangeReadOnce_When_EveryRangeOnTheTargetIsRemoved_Then_NoEndIsKept()
        {
            // Arrange
            var (store, target, first, second) = TwoRanges();
            store.LastEndOn(target);
            store.Remove(first);
            store.Remove(second);

            // Act
            var end = store.LastEndOn(target);

            // Assert
            Assert.That(end, Is.Null);
        }

        [Test]
        public void Given_AnEarlierRangeReadOnce_When_ItGrowsPastTheLastOne_Then_TheLastEndIsTheGrownOnes()
        {
            // Arrange
            var (store, target, first, _) = TwoRanges();
            store.LastEndOn(target);
            store[first] = Range(target, 0, 9);

            // Act
            var end = store.LastEndOn(target);

            // Assert
            Assert.That(end, Is.EqualTo(9));
        }

        [Test]
        public void Given_TheLastRangeReadOnce_When_ItMovesToAnotherTarget_Then_ThatTargetEndsWhereTheRangeDoes()
        {
            // Arrange
            var (store, target, _, second) = TwoRanges();
            var other = new VisualElement();
            store.LastEndOn(target);
            store[second] = Range(other, 2, 3);

            // Act
            var end = store.LastEndOn(other);

            // Assert
            Assert.That(end, Is.EqualTo(5));
        }

        // The targets holding a counted range, or -1 where the store keeps no such count.
        private static int TargetsCounted(PortalRangeStore store)
            => ((IDictionary)typeof(PortalRangeStore)
                .GetField("_rangesOn", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(store))?.Count ?? -1;

        [Test]
        public void Given_RangesOnOneTarget_When_EveryRangeIsRemoved_Then_NoTargetIsCounted()
        {
            // Arrange
            var (store, _, first, second) = TwoRanges();
            store.Remove(first);
            store.Remove(second);

            // Act
            var counted = TargetsCounted(store);

            // Assert
            Assert.That(counted, Is.EqualTo(0));
        }

        [Test]
        public void Given_RangesOnOneTarget_When_OneMovesToAnotherTarget_Then_BothTargetsAreCounted()
        {
            // Arrange
            var (store, _, _, second) = TwoRanges();
            store[second] = Range(new VisualElement(), 0, 1);

            // Act
            var counted = TargetsCounted(store);

            // Assert
            Assert.That(counted, Is.EqualTo(2));
        }
    }
}
