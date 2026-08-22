using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Velvet.Tests
{
    internal sealed class VelvetTaskAwaiterInteropPlayModeTests
    {
        const int ResumeFrameBudget = 30;

        bool _resumed;
        int _resumedThreadId;
        int _resumedResult;

        [UnityTest]
        public IEnumerator Given_AsyncVelvetTask_When_ItAwaitsAUnityAwaitable_Then_ItSuspendsAndResumes()
        {
            // Arrange
            _resumed = false;
            AwaitUnityAwaitable().Forget();
            var resumedBeforeAnyFrame = _resumed;

            // Act
            yield return null;
            yield return null;

            // Assert
            Assert.That((resumedBeforeAnyFrame, _resumed), Is.EqualTo((false, true)));
        }

        [UnityTest]
        public IEnumerator Given_AsyncVelvetTask_When_ItAwaitsABclTask_Then_ItSuspendsAndResumes()
        {
            // Arrange
            var gate = new TaskCompletionSource<bool>();
            _resumed = false;
            AwaitBclTask(gate.Task).Forget();
            var resumedBeforeCompletion = _resumed;

            // Act
            gate.SetResult(true);
            yield return null;
            yield return null;

            // Assert
            Assert.That((resumedBeforeCompletion, _resumed), Is.EqualTo((false, true)));
        }

        [UnityTest]
        public IEnumerator Given_AsyncVelvetTaskAwaitingABclTask_When_ThatTaskCompletesOffTheMainThread_Then_ItResumesOnTheMainThread()
        {
            // Arrange
            var gate = new TaskCompletionSource<bool>();
            var mainThreadId = Thread.CurrentThread.ManagedThreadId;
            _resumedThreadId = 0;
            AwaitBclTaskRecordingThread(gate.Task).Forget();

            // Act
            Task.Run(() => gate.SetResult(true));
            for (var frame = 0; frame < ResumeFrameBudget && _resumedThreadId == 0; frame++)
            {
                yield return null;
            }

            // Assert
            Assert.That(_resumedThreadId, Is.EqualTo(mainThreadId));
        }

        [UnityTest]
        public IEnumerator Given_AsyncVelvetTaskAwaitingABclTaskWithTheContextSuppressed_When_ThatTaskCompletesOffTheMainThread_Then_ItResumesOffTheMainThread()
        {
            // Arrange
            var gate = new TaskCompletionSource<bool>();
            var mainThreadId = Thread.CurrentThread.ManagedThreadId;
            _resumedThreadId = 0;
            AwaitBclTaskOffContextRecordingThread(gate.Task).Forget();

            // Act
            Task.Run(() => gate.SetResult(true));
            for (var frame = 0; frame < ResumeFrameBudget && _resumedThreadId == 0; frame++)
            {
                yield return null;
            }

            // Assert
            Assert.That((_resumedThreadId != 0, _resumedThreadId == mainThreadId), Is.EqualTo((true, false)));
        }

        [UnityTest]
        public IEnumerator Given_AnAsyncVelvetTaskReturningAValue_When_ItAwaitsABclTask_Then_ItSuspendsAndResumes()
        {
            // Arrange
            var gate = new TaskCompletionSource<int>();
            _resumedResult = 0;
            AwaitBclTaskForResult(gate.Task).Forget();
            var resultBeforeCompletion = _resumedResult;

            // Act
            gate.SetResult(7);
            yield return null;
            yield return null;

            // Assert
            Assert.That((resultBeforeCompletion, _resumedResult), Is.EqualTo((0, 7)));
        }

        async VelvetTask AwaitUnityAwaitable()
        {
            await Awaitable.NextFrameAsync();
            _resumed = true;
        }

        async VelvetTask AwaitBclTask(Task gate)
        {
            await gate;
            _resumed = true;
        }

        async VelvetTask AwaitBclTaskRecordingThread(Task gate)
        {
            await gate;
            _resumedThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        async VelvetTask AwaitBclTaskOffContextRecordingThread(Task gate)
        {
            await gate.ConfigureAwait(false);
            _resumedThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        async VelvetTask<int> AwaitBclTaskForResult(Task<int> gate)
        {
            _resumedResult = await gate;
            return _resumedResult;
        }
    }
}
