using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies that <see cref="ReconcilerBufferPool.PostOrderBuffers{T}.Clear"/> leaves every buffer a walk fills
    /// empty, so a walk that stopped before it emptied its own work stack does not hand the next walk its leftovers.
    /// </summary>
    [TestFixture]
    internal sealed class PostOrderBuffersTests
    {
        // GREEN_ON_BASE(characterization): the work stack is emptied with the other buffers when they are cleared.
        [Test]
        public void Given_AWalkThatLeftWorkOnTheStack_When_TheBuffersAreCleared_Then_NoWorkRemains()
        {
            // Arrange
            var buffers = new ReconcilerBufferPool.PostOrderBuffers<int>();
            buffers.Work.Push((0, 7));

            // Act
            buffers.Clear();

            // Assert
            Assert.That(buffers.Work, Is.Empty);
        }
    }
}
