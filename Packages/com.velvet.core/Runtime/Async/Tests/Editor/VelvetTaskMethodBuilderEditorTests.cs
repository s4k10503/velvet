using System;
using System.Reflection;
using NUnit.Framework;

namespace Velvet.Tests
{
    internal sealed class VelvetTaskMethodBuilderEditorTests
    {
        static readonly FieldInfo VelvetTaskSourceField =
            typeof(VelvetTask).GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic)!;

        static object? GetTaskSource(VelvetTask task) =>
            VelvetTaskSourceField.GetValue(task);

        static async VelvetTask CancelBeforeSuspending(OperationCanceledException own)
        {
            await VelvetTask.CompletedTask;
            throw own;
        }

        static async VelvetTask<int> CancelWithResultBeforeSuspending(OperationCanceledException own)
        {
            await VelvetTask.CompletedTask;
            throw own;
        }

        static Exception? ThrownBy(Action action)
        {
            try
            {
                action();
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        [Test]
        public void Given_BuilderWithExceptionBeforeFirstYield_When_TaskReadTwice_Then_ReturnsSameSource()
        {
            // Arrange
            var builder = VelvetTaskMethodBuilder.Create();
            builder.SetException(new InvalidOperationException("fail"));
            var firstTask = builder.Task;

            // Act
            var secondTask = builder.Task;

            // Assert
            Assert.That(GetTaskSource(secondTask), Is.SameAs(GetTaskSource(firstTask)));
        }

        [Test]
        public void Given_AnAsyncMethodThatThrowsItsOwnCancellationBeforeSuspending_When_ItsTaskIsConsumed_Then_ThrowsThatException()
        {
            // Arrange
            var own = new OperationCanceledException("own");
            var task = CancelBeforeSuspending(own);

            // Act
            var thrown = ThrownBy(() => task.GetAwaiter().GetResult());

            // Assert
            Assert.That(thrown, Is.SameAs(own));
        }

        [Test]
        public void Given_AnAsyncMethodWithAResultThatThrowsItsOwnCancellationBeforeSuspending_When_ItsTaskIsConsumed_Then_ThrowsThatException()
        {
            // Arrange
            var own = new OperationCanceledException("own");
            var task = CancelWithResultBeforeSuspending(own);

            // Act
            var thrown = ThrownBy(() => task.GetAwaiter().GetResult());

            // Assert
            Assert.That(thrown, Is.SameAs(own));
        }
    }
}
