using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that a re-render dropping every tenth row of a list of inline components does work on the
    /// container proportional to the list, not to its square: each removed row is searched for from where the
    /// container's entry found it, not from the first child.
    /// </summary>
    /// <remarks>
    /// Diverges from <see cref="KeyedListClearScalingTests"/>, whose removals each sit beside the one before: here
    /// ten rows separate them. The count adds the rows' equality checks to the container's child reads, so a search
    /// through <c>VisualElement.IndexOf</c> is counted too (<see cref="ElementRemovalIndexLookupTests"/> holds that it
    /// reaches them).
    /// </remarks>
    [TestFixture]
    internal sealed class SparseRowRemovalScalingTests
    {
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

        private sealed class CountingRow : VisualElement
        {
            public static int EqualsCalls;

            public override bool Equals(object? obj)
            {
                EqualsCalls++;
                return ReferenceEquals(this, obj);
            }

            public override int GetHashCode() => base.GetHashCode();
        }

        private static int s_rows;
        private static bool s_dropEveryTenth;
        private static StateUpdater<int> s_setTick;

        [Component(Compiler = false)]
        private static VNode List()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            var children = new System.Collections.Generic.List<VNode>();
            for (var i = 0; i < s_rows; i++)
            {
                if (s_dropEveryTenth && i % 10 == 5) continue;
                children.Add(V.Component(Row, i, key: "k" + i));
            }
            return V.Custom<CountingBox>(name: "box", children: children.ToArray());
        }

        [Component(Compiler = false)]
        private static VNode Row(int i) => V.Custom<CountingRow>(name: "r" + i);

        private static int WorkWhileEveryTenthRowIsDropped(int rows)
        {
            s_rows = rows;
            s_dropEveryTenth = false;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(List));
            var box = root.Q<CountingBox>("box");
            box.Reads = 0;
            CountingRow.EqualsCalls = 0;
            s_dropEveryTenth = true;
            s_setTick.Invoke(t => t + 1);
            mounted.FlushStateForTest();
            return box.Reads + CountingRow.EqualsCalls;
        }

        // GREEN_ON_BASE(refactor): the base found each removed row by walking slots down from the one removed above it.
        // The case holds that finding it by reference instead costs no more.
        [Test]
        public void Given_AListOfInlineComponents_When_EveryTenthRowIsDroppedAtTwiceTheLength_Then_TheWorkOnTheContainerGrowsLessThanTwoAndAHalfTimes()
        {
            // Arrange
            var atN = WorkWhileEveryTenthRowIsDropped(1000);

            // Act
            var at2N = WorkWhileEveryTenthRowIsDropped(2000);

            // Assert — doubling the list doubles a linear count and quadruples a quadratic one; the bound sits between.
            // A pass that counted nothing leaves no ratio, so it fails rather than passing as linear.
            var growth = atN > 0 ? (double)at2N / atN : double.NaN;
            Assert.That(growth, Is.LessThan(2.5));
        }
    }
}
