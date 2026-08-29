using NUnit.Framework;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that matching a URL costs the same whether the tree has one branch or many. `Match`
    /// walks the ranked branches until one succeeds, so anything a probe allocates is allocated again
    /// for every branch it did not take.
    /// </summary>
    [TestFixture]
    [Category("Performance")]
    internal sealed class RouteMatchAllocationTests
    {
        private static RouteDefinition Leaf(string path) => new() { Path = path };

        private static RouteTree Tree(int leaves)
        {
            var children = new RouteDefinition[leaves];
            for (var index = 0; index < leaves; index++)
            {
                children[index] = Leaf("branch-" + index);
            }
            return new RouteTree(new[] { new RouteDefinition { Path = "/", Children = children } });
        }

        private static int Blocks(RouteTree tree, string url)
        {
            void Once() => tree.Match(url);
            for (var i = 0; i < 64; i++)
            {
                Once();
            }
            return GCAllocationProbe.SampleBlocksDuring(Once);
        }

        [Test]
        public void Given_ATreeWhoseLastBranchMatches_When_MatchedAgainstOneWhoseFirstDoes_Then_TheCostIsTheSame()
        {
            // Arrange — the same tree, matched at its first leaf and at its eighth. The second walks
            // seven branches that do not match before the one that does.
            var tree = Tree(8);

            // Act
            var first = Blocks(tree, "/branch-0");
            var last = Blocks(tree, "/branch-7");

            // Assert — the first count rides along, because two equal numbers say nothing if the probe
            // measured nothing at all.
            Assert.That((first > 0, last), Is.EqualTo((true, first)));
        }
    }
}
