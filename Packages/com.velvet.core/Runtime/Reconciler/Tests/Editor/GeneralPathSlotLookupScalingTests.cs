using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a parent re-render of a container holding a list of inline components reads the
    /// container's children a number of times proportional to the list, not to its square: where the order
    /// holds, each old leaf the walk takes is found from the one before it rather than from the first child.
    /// </summary>
    [TestFixture]
    internal sealed class GeneralPathSlotLookupScalingTests
    {
        // The case's NaN gate is what holds that the re-render's reads of the box reach this override at all.
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
        private static VNode Row(int i) => V.Component(Inner, i, key: "inner");

        [Component(Compiler = false)]
        private static VNode Inner(int i) => V.Label(name: "r" + i, key: "r");

        private static int BoxReadsDuringParentRerender(int rows)
        {
            s_rows = rows;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(List));
            var box = root.Q<CountingBox>("box");
            box.Reads = 0;
            s_setTick.Invoke(t => t + 1);
            mounted.FlushStateForTest();
            return box.Reads;
        }

        [Test]
        public void Given_AListOfInlineComponents_When_ItsParentReRendersAtTwiceTheLength_Then_TheContainerReadsAtMostDouble()
        {
            // Arrange
            var readsAtN = BoxReadsDuringParentRerender(100);

            // Act
            var readsAt2N = BoxReadsDuringParentRerender(200);

            // Assert — doubling the list doubles a linear count and quadruples a quadratic one; the bound sits
            // between. A pass that read nothing leaves no ratio, so it fails rather than passing as linear.
            var growth = readsAtN > 0 ? (double)readsAt2N / readsAtN : double.NaN;
            Assert.That(growth, Is.LessThan(2.5));
        }
    }
}
