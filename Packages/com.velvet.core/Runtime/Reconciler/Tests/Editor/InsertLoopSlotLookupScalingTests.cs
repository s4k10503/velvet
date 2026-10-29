using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a container whose children grow from none to a list makes a number of contentContainer
    /// calls proportional to the list, not to its square: each insert of the loop that adds the rows is placed
    /// from the one before it rather than from the first child. One case per kind of list, since the keyed and
    /// the indexed diff add their rows in loops of their own.
    /// </summary>
    /// <remarks>
    /// Diverges from <see cref="GeneralPathSlotLookupScalingTests"/>, whose rows are inline components that the
    /// general path walks: the rows here are plain children of the box, and the list grows within one render
    /// instead of being rendered again at the same length.
    /// </remarks>
    [TestFixture]
    internal sealed class InsertLoopSlotLookupScalingTests
    {
        // The case's NaN gate is what holds that the growing render's reads of the box reach this override at all.
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

        private static int s_rows;
        private static bool s_keyed;
        private static StateUpdater<int> s_setCount;

        [Component(Compiler = false)]
        private static VNode GrowingList()
        {
            var (count, setCount) = Hooks.UseState(0);
            s_setCount = setCount;
            var children = new List<VNode>();
            for (var i = 0; i < count; i++)
            {
                children.Add(s_keyed ? V.Label(name: "r" + i, key: "k" + i) : V.Label(name: "r" + i));
            }
            return V.Custom<CountingBox>(name: "box", children: children.ToArray());
        }

        private static int BoxReadsWhileGrowingTo(int rows, bool keyed)
        {
            s_rows = rows;
            s_keyed = keyed;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(GrowingList));
            mounted.FlushStateForTest();
            var box = root.Q<CountingBox>("box");
            box.Reads = 0;
            s_setCount.Invoke(s_rows);
            mounted.FlushStateForTest();
            return box.Reads;
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Given_AnEmptyContainer_When_ItsListGrowsToTwiceTheLength_Then_TheContainerReadsGrowLessThanTwoAndAHalfTimes(bool keyed)
        {
            // Arrange
            var readsAtN = BoxReadsWhileGrowingTo(100, keyed);

            // Act
            var readsAt2N = BoxReadsWhileGrowingTo(200, keyed);

            // Assert — doubling the list doubles a linear count and quadruples a quadratic one; the bound sits
            // between. A pass that read nothing leaves no ratio, so it fails rather than passing as linear.
            var growth = readsAtN > 0 ? (double)readsAt2N / readsAtN : double.NaN;
            Assert.That(growth, Is.LessThan(2.5));
        }
    }
}
