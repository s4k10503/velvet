using System;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;

namespace Velvet.Tests
{
    [TestFixture]
    internal sealed class VelvetTaskCrossThreadCompletionEditorTests
    {
        const int Iterations = 2000;

        static readonly TimeSpan StallBound = TimeSpan.FromSeconds(5);

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
                    if (!step.SignalAndWait(StallBound))
                    {
                        return;
                    }

                    sources[i].TrySetException(new System.InvalidOperationException("fault"));
                }
            });
            var swallowed = 0;
            var stalled = false;

            // Act
            worker.Start();
            for (var i = 0; i < Iterations && !stalled; i++)
            {
                step.SignalAndWait(StallBound);
                var task = sources[i].Task;
                var waited = Stopwatch.StartNew();
                while (!task.Status.IsCompleted() && !stalled)
                {
                    stalled = waited.Elapsed > StallBound;
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

            worker.Join(StallBound);

            // Assert
            Assert.That((stalled, swallowed), Is.EqualTo((false, 0)));
        }
    }
}
