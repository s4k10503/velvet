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
    }
}
