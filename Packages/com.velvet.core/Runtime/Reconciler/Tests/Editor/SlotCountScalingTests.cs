using System;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that <see cref="LogicalChildSlots.Count"/>, <see cref="LogicalChildSlots.ToLogical"/>,
    /// <see cref="LogicalChildSlots.ToPhysical"/>, <see cref="LogicalChildSlots.TryGetPhysical"/> and the read of a
    /// container's old rows (<see cref="OldSlotOccupants"/>) make a number of contentContainer calls on their
    /// container that does not grow with the number of children: each is answered from the container's row model
    /// rather than by walking its child list.
    /// </summary>
    /// <remarks>
    /// Diverges from <see cref="PatchLoopSlotLookupScalingTests"/>, whose reads grow with the list by design and
    /// whose bound separates linear from quadratic: one call here has a constant cost, so the bound separates
    /// constant from linear. Each case asks a different method, so a cut to one leaves the others green.
    /// </remarks>
    [TestFixture]
    internal sealed class SlotCountScalingTests
    {
        // The case's NaN gate is what holds that the call's reads of the box reach this override at all.
        private sealed class CountingBox : VisualElement
        {
            public int Reads;

            public override VisualElement contentContainer
            {
                get
                {
                    Reads++;
                    return this;
                }
            }
        }

        private static int BoxReadsDuring(int rows, Func<CountingBox, Reconciler, int> call)
        {
            var box = new CountingBox();
            var labels = new VNode[rows];
            for (var i = 0; i < rows; i++)
            {
                labels[i] = V.Label(text: "row " + i);
            }
            using var reconciler = new Reconciler();
            reconciler.Reconcile(box, Array.Empty<VNode>(), labels);
            box.Reads = 0;
            var answer = call(box, reconciler);
            return answer == rows / 2 || answer == rows ? box.Reads : -1;
        }

        private static double GrowthFromTo(int from, int to, Func<CountingBox, Reconciler, int> call)
        {
            var readsAtFrom = BoxReadsDuring(from, call);
            var readsAtTo = BoxReadsDuring(to, call);
            return readsAtFrom > 0 && readsAtTo > 0 ? (double)readsAtTo / readsAtFrom : double.NaN;
        }

        // GREEN_ON_BASE(characterization): the base already reads Count from the row model, which an earlier change moved
        // there. Scanning the live child list again for Count turns it red.
        [Test]
        public void Given_ALabelListInOneContainer_When_ItsCountIsAskedAtEightTimesTheLength_Then_TheContainerReadsGrowLessThanOneAndAHalfTimes()
        {
            // Arrange
            Func<CountingBox, Reconciler, int> count = (box, _) => LogicalChildSlots.Count(box);

            // Act
            var growth = GrowthFromTo(250, 2000, count);

            // Assert — eight times the children gives eight times a linear count and one times a constant one;
            // the bound sits between. A call that read nothing, or answered wrongly, leaves no ratio, so it fails.
            Assert.That(growth, Is.LessThan(1.5));
        }

        // GREEN_ON_BASE(characterization): the base already reads ToLogical from the row model, which an earlier change moved
        // there. Scanning the live child list again for ToLogical turns it red.
        [Test]
        public void Given_ALabelListInOneContainer_When_ALogicalIndexIsAskedAtEightTimesTheLength_Then_TheContainerReadsGrowLessThanOneAndAHalfTimes()
        {
            // Arrange
            Func<CountingBox, Reconciler, int> toLogical = (box, _) => LogicalChildSlots.ToLogical(box, box.hierarchy.childCount / 2);

            // Act
            var growth = GrowthFromTo(250, 2000, toLogical);

            // Assert — as for the count: a call that read nothing, or answered wrongly, leaves no ratio.
            Assert.That(growth, Is.LessThan(1.5));
        }

        [Test]
        public void Given_ALabelListInOneContainer_When_ThePhysicalIndexOfTheMiddleSlotIsAskedAtEightTimesTheLength_Then_TheContainerReadsGrowLessThanOneAndAHalfTimes()
        {
            // Arrange
            Func<CountingBox, Reconciler, int> toPhysical = (box, _) =>
                LogicalChildSlots.ToPhysical(box, box.hierarchy.childCount / 2);

            // Act
            var growth = GrowthFromTo(250, 2000, toPhysical);

            // Assert — as for the count: a call that read nothing, or answered wrongly, leaves no ratio.
            Assert.That(growth, Is.LessThan(1.5));
        }

        [Test]
        public void Given_ALabelListInOneContainer_When_TheMiddleSlotIsLookedUpAtEightTimesTheLength_Then_TheContainerReadsGrowLessThanOneAndAHalfTimes()
        {
            // Arrange
            Func<CountingBox, Reconciler, int> tryGetPhysical = (box, _) =>
                LogicalChildSlots.TryGetPhysical(box, box.hierarchy.childCount / 2, out var physical) ? physical : -1;

            // Act
            var growth = GrowthFromTo(250, 2000, tryGetPhysical);

            // Assert — as for the count: a call that read nothing, or answered wrongly, leaves no ratio.
            Assert.That(growth, Is.LessThan(1.5));
        }

        [Test]
        public void Given_ALabelListInOneContainer_When_TenOldRowsAreReadFromTheMiddleAtEightTimesTheLength_Then_TheContainerReadsGrowLessThanOneAndAHalfTimes()
        {
            // Arrange
            Func<CountingBox, Reconciler, int> readRows = (box, reconciler) =>
            {
                var middle = box.hierarchy.childCount / 2;
                var occupants = new OldSlotOccupants();
                occupants.Resolve(box, middle, 10, int.MaxValue, new FiberNodePatcher(reconciler.Context));
                return occupants.Occupant(0) is Label { text: var text } && text == "row " + middle ? middle : -1;
            };

            // Act
            var growth = GrowthFromTo(250, 2000, readRows);

            // Assert — as for the count: a call that read nothing, or answered wrongly, leaves no ratio.
            Assert.That(growth, Is.LessThan(1.5));
        }
    }
}
