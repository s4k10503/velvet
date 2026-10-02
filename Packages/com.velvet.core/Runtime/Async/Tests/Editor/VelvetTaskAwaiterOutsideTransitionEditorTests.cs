using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins what a completed task's awaiter does with a continuation it is handed while no transition scope is
    /// open: it runs the continuation at once, since only an open scope holds one back.
    /// </summary>
    [TestFixture]
    internal sealed class VelvetTaskAwaiterOutsideTransitionEditorTests
    {
        // GREEN_ON_BASE(characterization): the base ran a completed task's continuation at once in every context.
        // What this pins is that the deferral an open transition scope asks for does not reach one outside it.
        [Test]
        public void Given_ACompletedTaskOutsideATransitionScope_When_ItsAwaiterIsHandedAContinuation_Then_TheContinuationRunsAtOnce()
        {
            // Arrange
            var completed = VelvetTask.FromResult(7);
            var ran = false;

            // Act
            completed.GetAwaiter().OnCompleted(() => ran = true);

            // Assert
            Assert.That(ran, Is.True, "Only an open transition scope holds back a completed task's continuation");
        }
    }
}
