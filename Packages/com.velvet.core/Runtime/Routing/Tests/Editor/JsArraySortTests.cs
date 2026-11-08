using System;
using System.Linq;
using NUnit.Framework;
using Velvet;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies <c>JsArraySort</c> as V8 14.0's <c>Array.prototype.sort</c>: below eight elements one binary
    /// insertion sort, from eight on TimSort, whose first step looks for a natural run. A comparator that
    /// contradicts itself is what tells the two apart.
    /// </summary>
    [TestFixture]
    internal sealed class JsArraySortTests
    {
        // Calls 3 less than 1 and 1 greater than 3, and every other pair equal.
        private static int Contradicting(int x, int y) => x == 3 && y == 1 ? -1 : x == 1 && y == 3 ? 1 : 0;

        private static string Sorted(int length)
        {
            var list = Enumerable.Range(0, length).ToList();
            JsArraySort.Sort(list, Contradicting);
            return string.Join(",", list);
        }

        [Test]
        public void Given_FourElements_When_Sorted_Then_BinaryInsertionPlacesEachByItsMidpointComparisons()
        {
            // Arrange / Act — inserting 3 compares it with 1 first and moves it in front.
            var sorted = Sorted(4);

            // Assert
            Assert.That(sorted, Is.EqualTo("0,3,1,2"));
        }

        [Test]
        public void Given_EightElements_When_Sorted_Then_TheNaturalRunTimSortFindsIsKept()
        {
            // Arrange / Act — the run 0..7 reads as ascending pair by pair, so TimSort leaves it whole.
            var sorted = Sorted(8);

            // Assert
            Assert.That(sorted, Is.EqualTo("0,1,2,3,4,5,6,7"));
        }
    }
}
