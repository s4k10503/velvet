using System;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Specifies the contract of <see cref="FiberStack"/>, the cursor that tracks the currently rendering
    /// <see cref="ComponentFiber"/> during a reconcile pass.
    /// <list type="bullet">
    /// <item>A fresh stack has no current fiber.</item>
    /// <item><c>Push</c> makes the pushed fiber the current one; with several pushed, the most recently pushed
    /// fiber is current (last-in, first-out).</item>
    /// <item><c>Pop</c> restores the previously pushed fiber as current, and emptying the stack returns the
    /// current fiber to none.</item>
    /// <item>Pushing a null fiber is rejected with <see cref="ArgumentNullException"/>.</item>
    /// <item>Popping an empty stack is rejected with <see cref="InvalidOperationException"/>.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    internal sealed class FiberStackTests
    {
        [Test]
        public void Given_FreshStack_When_Inspected_Then_HasNoCurrentFiber()
        {
            // Act
            var stack = new FiberStack();

            // Assert
            Assert.That(stack.Current, Is.Null);
        }

        [Test]
        public void Given_EmptyStack_When_OneFiberPushed_Then_ThatFiberIsCurrent()
        {
            // Arrange
            var stack = new FiberStack();
            var fiber = new ComponentFiber();

            // Act
            stack.Push(fiber);

            // Assert
            Assert.That(stack.Current, Is.SameAs(fiber));
        }

        [Test]
        public void Given_TwoFibersPushed_When_Inspected_Then_LastPushedIsCurrent()
        {
            // Arrange
            var stack = new FiberStack();
            var outer = new ComponentFiber();
            var inner = new ComponentFiber();

            // Act
            stack.Push(outer);
            stack.Push(inner);

            // Assert
            Assert.That(stack.Current, Is.SameAs(inner));
        }

        [Test]
        public void Given_TwoFibersPushed_When_Popped_Then_PreviousFiberBecomesCurrent()
        {
            // Arrange
            var stack = new FiberStack();
            var outer = new ComponentFiber();
            var inner = new ComponentFiber();
            stack.Push(outer);
            stack.Push(inner);

            // Act
            stack.Pop();

            // Assert
            Assert.That(stack.Current, Is.SameAs(outer));
        }

        [Test]
        public void Given_SingleFiberPushed_When_PoppedToEmpty_Then_HasNoCurrentFiber()
        {
            // Arrange
            var stack = new FiberStack();
            stack.Push(new ComponentFiber());

            // Act
            stack.Pop();

            // Assert
            Assert.That(stack.Current, Is.Null);
        }

        [Test]
        public void Given_AnyStack_When_NullFiberPushed_Then_ThrowsArgumentNullException()
        {
            // Arrange
            var stack = new FiberStack();

            // Act + Assert
            Assert.Throws<ArgumentNullException>(() => stack.Push(null));
        }

        [Test]
        public void Given_EmptyStack_When_Popped_Then_ThrowsInvalidOperationException()
        {
            // Arrange
            var stack = new FiberStack();

            // Act + Assert
            Assert.Throws<InvalidOperationException>(() => stack.Pop());
        }
    }
}
