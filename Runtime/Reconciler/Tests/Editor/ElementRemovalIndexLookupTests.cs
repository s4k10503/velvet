using NUnit.Framework;
using UnityEngine.UIElements;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that removing a list's rows does not search the container for each row's index: the index a
    /// removal found its row at is the one it takes the row out of. The count is the equality checks made on the
    /// rows themselves, which a search of the container's children makes once per child it passes.
    /// </summary>
    [TestFixture]
    internal sealed class ElementRemovalIndexLookupTests
    {
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
        private static StateUpdater<int> s_setTick;

        [Component(Compiler = false)]
        private static VNode List()
        {
            var (_, setTick) = Hooks.UseState(0);
            s_setTick = setTick;
            var children = new VNode[s_rows];
            for (var i = 0; i < s_rows; i++) children[i] = V.Component(Row, i, key: "k" + i);
            return V.Custom<VisualElement>(name: "box", children: children);
        }

        [Component(Compiler = false)]
        private static VNode Row(int i) => V.Custom<CountingRow>(name: "r" + i);

        private static int EqualsCallsWhileTheListIsCleared(int rows)
        {
            s_rows = rows;
            var root = new VisualElement();
            using var mounted = V.Mount(root, V.Component(List));
            CountingRow.EqualsCalls = 0;
            s_rows = 0;
            s_setTick.Invoke(t => t + 1);
            mounted.FlushStateForTest();
            return CountingRow.EqualsCalls;
        }

        // GREEN_ON_BASE(characterization): the instrument for the case below: IndexOf on the base reaches the rows' Equals.
        [Test]
        public void Given_ElementsInAParent_When_OneIsLookedUpByIndexOf_Then_TheLookupChecksEqualityOnTheElements()
        {
            // Arrange
            var parent = new VisualElement();
            for (var i = 0; i < 50; i++) parent.Add(new CountingRow());
            var last = parent[49];
            CountingRow.EqualsCalls = 0;

            // Act
            parent.IndexOf(last);

            // Assert
            Assert.That(CountingRow.EqualsCalls, Is.GreaterThan(0));
        }

        [Test]
        public void Given_AListOfInlineComponents_When_ItIsClearedAtTwiceTheLength_Then_TheRowsAreCheckedForEqualityLessThanTwoAndAHalfTimesAsOften()
        {
            // Arrange
            var atN = EqualsCallsWhileTheListIsCleared(200);

            // Act
            var at2N = EqualsCallsWhileTheListIsCleared(400);

            // Assert — doubling the list doubles a linear count and quadruples a quadratic one; the bound sits
            // between. A count of nothing at the shorter length is within it, and the case above is what holds
            // that the instrument reads at all.
            Assert.That(at2N, Is.LessThanOrEqualTo(2.5 * atN));
        }
    }
}
