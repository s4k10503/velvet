using System;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that <see cref="LogicalChildSlots.Count"/> and <see cref="LogicalChildSlots.ToLogical"/> make a
    /// number of contentContainer calls on their container that does not grow with the number of children: both are
    /// answered from the container's row model rather than by walking its child list.
    /// </summary>
    /// <remarks>
    /// Diverges from <see cref="PatchLoopSlotLookupScalingTests"/>, whose reads grow with the list by design and
    /// whose bound separates linear from quadratic: one call here has a constant cost, so the bound separates
    /// constant from linear. The two cases ask different methods, so a cut to one leaves the other green.
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

        private static int BoxReadsDuring(int rows, Func<CountingBox, int> call)
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
            var answer = call(box);
            return answer == rows / 2 || answer == rows ? box.Reads : -1;
        }

        private static double GrowthFromTo(int from, int to, Func<CountingBox, int> call)
        {
            var readsAtFrom = BoxReadsDuring(from, call);
            var readsAtTo = BoxReadsDuring(to, call);
            return readsAtFrom > 0 && readsAtTo > 0 ? (double)readsAtTo / readsAtFrom : double.NaN;
        }

        [Test]
        public void Given_ALabelListInOneContainer_When_ItsCountIsAskedAtEightTimesTheLength_Then_TheContainerReadsGrowLessThanOneAndAHalfTimes()
        {
            // Arrange
            Func<CountingBox, int> count = box => LogicalChildSlots.Count(box);

            // Act
            var growth = GrowthFromTo(250, 2000, count);

            // Assert — eight times the children gives eight times a linear count and one times a constant one;
            // the bound sits between. A call that read nothing, or answered wrongly, leaves no ratio, so it fails.
            Assert.That(growth, Is.LessThan(1.5));
        }

        [Test]
        public void Given_ALabelListInOneContainer_When_ALogicalIndexIsAskedAtEightTimesTheLength_Then_TheContainerReadsGrowLessThanOneAndAHalfTimes()
        {
            // Arrange
            Func<CountingBox, int> toLogical = box => LogicalChildSlots.ToLogical(box, box.hierarchy.childCount / 2);

            // Act
            var growth = GrowthFromTo(250, 2000, toLogical);

            // Assert — as for the count: a call that read nothing, or answered wrongly, leaves no ratio.
            Assert.That(growth, Is.LessThan(1.5));
        }
    }
}
