using System.Collections;
using System.Reflection;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the per-context stacks <see cref="ComponentContextStack"/> keeps beside the values on them: a stack
    /// a context's last value left behind is handed to the next context pushed, rather than allocated anew.
    /// </summary>
    [TestFixture]
    internal sealed class ComponentContextStackTests
    {
        // Non-generic, so the cast does not name the type of what the stacks hold.
        private static IDictionary StacksOf(ComponentContextStack stack)
            => (IDictionary)typeof(ComponentContextStack)
                .GetField("_stacks", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(stack)!;

        // GREEN_ON_BASE(refactor): the base already hands a context's emptied stack to the next context pushed.
        // The stacks now hold each value with the sequence it was pushed at, and still pass from context to context.
        [Test]
        public void Given_AContextPushedAndPopped_When_AnotherContextIsPushed_Then_ItTakesTheStackTheFirstLeft()
        {
            // Arrange
            var stack = new ComponentContextStack();
            var first = ComponentContext<int>.Create();
            var second = ComponentContext<int>.Create();
            stack.Push(first, 1);
            var left = StacksOf(stack)[first];
            stack.Pop(first);

            // Act
            stack.Push(second, 2);

            // Assert
            Assert.That(StacksOf(stack)[second], Is.SameAs(left));
        }
    }
}
