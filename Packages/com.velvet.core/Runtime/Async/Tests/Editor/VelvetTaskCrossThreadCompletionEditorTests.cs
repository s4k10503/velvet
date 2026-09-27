using System.Threading;
using NUnit.Framework;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class VelvetTaskCrossThreadCompletionEditorTests
    {
        const int Iterations = 2000;

        [Test]
        public void Given_TasksFaultedOnAnotherThread_When_TheMainThreadSeesEachComplete_Then_EveryGetResultThrowsTheFault()
        {
            // Arrange
            var sources = new VelvetTaskCompletionSource[Iterations];
            for (var i = 0; i < Iterations; i++)
            {
                sources[i] = new VelvetTaskCompletionSource();
            }

            var step = new Barrier(2);
            var worker = new Thread(() =>
            {
                for (var i = 0; i < Iterations; i++)
                {
                    step.SignalAndWait();
                    sources[i].TrySetException(new System.InvalidOperationException("fault"));
                }
            });
            var swallowed = 0;

            // Act
            worker.Start();
            for (var i = 0; i < Iterations; i++)
            {
                step.SignalAndWait();
                var task = sources[i].Task;
                while (!task.Status.IsCompleted())
                {
                }

                try
                {
                    task.GetAwaiter().GetResult();
                    swallowed++;
                }
                catch (System.InvalidOperationException)
                {
                }
            }

            worker.Join();

            // Assert
            Assert.That(swallowed, Is.Zero);
        }
    }
}
