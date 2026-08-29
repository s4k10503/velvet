using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Velvet.Tests
{
    internal sealed class AwaitableSecondConsumeEditorTests
    {
        [Test]
        public void Given_CompletedAwaitableCompletionSourceInt_When_FirstGetResult_Then_ReturnsSetValue()
        {
            // Arrange
            var source = new AwaitableCompletionSource<int>();
            source.SetResult(5);
            var awaitable = source.Awaitable;

            // Act
            var result = awaitable.GetAwaiter().GetResult();

            // Assert
            Assert.That(result, Is.EqualTo(5));
        }

        [Test]
        public void Given_CompletedAwaitableCompletionSourceInt_When_IsCompletedPeekedBeforeFirstGetResult_Then_IsTrue()
        {
            // Arrange
            var source = new AwaitableCompletionSource<int>();
            source.SetResult(5);
            var awaitable = source.Awaitable;

            // Act
            var isCompleted = awaitable.GetAwaiter().IsCompleted;

            // Assert
            Assert.That(isCompleted, Is.True);
        }

        [Test]
        public void Given_CompletedAwaitableCompletionSourceInt_When_PeekedThenFirstGetResult_Then_ReturnsSetValue()
        {
            // Arrange
            var source = new AwaitableCompletionSource<int>();
            source.SetResult(5);
            var awaitable = source.Awaitable;
            Assume.That(awaitable.GetAwaiter().IsCompleted, Is.True);

            // Act
            var result = awaitable.GetAwaiter().GetResult();

            // Assert
            Assert.That(result, Is.EqualTo(5));
        }

        [Test]
        public void Given_ConsumedAwaitableCompletionSourceInt_When_SecondGetResultSameFrame_Then_ThrowsNullReferenceException()
        {
            // Arrange
            var source = new AwaitableCompletionSource<int>();
            source.SetResult(5);
            var awaitable = source.Awaitable;
            awaitable.GetAwaiter().GetResult();

            // Act
            Exception? caught = null;
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
        }

        [Test]
        public void Given_ConsumedAwaitableCompletionSourceInt_When_SecondIsCompletedPeekSameFrame_Then_ThrowsNullReferenceException()
        {
            // Arrange
            var source = new AwaitableCompletionSource<int>();
            source.SetResult(5);
            var awaitable = source.Awaitable;
            awaitable.GetAwaiter().GetResult();

            // Act
            Exception? caught = null;
            try
            {
                _ = awaitable.GetAwaiter().IsCompleted;
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            // Assert
            Assert.That(caught, Is.TypeOf<NullReferenceException>());
        }

        [Test]
        public void Given_CompletedAwaitableCompletionSourceVoid_When_FirstGetResult_Then_DoesNotThrow()
        {
            // Arrange
            var source = new AwaitableCompletionSource();
            source.SetResult();
            var awaitable = source.Awaitable;

            // Act
            void Act() => awaitable.GetAwaiter().GetResult();

            // Assert
            Assert.DoesNotThrow(Act);
        }

        [Test]
        public void Given_ConsumedAwaitableCompletionSourceVoid_When_SecondGetResultSameFrame_Then_ThrowsInvalidOperationException()
        {
            // Arrange
            var source = new AwaitableCompletionSource();
            source.SetResult();
            var awaitable = source.Awaitable;
            awaitable.GetAwaiter().GetResult();

            // Act
            Exception? caught = null;
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
    }
}
