using System.Linq;
using NUnit.Framework;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class StyleCandidateOrderTests
    {
        // The order Tailwind 4.3.3 emits these utilities in.
        private static readonly string[] s_tailwindOrder =
        {
            "flex-0", "flex-0/2", "flex-1", "flex-1/2", "flex-1/3", "flex-2", "flex-3/2", "flex-9", "flex-10",
            "flex-12", "flex-[0]", "flex-[01]", "flex-[1.5]", "flex-[1_1_0]", "flex-[2]", "flex-[auto]",
            "flex-auto", "flex-initial", "flex-none",
        };

        [Test]
        public void Given_TailwindsFlexOrder_When_EachNeighbourIsCompared_Then_EachSortsBeforeTheNext()
        {
            // Act — the sign of each comparison with the next candidate, which a tie reports as 0.
            var signs = Enumerable.Range(0, s_tailwindOrder.Length - 1)
                .Select(i => System.Math.Sign(StyleCandidateOrder.Compare(s_tailwindOrder[i], s_tailwindOrder[i + 1])));

            // Assert
            Assert.That(string.Join(" ", signs), Is.EqualTo(string.Join(" ", Enumerable.Repeat(-1, s_tailwindOrder.Length - 1))));
        }
    }
}
