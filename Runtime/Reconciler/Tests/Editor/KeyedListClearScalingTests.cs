using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a parent re-render which removes a whole list of inline components does work on the
    /// container proportional to the list, not to its square.
    /// <list type="bullet">
    /// <item>Each removed row is found at its slot from the one removed before it rather than from the first
    /// child, which the container's child reads count.</item>
    /// <item>The fibers a removed row holds are selected from the row's own subtree rather than by testing every
    /// registered fiber against it, which the hashes of the container, the mount point of every row's fiber,
    /// count.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class KeyedListClearScalingTests
    {
        private sealed class CountingBox : VisualElement
        {
            public int Reads;
            public int Hashes;

            public override VisualElement contentContainer
            {
                get
                {
                    Reads++;
                    return this;
                }
            }

            public override int GetHashCode()
            {
                Hashes++;
                return base.GetHashCode();
            }
        }

        private static int s_rows;
        private static StateUpdater<int> s_setTick;

        [Component(Compiler = false)]
        private static VNode List()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            var children = new VNode[s_rows];
            for (var i = 0; i < s_rows; i++) children[i] = V.Component(Row, i, key: "k" + i);
            return V.Custom<CountingBox>(name: "box", children: children);
        }

        [Component(Compiler = false)]
        private static VNode Row(int i) => V.Label(name: "r" + i, key: "r");

        private static (int Reads, int Hashes) CountsWhileTheListIsCleared(int rows)
        {
            s_rows = rows;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(List));
            var box = root.Q<CountingBox>("box");
            box.Reads = 0;
            box.Hashes = 0;
            s_rows = 0;
            s_setTick.Invoke(t => t + 1);
            mounted.FlushStateForTest();
            return (box.Reads, box.Hashes);
        }

        // A pass that counted nothing leaves no ratio, so it fails rather than passing as linear.
        private static double Growth(int atN, int at2N) => atN > 0 ? (double)at2N / atN : double.NaN;

        [Test]
        public void Given_AListOfInlineComponents_When_ItIsClearedAtTwiceTheLength_Then_TheContainerReadsGrowLessThanTwoAndAHalfTimes()
        {
            // Arrange
            var atN = CountsWhileTheListIsCleared(100).Reads;

            // Act
            var at2N = CountsWhileTheListIsCleared(200).Reads;

            // Assert — doubling the list doubles a linear count and quadruples a quadratic one; the bound sits between.
            Assert.That(Growth(atN, at2N), Is.LessThan(2.5));
        }

        [Test]
        public void Given_AListOfInlineComponents_When_ItIsClearedAtTwiceTheLength_Then_TheContainerIsHashedLessThanTwoAndAHalfTimesAsOften()
        {
            // Arrange
            var atN = CountsWhileTheListIsCleared(100).Hashes;

            // Act
            var at2N = CountsWhileTheListIsCleared(200).Hashes;

            // Assert — same bound as above: a registry scan per removed row hashes the container once per fiber.
            Assert.That(Growth(atN, at2N), Is.LessThan(2.5));
        }
    }
}
