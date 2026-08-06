using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Velvet.Tests
{
    internal sealed class AwaitableSecondConsumePlayModeTests
    {
        [UnityTest]
        public IEnumerator Given_CompletedAwaitableCompletionSourceInt_When_AwaitedOnce_Then_ReturnsSetValue()
        {
            // Arrange
            var source = new AwaitableCompletionSource<int>();
            source.SetResult(5);
            var awaitable = source.Awaitable;
            var result = 0;

            // Act
            yield return AwaitToCoroutine(awaitable, value => result = value);

            // Assert
            Assert.That(result, Is.EqualTo(5));
        }

        [UnityTest]
        public IEnumerator Given_ConsumedAwaitableCompletionSourceInt_When_SecondAwaitSameFrame_Then_ThrowsNullReferenceException()
        {
            // Arrange
            var source = new AwaitableCompletionSource<int>();
            source.SetResult(5);
            var awaitable = source.Awaitable;
            yield return AwaitToCoroutine(awaitable, _ => { });
            Exception? caught = null;

            // Act
            yield return AwaitToCoroutine(awaitable, _ => { }, ex => caught = ex);

            // Assert
            Assert.That(caught, Is.TypeOf<NullReferenceException>());
        }

        [UnityTest]
        public IEnumerator Given_CompletedNextFrameAwaitable_When_AwaitedOnce_Then_Completes()
        {
            // Arrange
            var awaitable = Awaitable.NextFrameAsync();
            var completed = false;

            // Act
            yield return AwaitToCoroutine(awaitable, () => completed = true);

            // Assert
            Assert.That(completed, Is.True);
        }

        [UnityTest]
        public IEnumerator Given_ConsumedNextFrameAwaitable_When_SecondGetResultSameFrame_Then_ThrowsInvalidOperationException()
        {
            // Arrange
            var awaitable = Awaitable.NextFrameAsync();
            yield return AwaitToCoroutine(awaitable, () => { });
            Exception? caught = null;

            // Act
            try
            {
                awaitable.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            // Assert
            Assert.That(caught, Is.TypeOf<InvalidOperationException>());
        }

        [UnityTest]
        public IEnumerator Given_ConsumedNextFrameAwaitable_When_SecondGetResultNextFrame_Then_ThrowsInvalidOperationException()
        {
            // Arrange
            var awaitable = Awaitable.NextFrameAsync();
            yield return AwaitToCoroutine(awaitable, () => { });
            yield return null;
            Exception? caught = null;

            // Act
            try
            {
                awaitable.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            // Assert
            Assert.That(caught, Is.TypeOf<InvalidOperationException>());
        }

        [UnityTest]
        public IEnumerator Given_ConsumedNextFrameAwaitable_When_SecondAwaitNextFrame_Then_ThrowsInvalidOperationException()
        {
            // Arrange
            var awaitable = Awaitable.NextFrameAsync();
            yield return AwaitToCoroutine(awaitable, () => { });
            yield return null;
            Exception? caught = null;

            // Act
            yield return AwaitToCoroutine(awaitable, () => { }, ex => caught = ex);

            // Assert
            Assert.That(caught, Is.TypeOf<InvalidOperationException>());
        }

        [UnityTest]
        public IEnumerator Given_EndOfFrameAwaitable_When_ThreeFramesYielded_Then_IsNotCompleted()
        {
            // Arrange
            var awaitable = Awaitable.EndOfFrameAsync();
            yield return null;
            yield return null;
            yield return null;

            // Act
            var isCompleted = awaitable.GetAwaiter().IsCompleted;

            // Assert
            Assert.That(isCompleted, Is.False);
        }

        [UnityTest]
        public IEnumerator Given_EndOfFrameAwaitable_When_GetResultWithoutWaiting_Then_DoesNotThrow()
        {
            // Arrange
            var awaitable = Awaitable.EndOfFrameAsync();
            Exception? caught = null;

            // Act
            try
            {
                awaitable.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            // Assert
            Assert.That(caught, Is.Null);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Given_ConsumedAwaitableCompletionSourceInt_When_IntermediateNextFrameAllocatedBeforeSecondGetResult_Then_ThrowsNullReferenceException()
        {
            // Arrange
            var source = new AwaitableCompletionSource<int>();
            source.SetResult(5);
            var awaitable = source.Awaitable;
            awaitable.GetAwaiter().GetResult();
            GC.KeepAlive(Awaitable.NextFrameAsync());
            Exception? caught = null;

            // Act
            try
            {
                awaitable.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            // Assert
            Assert.That(caught, Is.TypeOf<NullReferenceException>());
            yield return null;
        }

        [UnityTest]
        public IEnumerator Given_CompletedNextFrameAwaitable_When_PeekedThenFirstGetResult_Then_CompletesWithoutThrow()
        {
            // Arrange
            var awaitable = Awaitable.NextFrameAsync();
            yield return null;
            Assume.That(awaitable.GetAwaiter().IsCompleted, Is.True);
            Exception? caught = null;

            // Act
            try
            {
                awaitable.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            // Assert
            Assert.That(caught, Is.Null);
        }

        static IEnumerator AwaitToCoroutine(Awaitable awaitable, Action onSuccess, Action<Exception>? onError = null)
        {
            var awaiter = awaitable.GetAwaiter();
            bool isCompleted;
            try
            {
                isCompleted = awaiter.IsCompleted;
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
                yield break;
            }

            if (!isCompleted)
            {
                var done = false;
                awaiter.OnCompleted(() => done = true);
                while (!done)
                {
                    yield return null;
                }
            }

            try
            {
                awaiter.GetResult();
                onSuccess();
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
            }
        }

        static IEnumerator AwaitToCoroutine(Awaitable<int> awaitable, Action<int> onSuccess, Action<Exception>? onError = null)
        {
            var awaiter = awaitable.GetAwaiter();
            bool isCompleted;
            try
            {
                isCompleted = awaiter.IsCompleted;
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
                yield break;
            }

            if (!isCompleted)
            {
                var done = false;
                awaiter.OnCompleted(() => done = true);
                while (!done)
                {
                    yield return null;
                }
            }

            try
            {
                onSuccess(awaiter.GetResult());
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
            }
        }
    }
}
