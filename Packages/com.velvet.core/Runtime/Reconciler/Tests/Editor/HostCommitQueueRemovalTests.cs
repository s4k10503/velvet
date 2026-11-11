using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that <see cref="HostCommitQueue.Remove"/> takes the element it names out of the parent wherever the
    /// element now stands relative to the index it is searched from — at it, below it, above it, or with that index
    /// past the last child — and takes nothing out where the element is not a child.
    /// </summary>
    [TestFixture]
    internal sealed class HostCommitQueueRemovalTests
    {
        private Reconciler _reconciler = null!;

        private HostCommitQueue Host => _reconciler.Context.HostCommit;

        [SetUp]
        public void SetUp() => _reconciler = new Reconciler();

        [TearDown]
        public void TearDown() => _reconciler.Dispose();

        private static VisualElement ParentOf(params string[] names)
        {
            var parent = new VisualElement();
            foreach (var name in names) parent.Add(new VisualElement { name = name });
            return parent;
        }

        private static string Names(VisualElement parent)
        {
            var names = new string[parent.childCount];
            for (var i = 0; i < names.Length; i++) names[i] = parent[i].name;
            return string.Join(",", names);
        }

        [Test]
        public void Given_AChildAtTheIndexItIsSearchedFrom_When_Removed_Then_ItIsTheOneThatLeaves()
        {
            // Arrange
            var parent = ParentOf("a", "b", "c");

            // Act
            Host.Remove(parent, parent[1], near: 1);

            // Assert
            Assert.That(Names(parent), Is.EqualTo("a,c"));
        }

        [Test]
        public void Given_TheFirstChildSearchedForFromTheLast_When_Removed_Then_ItIsTheOneThatLeaves()
        {
            // Arrange
            var parent = ParentOf("a", "b", "c", "d");

            // Act
            Host.Remove(parent, parent[0], near: 3);

            // Assert
            Assert.That(Names(parent), Is.EqualTo("b,c,d"));
        }

        [Test]
        public void Given_TheLastChildSearchedForFromTheFirst_When_Removed_Then_ItIsTheOneThatLeaves()
        {
            // Arrange
            var parent = ParentOf("a", "b", "c", "d");

            // Act
            Host.Remove(parent, parent[3], near: 0);

            // Assert
            Assert.That(Names(parent), Is.EqualTo("a,b,c"));
        }

        [Test]
        public void Given_AnIndexPastTheLastChild_When_AChildIsRemovedSearchingFromIt_Then_ItIsTheOneThatLeaves()
        {
            // Arrange
            var parent = ParentOf("a", "b", "c");

            // Act
            Host.Remove(parent, parent[1], near: 10);

            // Assert
            Assert.That(Names(parent), Is.EqualTo("a,c"));
        }

        [Test]
        public void Given_AnElementThatIsNotAChild_When_Removed_Then_EveryChildStays()
        {
            // Arrange
            var parent = ParentOf("a", "b", "c");

            // Act
            Host.Remove(parent, new VisualElement { name = "x" }, near: 1);

            // Assert
            Assert.That(Names(parent), Is.EqualTo("a,b,c"));
        }

        [Test]
        public void Given_AParentWithNoChildren_When_AnElementIsRemovedFromIt_Then_NothingThrows()
        {
            // Arrange
            var parent = new VisualElement();

            // Act + Assert
            Assert.That(() => Host.Remove(parent, new VisualElement(), near: 0), Throws.Nothing);
        }
    }
}
