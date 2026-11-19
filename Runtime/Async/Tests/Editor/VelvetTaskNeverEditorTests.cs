using System;
using System.Reflection;
using System.Threading;
using NUnit.Framework;

namespace Velvet.Tests
{
    internal sealed class VelvetTaskNeverEditorTests
    {
        static readonly FieldInfo VelvetTaskSourceField =
            typeof(VelvetTask).GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic)!;

        static object GetTaskSource(VelvetTask task) =>
            VelvetTaskSourceField.GetValue(task)!;

        [Test]
        public void Given_NeverWithCancelableToken_When_Cancelled_Then_OperationCanceledExceptionCarriesRegisteredToken()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            var task = VelvetTask.Never(cts.Token);

            // Act
            cts.Cancel();
            var thrown = Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());

            // Assert
            Assert.That(thrown!.CancellationToken, Is.EqualTo(cts.Token));
        }

        [Test]
        public void Given_GenericNeverWithCancelableToken_When_Cancelled_Then_OperationCanceledExceptionCarriesRegisteredToken()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            var task = VelvetTask.Never<int>(cts.Token);

            // Act
            cts.Cancel();
            var thrown = Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());

            // Assert
            Assert.That(thrown!.CancellationToken, Is.EqualTo(cts.Token));
        }
    }
}
