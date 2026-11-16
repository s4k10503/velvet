using System.Collections.Generic;
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
        private static Dictionary<object, Stack<object>> StacksOf(ComponentContextStack stack)
            => (Dictionary<object, Stack<object>>)typeof(ComponentContextStack)
                .GetField("_stacks", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(stack)!;

        // GREEN_ON_BASE(characterization): the base already hands a context's emptied stack to the next context pushed.
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
